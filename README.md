# PC Health Dashboard

*Read in Vietnamese: [README-vi](README-vi.md).*

> **Understand your PC at a glance.**

PC Health Dashboard is a Windows desktop app that brings everyday hardware and system readings into one place. It helps you see CPU and graphics load, temperature readings, memory use, drive capacity, and network throughput, with a few built-in maintenance tools for common tasks.

## Why

When a PC feels hot, slow, or unstable, useful clues are often spread across Task Manager, hardware utilities, and Windows settings. This dashboard gathers common readings and alerts in one view so users can spot changes quickly and decide what to inspect next.

Its health score is a summary of selected readings, not a certified diagnosis of the computer or its components.

## Who

The app is for Windows PC owners who want a straightforward system overview. Gamers, developers, creators, and PC enthusiasts can use it to observe system load and temperatures while playing, working, or troubleshooting. It is intended for everyday checks and does not replace vendor diagnostics or a full task manager.

## What

- **CPU and graphics:** current load and the temperatures exposed by available sensors.
- **Memory:** used and total RAM, with an optional RAM maintenance window.
- **Storage:** system-drive capacity. The displayed `SSD Health` value is currently a placeholder; this source version does **not** read SMART/NVMe wear data or report SSD lifespan.
- **Network:** download/upload rate and a short traffic chart. The visible ping and packet-loss fields are not connected to live updates in this source version.
- **System status:** a health score with selected temperature, load, memory, and low-storage alerts. Its network subscore is fixed and its SSD-health input is a default, so it cannot assess actual network quality or drive wear.
- **Maintenance and display:** RAM maintenance, temporary-file cleanup, a desktop widget, and compact display mode.

## How

The app is built with WPF on .NET. It periodically reads CPU/GPU sensors through LibreHardwareMonitor, obtains memory and drive-capacity readings through Windows interfaces, and samples network throughput. Recent chart points are kept in memory rather than continuously written to disk.

Sensor availability depends on the PC, firmware, Windows version, and driver. Some GPU or temperature fields may be unavailable or use a fallback reading. The app manifest requests Administrator privileges, so Windows shows a UAC prompt at launch.

## Requirements and build

- Windows x64.
- Running the app requires accepting the Administrator/UAC prompt.
- Building from source requires the **.NET 10 SDK** and an internet connection for the initial NuGet restore. A framework-dependent build requires the .NET 10 Desktop Runtime on the target PC; the self-contained publish command below bundles the runtime.

```powershell
dotnet restore PCHealthDashboard.slnx
dotnet publish PCHealthDashboard.csproj -c Release -r win-x64 --self-contained true -o Publish
```

Zip the complete `Publish` folder for a portable build. To compile the installer, install **Inno Setup 6** and open `setup.iss`; output is placed in `Installer`.

## License

MIT. See [LICENSE](LICENSE).
