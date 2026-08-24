# PC Health Dashboard

*Read this in other languages: [Tiếng Việt](README-vi.md).*

PC Health Dashboard is a high-performance, system-level task manager and hardware monitoring tool for Windows. It provides real-time telemetry, deep memory optimization, and a beautiful UI, designed with a focus on zero-allocation architectures and hardware longevity.

## Key Features
- **Hardware Monitoring**: Real-time tracking of CPU, GPU, RAM, Disk, and Network usage using `LibreHardwareMonitorLib`.
- **Health Score**: A smart 0-100 system health score calculated via an EWMA (Exponentially Weighted Moving Average) algorithm to filter out temporary spikes.
- **Zero-Disk-Wear Telemetry**: Metric history is stored entirely in RAM using a custom thread-safe `RingBuffer`, preventing unnecessary write cycles on your SSD.
- **Deep RAM Optimization**: Utilizes low-level Windows NT kernel APIs (`NtSetSystemInformation`) to purge Standby Lists and Modified Page Lists safely, returning memory to the system exactly like Sysinternals RAMMap, without freezing the UI.
- **Safe Disk Cleaner**: Cleans system junk, browser caches, and temp files with graceful handling of locked files and system integrity protections.
- **High-Performance UI**: Hardware charts are rendered directly to memory using `SkiaSharp` (60 FPS, eliminating WPF Polyline overhead) against a native Windows 11 Mica/Acrylic transparent backdrop via DWM APIs.
- **Cryo Mode & Widget**: The app can shrink into a lightweight desktop widget or enter "Cryo Mode" in the System Tray, reducing CPU usage to <1% while still monitoring in the background.
