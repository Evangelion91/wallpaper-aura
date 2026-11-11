# Wallpaper Aura LCD for Phanteks LCD6-HD

A separate Windows controller for the **Phanteks LCD6-HD**, USB VID `1f3a`, PID `6502`, as used in the reference EX5 Pro setup. It sends MSI Afterburner metrics to the screen and optionally creates a soft, mirrored background based on the current Windows wallpaper. It does not control case LEDs, fans, pumps, or firmware. This is an independent community project.

## Requirements and limits

- Windows x64, .NET 8 runtime, Python 3.10+, and MSI Afterburner running with monitoring enabled. CPU temperature, GPU 0 temperature and power, and RAM usage must be available through Afterburner's shared memory. Missing or stale readings pause updates, leaving the last values on screen.
- Configure the layout and image-background mode in Phanteks NexLinq first. Close NexLinq before starting this tool; do not use both simultaneously. This tool reads `%APPDATA%\Phanteks\configs.xml`; it does not distribute or edit that configuration. Back up your XML and original background before starting.
- **Background mode supports only the tested landscape layout 9: six specific CPU/GPU temperature, RAM, GPU power, CPU graph and GPU graph widgets, image background, disabled alarms and the tested gauge colours.** It validates the saved configuration before writing. Other layouts, some colours, non-ASCII labels and orientations are not supported in background mode. The check reads the saved NexLinq profile and cannot confirm that it still matches the layout stored on the device. Telemetry-only mode leaves the layout alone.
- Every background update uploads an image and reapplies the six saved widget packets. Graph history resets when the wallpaper changes; live values resume. The background asset on the device is replaced. Its storage endurance is not established, so avoid unnecessarily frequent updates.
- Tested with the specified device and one NVIDIA GPU. Other displays and multi-GPU configurations are not supported. Images are processed locally; wallpaper paths are logged.

## Install and run the release

Download `lcd6hd-windows-x64.zip` from the [LCD release](https://github.com/Evangelion91/wallpaper-aura/releases/tag/lcd-v0.1.0), unpack it to a user-writable folder, and open PowerShell there. Install the [.NET 8 runtime](https://dotnet.microsoft.com/download/dotnet/8.0) if needed. Create a Python environment and install the worker dependencies:

```powershell
py -3 -m venv .venv
.\.venv\Scripts\python.exe -m pip install "Pillow>=10,<14" "comtypes>=1.4,<2"
```

Review your saved configuration without communicating with the device. Replace `64` with your installed RAM in GiB and `600` with the GPU power scale in watts for the layout:

```powershell
.\NexLcd.exe --check-layout --ram-gib=64 --gpu-max-watts=600
.\NexLcd.exe --self-test
```

If the layout is incompatible, do not use background mode. After closing NexLinq, start the controller:

```powershell
.\NexLcd.exe --send --background --python="$PWD\.venv\Scripts\python.exe" --ram-gib=64 --gpu-max-watts=600
```

For telemetry only, run `.\NexLcd.exe --send`. Stop with Ctrl+C or, from another terminal in the same Windows session, `.\NexLcd.exe --stop`. No scheduled task is created. If you configure autostart, test interactively first and ensure Afterburner starts before the controller. To return to NexLinq, stop this controller and launch NexLinq; reapply the desired background and layout if required.

`--list` shows Afterburner sensor entries and LCD interfaces when no other instance of this controller is running. `--seconds=N` limits a test run. Optional `--log=C:\path\file.log` writes a rolling log instead of console output. The Python worker logs its wallpaper path to the process console.

## Build from source

```powershell
dotnet publish .\NexLcd.csproj -c Release -r win-x64 --self-contained false -o .\out
.\out\NexLcd.exe --self-test
py -3 -m pip install "Pillow>=10,<14" "comtypes>=1.4,<2"
py -3 -m unittest discover -s tests -v
```

The build restores HidSharp 2.6.4 from NuGet. The Python helper is copied into the publish directory. No Phanteks binaries or user settings are included. Source code is MIT licensed; dependencies have their own licenses.
