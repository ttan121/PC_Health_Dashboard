# PC Health Dashboard

*Read this in other languages: [Tiếng Việt](README-vi.md).*

![PC Health Dashboard](https://via.placeholder.com/800x450.png?text=Add+Screenshot+Here)

PC Health Dashboard is a high-performance, system-level task manager and hardware monitoring tool for Windows. It provides real-time telemetry, deep memory optimization, and a beautiful UI, designed with a focus on zero-allocation architectures and hardware longevity.

## Table of Contents
- [Key Features](#key-features)
- [Technology Stack](#technology-stack)
- [Installation](#installation)
- [How It Works](#how-it-works)
- [Contributing](#contributing)
- [License](#license)

## Key Features
- **Real-time Hardware Monitoring**: Tracks CPU, GPU, RAM, Disk, and Network usage accurately using the \LibreHardwareMonitorLib\ engine.
- **Smart Health Score**: Provides a 0-100 system health score calculated via an EWMA (Exponentially Weighted Moving Average) algorithm. This effectively filters out temporary temperature spikes, giving you a smooth and realistic health assessment.
- **Zero-Disk-Wear Telemetry**: Metric history is stored entirely in RAM using a custom thread-safe \RingBuffer\. Unlike traditional monitoring tools, it prevents unnecessary write cycles, protecting your SSD's lifespan.
- **Deep RAM Optimization**: Utilizes low-level Windows NT kernel APIs (\NtSetSystemInformation\) to purge Standby Lists and Modified Page Lists safely. It returns memory to the system exactly like Sysinternals RAMMap, all without freezing the UI.
- **Safe Disk Cleaner**: Cleans system junk, browser caches, and temp files with graceful handling of locked files and system integrity protections.
- **High-Performance UI**: Hardware charts are rendered directly to memory using \SkiaSharp\ (running at 60 FPS, completely eliminating WPF Polyline overhead) against a native Windows 11 Mica/Acrylic transparent backdrop via DWM APIs.
- **Cryo Mode & System Tray Widget**: The app can shrink into a lightweight desktop widget or enter "Cryo Mode" in the System Tray. In this mode, it reduces CPU usage to <1% while still monitoring telemetry in the background.

## Technology Stack
- **Framework**: .NET 10, Windows Presentation Foundation (WPF)
- **Architecture**: MVVM (Model-View-ViewModel)
- **Graphics**: SkiaSharp for ultra-fast, zero-allocation 2D rendering
- **Hardware Interop**: LibreHardwareMonitorLib, direct P/Invoke to Windows NT Kernel and Shell32
- **UI Styling**: Custom XAML styles with Windows 11 DWM (Desktop Window Manager) Mica/Acrylic integration

## Installation
### Option 1: Using the Installer
1. Go to the [Releases](https://github.com/ttan121/PC_Health_Dashboard/releases) page.
2. Download \PCHealthDashboard_Setup.exe\.
3. Run the installer and follow the on-screen instructions.
4. Launch the application as an **Administrator** (required for deep RAM cleaning APIs and hardware sensor access).

### Option 2: Portable Version
1. Download \PCHealthDashboard_Portable.zip\ from the Releases page.
2. Extract the folder to your preferred location.
3. Run \PCHealthDashboard.exe\ as an **Administrator**.

### Building from Source
1. Clone the repository:
   \\\ash
   git clone https://github.com/ttan121/PC_Health_Dashboard.git
   \\\
2. Publish the project:
   \\\ash
   cd PC_Health_Dashboard
   dotnet publish -c Release -r win-x64 -o Publish
   \\\
3. Use Inno Setup to compile \setup.iss\ to generate the installer.

## How It Works
Unlike traditional optimizers that use the \EmptyWorkingSet\ API (which just forces apps to page to disk and slows down your PC), PC Health Dashboard interacts directly with the NT Kernel to clear the **Standby List**. This means it only frees up cached memory that isn't actively being used, leaving your running applications perfectly smooth.

## Contributing
Pull requests are welcome! If you have suggestions for improvements, bug fixes, or new features, please open an issue first to discuss what you would like to change.

## License
Distributed under the MIT License. See \LICENSE\ for more information.