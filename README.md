# 💻 PC Health Dashboard

**PC Health Dashboard** is a modern, high-performance system monitoring application built with **.NET 10** and **WPF**. Designed with a premium dark-themed UI and fluid animations, it provides real-time insights into your computer's vital hardware statistics.

Whether you are gaming, rendering, or simply keeping an eye on your system, PC Health Dashboard delivers precise, synchronized hardware data in a stunning visual interface.

---

## ✨ Key Features

- **📊 Comprehensive Monitoring:** Tracks real-time temperature, load, and usage for CPU, GPU, RAM, and Storage using the powerful `LibreHardwareMonitor` engine.
- **🌐 Network Analytics:** Accurately measures live upload/download speeds (Mbps), latency (ping), and packet loss.
- **💯 Intelligent Health Score:** Automatically calculates an overall "Health Score" for your PC based on temperatures, free space, and network stability.
- **🎨 Premium UI/UX:** Features a sleek, modern dark mode with glassmorphism elements, dynamic gradients, and smooth native WPF sparkline charts (zero heavy dependencies).
- **📌 Widget (Popup) & Compact Mode:** Includes a compact, always-on-top, translucent widget. The widget syncs flawlessly with the main dashboard, allowing you to monitor your system seamlessly while inside other full-screen apps or games.
- **⚡ Zero Lag & Low Overhead:** Highly optimized asynchronous data polling and "Cryo Mode" ensures smooth updates 24/7 while consuming less than 1% CPU without bogging down your machine.

---

## 📸 Screenshots

**Main Dashboard**
<img width="934" height="609" alt="{A2C99F69-1902-4608-B7E8-C2E0F408FEA7}" src="https://github.com/user-attachments/assets/c60f1999-5c23-4da7-b66a-f8dc98eecf68" />


**KittyWindow (Always-on-top Widget)**
<img width="363" height="321" alt="{A98E0C17-4455-46F0-932D-9EB820F3D7BC}" src="https://github.com/user-attachments/assets/51ae0cd3-f757-492e-ab0a-68f82279c6d3" />


---

## 🛠️ Technology Stack

- **Framework:** .NET 10.0 (WPF)
- **Architecture:** MVVM (Model-View-ViewModel) via `CommunityToolkit.Mvvm`
- **Hardware Tracking:** `LibreHardwareMonitorLib`

---

## 🚀 Getting Started

### Prerequisites
- Windows 10 or Windows 11
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download)
- The app must be run as **Administrator** so that `LibreHardwareMonitor` can access low-level CPU/GPU hardware sensors.

### Build from source
1. Clone the repository:
   ```bash
   git clone https://github.com/ttan121/PC_Health_Dashboard.git
   ```
2. Open the solution in **Visual Studio 2022** or **JetBrains Rider**.
3. Restore NuGet packages and Build the project.
4. Run the application (Ensure you start Visual Studio as Administrator for hardware sensors to work).

---

## ⌨️ Shortcuts

- **Minimize to Tray / Hide:** Click the minimize button or close button on the main dashboard to send it to the System Tray.
- **Toggle KittyWindow (Widget):** Press `Ctrl + Shift + Space` globally to show/hide the floating widget.
- **Toggle Compact Mode:** Press `Ctrl + Shift + Alt + Space` globally to switch between Full Dashboard and Compact UI.

---

## 🤝 Contributing

Contributions, issues, and feature requests are welcome! Feel free to check the [issues page](https://github.com/ttan121/PC_Health_Dashboard/issues).

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
