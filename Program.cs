using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;

using var singleInstance = new Mutex(initiallyOwned: true, name: @"Global\ComfyTray.SingleInstance", createdNew: out var isFirstInstance);
if (!isFirstInstance) return;

Application.EnableVisualStyles();
Application.SetCompatibleTextRenderingDefault(false);
Application.Run(new TrayAppContext(args));

sealed class TrayAppContext : ApplicationContext
{
    private static readonly Regex GuiUrlPattern = new(@"To see the GUI go to:\s+(https?://\S+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly NotifyIcon _tray;
    private StartupLogForm? _startupLogForm;
    private Process? _process;
    private StreamWriter? _stdoutLog;
    private StreamWriter? _stderrLog;
    private bool _hasOpenedBrowser;
    private bool _startupSucceeded;
    private bool _exitWhenStartupLogCloses;
    private bool _isExiting;

    private readonly string _workdir;
    private readonly string _host;
    private readonly int _port;
    private readonly bool _lowvram;
    private readonly string _logDir;
    private readonly SynchronizationContext _uiContext;
    private string? _startupCommandDescription;

    public TrayAppContext(string[] args)
    {
        _workdir = GetArg(args, "--path") ?? AppContext.BaseDirectory;
        _host = GetArg(args, "--host") ?? GetArg(args, "--ip") ?? "127.0.0.1";
        _port = int.TryParse(GetArg(args, "--port"), out var p) ? p : 8188;
        _lowvram = args.Contains("--lowvram");
        _logDir = Path.Combine(AppContext.BaseDirectory, "logs");
        _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open ComfyUI", null, (_, _) => OpenBrowser());
        menu.Items.Add("Open Logs", null, (_, _) => OpenLogs());
        menu.Items.Add("Restart", null, (_, _) => Restart());
        menu.Items.Add("Exit", null, (_, _) => Exit());

        _tray = new NotifyIcon
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application,
            Text = "ComfyUI Tray",
            Visible = true,
            ContextMenuStrip = menu
        };

        _tray.DoubleClick += (_, _) => OpenBrowser();

        Start();
    }

    private void Start()
    {
        if (_process is { HasExited: false }) return;

        _hasOpenedBrowser = false;
        _startupSucceeded = false;
        _exitWhenStartupLogCloses = false;
        ShowStartupLogForm();

        Directory.CreateDirectory(_logDir);

        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        _stdoutLog = new StreamWriter(Path.Combine(_logDir, $"comfyui-{timestamp}.out.log"), append: false) { AutoFlush = true };
        _stderrLog = new StreamWriter(Path.Combine(_logDir, $"comfyui-{timestamp}.err.log"), append: false) { AutoFlush = true };

        var psi = CreateComfyProcessStartInfo();
        if (psi is null)
        {
            var activatePath = Path.Combine(_workdir, ".venv", "Scripts", "activate.bat");
            HandleOutputLine($"[ComfyTray] Working directory: {_workdir}", _stderrLog);
            HandleOutputLine("[ComfyTray] Could not find uv in PATH.", _stderrLog);
            HandleOutputLine($"[ComfyTray] Could not find virtual environment activation script: {activatePath}", _stderrLog);
            return;
        }

        HandleOutputLine($"[ComfyTray] Working directory: {_workdir}", _stdoutLog);
        HandleOutputLine($"[ComfyTray] Startup command: {_startupCommandDescription}", _stdoutLog);

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null) HandleOutputLine(e.Data, _stdoutLog);
        };
        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) HandleOutputLine(e.Data, _stderrLog);
        };
        _process.Exited += (_, _) =>
        {
            _tray.Text = "ComfyUI Exited";
            AppendStartupLog("[ComfyTray] ComfyUI exited.");
            if (!_startupSucceeded && !_isExiting)
            {
                _exitWhenStartupLogCloses = true;
            }
        };

        try
        {
            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            HandleOutputLine($"[ComfyTray] Failed to start ComfyUI: {ex.Message}", _stderrLog);
            _process.Dispose();
            _process = null;
            return;
        }

        _tray.Text = $"ComfyUI: http://{_host}:{_port}";
    }

    private ProcessStartInfo? CreateComfyProcessStartInfo()
    {
        var comfyArgs = new List<string>
        {
            "main.py",
            "--listen",
            _host,
            "--port",
            _port.ToString()
        };
        if (_lowvram) comfyArgs.Add("--lowvram");

        var uvPath = FindCommandInPath("uv");
        if (uvPath is not null)
        {
            var psi = CreateBaseProcessStartInfo(uvPath);
            psi.Arguments = "run python " + JoinCmdArguments(comfyArgs);
            _startupCommandDescription = $"{uvPath} run python {JoinDisplayArguments(comfyArgs)}";
            return psi;
        }

        var activatePath = Path.Combine(_workdir, ".venv", "Scripts", "activate.bat");
        if (!File.Exists(activatePath)) return null;

        var pythonPath = Path.Combine(_workdir, ".venv", "Scripts", "python.exe");
        var cmd = $"call {QuoteCmdArgument(activatePath)} && python {JoinCmdArguments(comfyArgs)}";
        var venvPsi = CreateBaseProcessStartInfo("cmd.exe");
        venvPsi.Arguments = "/d /c " + cmd;
        _startupCommandDescription = $"cmd.exe /d /c call {activatePath} && python {JoinDisplayArguments(comfyArgs)}";
        if (File.Exists(pythonPath))
        {
            _startupCommandDescription += $" (venv python: {pythonPath})";
        }

        return venvPsi;
    }

    private ProcessStartInfo CreateBaseProcessStartInfo(string fileName)
    {
        return new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = _workdir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
    }

    private void Restart()
    {
        Stop();
        Start();
    }

    private void Stop()
    {
        try
        {
            if (_process is { HasExited: false })
            {
                KillProcessTree(_process.Id);
                _process.WaitForExit(5000);
            }

            KillTcpListenersOnPort(_port);
        }
        catch
        {
            // 退出阶段忽略进程已结束等情况
        }
        finally
        {
            _process?.Dispose();
            _process = null;
            _stdoutLog?.Dispose();
            _stdoutLog = null;
            _stderrLog?.Dispose();
            _stderrLog = null;
        }
    }

    private void HandleOutputLine(string line, StreamWriter? logWriter)
    {
        logWriter?.WriteLine(line);
        AppendStartupLog(line);

        var match = GuiUrlPattern.Match(line);
        if (!match.Success || _hasOpenedBrowser) return;

        _hasOpenedBrowser = true;
        _startupSucceeded = true;
        _exitWhenStartupLogCloses = false;
        var url = match.Groups[1].Value;
        AppendStartupLog("This window will close in 5 seconds.");
        AppendStartupLog("Startup succeeded ^_^");

        _ = Task.Delay(TimeSpan.FromSeconds(5)).ContinueWith(_ =>
        {
            RunOnUiThread(() => CloseStartupLogAndOpenBrowser(url));
        });
    }

    private void CloseStartupLogAndOpenBrowser(string url)
    {
        CloseStartupLogForm();

        try
        {
            OpenBrowser(url);
        }
        catch
        {
            ShowStartupLogForm();
            AppendStartupLog("[ComfyTray] Failed to open browser automatically. Use tray menu to open it.");
        }
    }

    private void ExitFromStartupFailure()
    {
        _exitWhenStartupLogCloses = false;
        RunOnUiThread(() =>
        {
            Exit();
        });
    }

    private void OpenBrowser()
    {
        var urlHost = _host == "0.0.0.0" ? "127.0.0.1" : _host;
        OpenBrowser($"http://{urlHost}:{_port}");
    }

    private static void OpenBrowser(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    private void OpenLogs()
    {
        Directory.CreateDirectory(_logDir);
        Process.Start(new ProcessStartInfo
        {
            FileName = _logDir,
            UseShellExecute = true
        });
    }

    private void Exit()
    {
        _isExiting = true;
        _exitWhenStartupLogCloses = false;
        Stop();
        _tray.Visible = false;
        _tray.Dispose();
        CloseStartupLogForm();
        Application.Exit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Stop();
            _tray.Dispose();
            _startupLogForm?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void ShowStartupLogForm()
    {
        _startupLogForm?.Dispose();
        _startupLogForm = new StartupLogForm();
        _startupLogForm.FormClosed += (_, _) =>
        {
            _startupLogForm = null;
            if (_exitWhenStartupLogCloses && !_isExiting)
            {
                ExitFromStartupFailure();
            }
        };
        _startupLogForm.Show();
    }

    private void AppendStartupLog(string line)
    {
        var form = _startupLogForm;
        form?.BeginInvokeIfNeeded(() => form.AppendLine(line));
    }

    private void CloseStartupLogForm()
    {
        var form = _startupLogForm;
        form?.BeginInvokeIfNeeded(form.Close);
    }

    private void RunOnUiThread(Action action)
    {
        if (SynchronizationContext.Current == _uiContext)
        {
            action();
            return;
        }

        _uiContext.Post(_ => action(), null);
    }

    private static string? FindCommandInPath(string command)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path)) return null;

        var pathExtensions = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD")
            .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(extension => extension.Trim())
            .Where(extension => extension.Length > 0);

        foreach (var directory in path.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries).Select(directory => directory.Trim()).Where(directory => directory.Length > 0))
        {
            foreach (var extension in pathExtensions)
            {
                var fileName = command.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
                    ? command
                    : command + extension;
                var candidate = Path.Combine(directory, fileName);
                if (File.Exists(candidate)) return candidate;
            }
        }

        return null;
    }

    private static string JoinCmdArguments(IEnumerable<string> args)
    {
        return string.Join(" ", args.Select(QuoteCmdArgument));
    }

    private static string JoinDisplayArguments(IEnumerable<string> args)
    {
        return string.Join(" ", args.Select(arg => arg.Contains(' ') ? QuoteCmdArgument(arg) : arg));
    }

    private static string QuoteCmdArgument(string arg)
    {
        return "\"" + arg.Replace("\"", "\\\"") + "\"";
    }

    private static string? GetArg(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return null;
    }

    private static void KillTcpListenersOnPort(int port)
    {
        foreach (var pid in GetTcpListenerPids(port))
        {
            if (pid == Process.GetCurrentProcess().Id) continue;

            try
            {
                using var process = Process.GetProcessById(pid);
                KillProcessTree(pid);
                process.WaitForExit(5000);
            }
            catch
            {
                // 退出阶段忽略进程已结束或无权限结束的情况
            }
        }
    }

    private static void KillProcessTree(int processId)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "taskkill.exe",
            Arguments = $"/PID {processId} /T /F",
            UseShellExecute = false,
            CreateNoWindow = true
        });
        process?.WaitForExit(5000);
    }

    private static IEnumerable<int> GetTcpListenerPids(int port)
    {
        var bufferSize = 0;
        var result = GetExtendedTcpTable(IntPtr.Zero, ref bufferSize, sort: true, AF_INET, TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_LISTENER, reserved: 0);
        if (result != ERROR_INSUFFICIENT_BUFFER || bufferSize <= 0) yield break;

        var buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            result = GetExtendedTcpTable(buffer, ref bufferSize, sort: true, AF_INET, TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_LISTENER, reserved: 0);
            if (result != NO_ERROR) yield break;

            var rowCount = Marshal.ReadInt32(buffer);
            var rowPtr = IntPtr.Add(buffer, sizeof(int));
            var rowSize = Marshal.SizeOf<MIB_TCPROW_OWNER_PID>();

            for (var i = 0; i < rowCount; i++)
            {
                var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(rowPtr);
                var localPort = (ushort)IPAddress.NetworkToHostOrder((short)row.LocalPort);
                if (localPort == port)
                {
                    yield return (int)row.OwningPid;
                }

                rowPtr = IntPtr.Add(rowPtr, rowSize);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private const int AF_INET = 2;
    private const uint NO_ERROR = 0;
    private const uint ERROR_INSUFFICIENT_BUFFER = 122;

    private enum TCP_TABLE_CLASS
    {
        TCP_TABLE_OWNER_PID_LISTENER = 3
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCPROW_OWNER_PID
    {
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;
        public uint RemoteAddr;
        public uint RemotePort;
        public uint OwningPid;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr tcpTable,
        ref int tcpTableLength,
        bool sort,
        int ipVersion,
        TCP_TABLE_CLASS tableClass,
        uint reserved);
}
