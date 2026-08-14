# PC Health Dashboard

**PC Health Dashboard** là giải pháp phần mềm giám sát hệ thống chuyên sâu dành riêng cho nền tảng Windows. Ứng dụng được sinh ra không nhằm mục đích thay thế Windows Task Manager, mà đóng vai trò là một "bước tiến hóa" hướng tới việc chẩn đoán rủi ro phần cứng, phục vụ đắc lực cho trải nghiệm của Game thủ (Gamer) và Người dùng chuyên nghiệp (Power User).

---

## 🎯 Vấn đề thực tế (Pain Points)

Trong quá trình sử dụng máy tính hàng ngày, người dùng phổ thông thường phải đối mặt với một "khoảng trống" lớn về thông tin hệ thống:
1. **Phát hiện sự cố quá muộn:** Đa số người dùng chỉ nhận ra máy tính có vấn đề khi thiết bị đã xuất hiện các biểu hiện tiêu cực rõ rệt như sụt giảm hiệu năng (giật lag), máy tự sập nguồn do quá nhiệt, hoặc ổ cứng hỏng hóc dẫn đến mất mát dữ liệu.
2. **Rào cản từ các công cụ hiện tại:** Các phần mềm giám sát chuyên sâu (như HWiNFO, MSI Afterburner) cung cấp quá nhiều thông số kỹ thuật phức tạp. Giao diện khô khan, khó hiểu và chủ yếu nhắm đến kỹ thuật viên, tạo rào cản với người dùng phổ thông.
3. **Tiêu tốn tài nguyên và gây gián đoạn:** Các ứng dụng giám sát hiện hành thường yêu cầu mở toàn màn hình hoặc cửa sổ lớn gây gián đoạn luồng làm việc. Đồng thời, việc chạy nền liên tục ngốn nhiều RAM và CPU, đi ngược lại mục đích tối ưu hóa máy tính.

---

## 💡 Điểm khác biệt cốt lõi (Unique Selling Points)

- **Trải nghiệm "Không gián đoạn" (Seamless Experience):** Xuất hiện dưới dạng popup nổi mượt mà (KittyWindow) bằng tổ hợp phím tắt, cho phép xem nhanh thông tin rồi tự động ẩn đi.
- **Hệ sinh thái All-in-One:** Tích hợp cả giám sát phần cứng (Nhiệt độ, độ chai SSD) và đánh giá chất lượng mạng (Ping/Network Latency) trong cùng một giao diện.
- **Quy đổi "Health Score":** Tự động tổng hợp và quy đổi sức khỏe máy tính thành điểm số trực quan kèm đánh giá bằng ngôn ngữ tự nhiên.
- **Thiết kế UI/UX tối giản:** Áp dụng hiệu ứng vật liệu mờ (Mica/Acrylic), bo góc tinh tế, tạo cảm giác ứng dụng là một thành phần nguyên bản (native) của Windows.

---

## 🛠 Kiến trúc & Công nghệ (Technology Stack)

Dự án sử dụng các công nghệ hiện đại nhất để đảm bảo hiệu suất tối đa. Dưới đây là bảng so sánh chuyên sâu kiến trúc của PC Health Dashboard so với Windows Task Manager:

| Tiêu chí | PC Health Dashboard | Windows Task Manager (Mặc định) |
|---|---|---|
| **Nền tảng / Ngôn ngữ** | **.NET 10 (C#) / WPF** - Khung lập trình hiện đại. | C++ / WinRT (Windows Runtime). |
| **Kiến trúc mã nguồn** | **MVVM** (CommunityToolkit.Mvvm) tách biệt Giao diện và Logic. | Win32 API Monolithic. |
| **Cơ chế đọc Dữ liệu** | Dùng **LibreHardwareMonitorLib** (Ring-0 / Kernel) đọc thanh ghi MSR, NVAPI, Super I/O cực chính xác. | Dùng WMI & Performance Counters. |
| **Kỹ thuật vẽ Biểu đồ** | Tự xây dựng (**WPF Native Polyline**), áp dụng **Data point decimation** thay vì dùng thư viện làm nặng máy. | Direct2D gốc. |
| **Xử lý Bất đồng bộ** | Tận dụng tối đa sync/await và Task.Run tách biệt luồng UI và thu thập dữ liệu (Tránh giật lag). | Đa luồng truyền thống. |
| **Tối ưu Tài nguyên**| Áp dụng **Cryo Mode**: Dừng vẽ UI khi thu nhỏ, ép mức ngốn CPU xuống dưới 1%. | Vẫn tiêu tốn tài nguyên khi thu nhỏ. |

---

## 🚀 10 Tính năng Cốt lõi (Core Features)

### 1. Theo dõi Ổ cứng (SSD/HDD)
Sử dụng thư viện LibreHardwareMonitor giao tiếp trực tiếp với vi điều khiển ổ cứng để lấy thông số **S.M.A.R.T**. Hiển thị dung lượng trống, nhiệt độ (°C), sức khỏe ổ đĩa (Health %). Tự động sinh cảnh báo nếu ổ hệ điều hành sắp đầy.

### 2. Theo dõi Vi xử lý (CPU)
Tích hợp cảm biến đo nhiệt độ thời gian thực (°C) và tính toán tổng mức độ sử dụng đa luồng (CPU Usage %). Minh họa trực quan trạng thái tải bằng Progress Bar tối giản.

### 3. Theo dõi Card đồ họa (Multi-GPU)
Giám sát nhiệt độ (°C) và mức tải thực tế (GPU Load %) của tất cả các GPU trong hệ thống (bao gồm iGPU và dGPU). Tính toán chính xác mức dung lượng bộ nhớ đồ họa (VRAM) đang bị chiếm dụng.

### 4. Theo dõi Bộ nhớ trong (RAM)
Thu thập dữ liệu trực tiếp từ Windows API để hiển thị tổng dung lượng RAM vật lý và lượng RAM khả dụng (GB). Trình bày dạng thanh Bar trực quan. Tích hợp tính năng **RAM Optimizer** để giải phóng bộ nhớ bằng EmptyWorkingSet API.

### 5. Phân tích Chất lượng mạng (Network)
Chạy luồng phân tích ngầm gửi gói tin ICMP Ping liên tục để đo **Độ trễ (Ping)** và **Tỷ lệ rớt mạng (Packet Loss %)**. Giám sát băng thông Upload/Download (Mbps).

### 6. Hiển thị Biểu đồ Lịch sử (Charting)
Sử dụng biểu đồ dạng đường (**Sparkline**) để liên tục nội suy dữ liệu lưu lượng mạng theo thời gian thực, nhưng lại dùng thanh Bar cho CPU, GPU, RAM để giữ giao diện gọn gàng.

### 7. Hệ thống Cảnh báo Bất thường (Smart Alerts)
Tích hợp bộ máy phân tích (Alert System) kiểm tra hệ thống ngầm 24/7. **Cảnh báo chủ động (Pop-up)** sẽ bật ngay khi nhiệt độ vượt ngưỡng nguy hiểm (85°C - 88°C) hoặc RAM đầy (>90%).

### 8. Bảng điều khiển thu nhỏ (KittyWindow / Widget)
Tích hợp cửa sổ tiện ích nhỏ luôn trôi nổi hoặc gọi nhanh qua phím tắt. Tính năng **Auto Resize** co giãn động theo nội dung. Phục vụ hoàn hảo cho việc ghim góc màn hình khi chơi game.

### 9. Chế độ tối ưu tài nguyên ngầm (Cryo Mode)
Kích hoạt trạng thái "Chết đông" khi ứng dụng được thu nhỏ xuống khay hệ thống (System Tray). Tự động giãn chu kỳ quét phần cứng và ngắt toàn bộ tác vụ render đồ họa UI. Đảm bảo phần mềm vẫn theo dõi an toàn 24/7 nhưng **CPU sử dụng loanh quanh 1%**.

### 10. Hệ thống chấm điểm sức khỏe (Health Score)
Hệ thống tự động chấm điểm tình trạng máy tính (Thang 0-100) dựa trên:
- **Định luật Amdahl (Nút thắt cổ chai):** Phạt điểm mạnh tay nếu SSD bị suy giảm tuổi thọ hoặc ổ đĩa quá đầy.
- **Phân cấp bộ nhớ (Thrashing):** Trừ điểm nặng khi RAM sử dụng trên 90%, dẫn đến hiện tượng tráo đổi trang (Paging) xuống ổ cứng.
- **Thermal Throttling (Ép xung do nhiệt):** Trừ điểm khi linh kiện (CPU/GPU) chạm mốc 85°C, ép hệ thống phải giảm xung nhịp.
