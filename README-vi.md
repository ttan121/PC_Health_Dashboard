# PC Health Dashboard

*Đọc bằng ngôn ngữ khác: [English](README.md).*

PC Health Dashboard là một công cụ quản lý tác vụ và giám sát phần cứng cấp độ hệ thống dành cho Windows. Phần mềm cung cấp thông số theo thời gian thực, tối ưu hóa bộ nhớ chuyên sâu và giao diện đẹp mắt, được thiết kế với tiêu chí tối ưu hiệu năng tuyệt đối và bảo vệ tuổi thọ phần cứng.

## Tính Năng Chính
- **Giám sát Phần cứng**: Theo dõi CPU, GPU, RAM, Ổ cứng và Mạng theo thời gian thực sử dụng `LibreHardwareMonitorLib`.
- **Chấm điểm Sức khỏe**: Điểm sức khỏe hệ thống từ 0-100 được tính toán thông minh qua thuật toán EWMA để loại bỏ các biến động nhiệt độ ảo.
- **Chống chai SSD (Zero-Disk-Wear)**: Lịch sử thông số được lưu hoàn toàn trên RAM bằng cấu trúc `RingBuffer`, ngăn chặn việc ghi dữ liệu liên tục làm giảm tuổi thọ ổ cứng SSD.
- **Dọn RAM Chuyên sâu**: Sử dụng trực tiếp mã lệnh lõi của Windows NT (`NtSetSystemInformation`) để giải phóng triệt để vùng nhớ Standby List và Modified Page List mà không gây đứng hình, tương tự như phần mềm RAMMap của Microsoft.
- **Dọn rác Ổ đĩa An toàn**: Xóa rác hệ thống, bộ nhớ đệm trình duyệt và file tạm với cơ chế bắt lỗi thông minh, tự động bỏ qua các file đang bị khóa mà không làm treo ứng dụng.
- **Giao diện Hiệu năng cao**: Biểu đồ phần cứng được vẽ trực tiếp lên bộ nhớ bằng thư viện `SkiaSharp` (đạt 60 FPS, mượt hơn nhiều so với WPF Polyline) kết hợp cùng hiệu ứng xuyên thấu Mica/Acrylic thế hệ mới của Windows 11.
- **Chế độ Ngủ đông (Cryo Mode) & Widget**: Ứng dụng có thể thu nhỏ thành Widget độc lập trên màn hình, hoặc thu hoàn toàn xuống System Tray. Ở chế độ này, ứng dụng giảm mức tiêu thụ CPU xuống dưới 1% nhưng vẫn âm thầm giám sát hệ thống.
