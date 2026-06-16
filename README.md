# ComfyTray

ComfyTray is a small Windows tray launcher for ComfyUI. It starts ComfyUI into Windows system tray.

> This is only a launcher. You need to python virtual environment in `.venv` by your self.

## Install

Download: [Go to Releases](../../releases)

Put `ComfyTray.exe` and `ComfyTray.exe.config` into ComfyUI working directory and click.

## Startup options

### Optional Arguments

```powershell
ComfyTray.exe --path <dir> --host <host> --port <port> --lowvram
```

| Argument | Default | Description |
| --- | --- | --- |
| `--path <dir>` | The directory containing `ComfyTray.exe` | ComfyUI working directory. This should contain `main.py`. |
| `--host <host>` | `127.0.0.1` | Host passed to ComfyUI `--listen`. |
| `--ip <host>` | `127.0.0.1` | Alias for `--host`. |
| `--port <port>` | `8188` | Port passed to ComfyUI `--port`. |
| `--lowvram` | Off | Passes `--lowvram` to ComfyUI. |

## Set Arguments From Windows GUI

You can configure startup arguments through a Windows shortcut:

1. Right-click `ComfyTray.exe`.
2. Choose `Create shortcut`.
3. Right-click the shortcut and choose `Properties`.
4. In the `Target` field, keep the exe path and append arguments after it.

Example `Target` value:

```text
"F:\AI\ComfyUI\ComfyTray\ComfyTray.exe" --path "F:\AI\ComfyUI\ComfyUI" --port 8188 --lowvram
```

The `Start in` field is not required for the default working directory. If `--path` is omitted, ComfyTray uses the directory containing `ComfyTray.exe`.

## Tray Menu

Right-click the tray icon to access:

- `Open ComfyUI`: opens the configured local ComfyUI URL.
- `Open Logs`: opens the `logs` folder next to `ComfyTray.exe`.
- `Restart`: stops and starts the ComfyUI process again.
- `Exit`: stops ComfyUI and exits ComfyTray.

## Logs

ComfyTray writes process output to:

```text
logs\
```

inside the directory containing `ComfyTray.exe`.

Each run creates separate stdout and stderr files:

```text
comfyui-YYYYMMDD-HHMMSS.out.log
comfyui-YYYYMMDD-HHMMSS.err.log
```

## Publish

Build the release executable with:

```powershell
.\scripts\publish-release.ps1
```

By default it builds the `.NET Framework 4.8` target and copies the release executable to:

```text
artifacts\release\ComfyTray.exe
```

The release does not bundle the .NET runtime.
