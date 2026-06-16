# AGENTS.md

## Project Overview

ComfyTray is a small Windows Forms tray application for launching and supervising ComfyUI.

The app has no main window. It creates a tray icon, starts ComfyUI in the background, shows a temporary startup log window, waits for ComfyUI to print its GUI URL, then opens that URL in the default browser.

## Main Files

- `Program.cs`: application entry point, single-instance guard, tray lifecycle, ComfyUI process startup, output handling, browser opening, and process cleanup.
- `StartupLogForm.cs`: small resizable startup log window used while ComfyUI is starting.
- `ComfyTray.csproj`: Windows Forms project configuration, `net48` target framework, and application icon.
- `scripts/publish-release.ps1`: release build script. It builds `net48` and copies the executable into `artifacts\release`.

## Runtime Flow

1. `Program.cs` creates a named mutex:

   ```text
   Global\ComfyTray.SingleInstance
   ```

   If another instance already owns the mutex, the second process exits immediately. Only one tray instance should run.

2. `Application.Run(new TrayAppContext(args))` starts the tray context.

3. `TrayAppContext` parses arguments:

   - `--path <dir>`: ComfyUI working directory. Defaults to `AppContext.BaseDirectory`, the directory containing `ComfyTray.exe`.
   - `--host <host>` / `--ip <host>`: host passed to ComfyUI `--listen`. Defaults to `127.0.0.1`.
   - `--port <port>`: port passed to ComfyUI `--port`. Defaults to `8188`.
   - `--lowvram`: passed through to ComfyUI.

4. The tray icon is created with these menu items:

   - `Open ComfyUI`
   - `Open Logs`
   - `Restart`
   - `Exit`

5. `Start()` creates a new startup log window and starts ComfyUI.

## Startup Command Selection

`CreateComfyProcessStartInfo()` chooses how to start ComfyUI.

1. If `uv` exists in the system `PATH`, use:

   ```text
   uv run python main.py --listen <host> --port <port>
   ```

2. If `uv` is not found, check the working directory for:

   ```text
   .venv\Scripts\activate.bat
   ```

   If it exists, use:

   ```text
   cmd.exe /d /c call .venv\Scripts\activate.bat && python main.py --listen <host> --port <port>
   ```

3. If neither startup method is available, write diagnostic errors to the startup log window.

Always keep the startup diagnostics. The log should show:

- working directory
- selected startup command
- resolved `uv.exe` path when using `uv`
- `.venv\Scripts\python.exe` path when available

## Startup Log Behavior

`StartupLogForm` is a resizable read-only log window. It is centered on screen and hidden from the taskbar.

ComfyUI stdout and stderr are both:

- appended to the startup log window
- written to timestamped files under `logs\` next to `ComfyTray.exe`

Ready detection is based on this pattern:

```text
To see the GUI go to: http://...
```

The line may or may not include an `[INFO]` prefix. Do not make this regex depend on `[INFO]`.

When the ready URL is detected:

1. Mark startup as successful.
2. Append these two lines:

   ```text
   This window will close in 5 seconds.
   Startup succeeded ^_^
   ```

3. Wait 5 seconds.
4. Close the startup log window.
5. Open the detected URL in the default browser.

Browser opening and window operations must run on the WinForms UI thread. Use `RunOnUiThread()`.

## Startup Failure Behavior

If the ComfyUI process exits before the ready URL is detected, append:

```text
[ComfyTray] ComfyUI exited.
```

Then mark the startup log window so that closing it exits the tray app. This prevents a failed startup from leaving an empty tray icon running.

Use `_isExiting` to distinguish user-initiated tray exit from startup failure cleanup.

## Stop, Restart, and Exit

`Restart()` calls:

```text
Stop()
Start()
```

`Exit()` sets `_isExiting`, stops ComfyUI, disposes the tray icon, closes the startup log window, and exits the application.

`Stop()` must:

1. Kill the tracked process tree with:

   ```csharp
   _process.Kill(entireProcessTree: true)
   ```

2. Also call `KillTcpListenersOnPort(_port)`.

The port cleanup is important because `uv`, `cmd.exe`, or Python can leave the actual listening Python process outside the original process tree. `KillTcpListenersOnPort()` uses `GetExtendedTcpTable` from `iphlpapi.dll` to find TCP listener PIDs for the configured port and terminate them.

Be careful with this fallback: it kills any process listening on the configured port, not only a process started by ComfyTray.

## Threading Notes

Process output events do not run on the UI thread.

Use:

- `StartupLogForm.BeginInvokeIfNeeded()` for log window updates.
- `RunOnUiThread()` for operations that close/create forms or open the browser after ready detection.

Do not directly manipulate WinForms controls from process output callbacks.

## Build and Release

The project targets `.NET Framework 4.8` (`net48`) so the release does not bundle the modern .NET runtime. Keep this model unless explicitly changing the distribution strategy.

Build locally with:

```powershell
dotnet build -c Release
```

Create the release output with:

```powershell
.\scripts\publish-release.ps1
```

The script writes:

```text
artifacts\release\ComfyTray.exe
```

Do not reintroduce `dotnet publish -r win-x64 --self-contained true` or `PublishSingleFile=true` unless the release is intentionally switching back to bundled-runtime distribution.

## Maintenance Notes

- Keep changes small and consistent with the current single-file tray context style unless the feature clearly needs a new module.
- Do not reintroduce a main form for normal runtime. `StartupLogForm` is only a temporary startup status window.
- Preserve `OutputType=WinExe`; the app should not open a console window.
- Preserve single-instance behavior unless explicitly changing the app model.
- If changing ready detection, test against ComfyUI lines with and without `[INFO]`.
- If changing process cleanup, consider both `uv` and `.venv\Scripts\activate.bat` startup modes.
