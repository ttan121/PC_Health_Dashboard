# PC Health Dashboard

*Đọc bằng ngôn ngữ khác: [English](README.md).*

![PC Health Dashboard](https://via.placeholder.com/800x450.png?text=Them+Anh+Man+Hinh+Tai+Day)

PC Health Dashboard là một công cụ quản lý tác vụ và giám sát phần cứng cấp độ hệ thống dành cho Windows. Phần mềm cung cấp thông số theo thời gian thực, tối ưu hóa bộ nhớ chuyên sâu và giao diện đẹp mắt, được thiết kế với tiêu chí tối ưu hiệu năng tuyệt đối và bảo vệ tuổi thọ phần cứng.

## Mục Lục
- [Tính Năng Chính](#tinh-nang-chinh)
- [Công Nghệ Sử Dụng](#cong-nghe-su-dung)
- [Hướng Dẫn Cài Đặt](#huong-dan-cai-dat)
- [Cơ Chế Hoạt Động](#co-che-hoat-dong)
- [Đóng Góp Phát Triển](#dong-gop-phat-trien)
- [Bản Quyền](#ban-quyen)

## Tính Năng Chính
- **Giám sát Phần cứng Thời gian thực**: Theo dõi CPU, GPU, RAM, Ổ cứng và Mạng với độ chính xác cao nhờ lõi \LibreHardwareMonitorLib\.
- **Chấm điểm Sức khỏe Thông minh**: Cung cấp điểm số hệ thống từ 0-100 được tính toán qua thuật toán EWMA (Trung bình động có trọng số mũ). Cơ chế này giúp loại bỏ các biến động nhiệt độ ảo để phản ánh đúng thực trạng máy tính.
- **Chống chai SSD (Zero-Disk-Wear)**: Lịch sử thông số (để vẽ biểu đồ) được lưu hoàn toàn trên RAM bằng cấu trúc \RingBuffer\ an toàn luồng. Việc này chặn hoàn toàn các chu kỳ ghi dữ liệu liên tục ra ổ cứng, giúp kéo dài tuổi thọ SSD.
- **Dọn RAM Chuyên sâu (NT Kernel)**: Sử dụng trực tiếp mã lệnh lõi của Windows NT (\NtSetSystemInformation\) để giải phóng triệt để vùng nhớ Standby List và Modified Page List. Thao tác này trả lại RAM cho hệ thống một cách chuẩn xác như phần mềm RAMMap của Microsoft, mà không làm treo giao diện.
- **Dọn rác Ổ đĩa An toàn**: Xóa rác hệ thống, bộ nhớ đệm trình duyệt và file tạm với cơ chế xử lý lỗi an toàn, tự động bỏ qua các file đang bị hệ thống khóa.
- **Giao diện Siêu mượt**: Biểu đồ phần cứng được vẽ trực tiếp vào bộ nhớ bằng thư viện đồ họa \SkiaSharp\ (đạt 60 FPS, vượt trội hoàn toàn so với WPF Polyline thông thường) kết hợp với hiệu ứng nền xuyên thấu Mica/Acrylic gốc của Windows 11 qua DWM API.
- **Chế độ Ngủ đông & Widget**: Ứng dụng có thể thu nhỏ thành Widget hoặc nằm ẩn trong khay hệ thống (System Tray). Ở chế độ "Cryo Mode", mức tiêu thụ CPU giảm xuống dưới 1% nhưng hệ thống giám sát vẫn chạy ngầm mượt mà.

## Công Nghệ Sử Dụng
- **Nền tảng**: .NET 10, Windows Presentation Foundation (WPF)
- **Kiến trúc**: MVVM (Model-View-ViewModel)
- **Đồ họa**: SkiaSharp cho hiệu ứng vẽ 2D tốc độ cao, không cấp phát rác (zero-allocation)
- **Giao tiếp Hệ thống**: LibreHardwareMonitorLib, gọi P/Invoke trực tiếp vào Windows NT Kernel và Shell32
- **Giao diện**: Custom XAML kết hợp Windows 11 Desktop Window Manager (Mica/Acrylic)

## Hướng Dẫn Cài Đặt
### Cách 1: Sử dụng File Cài đặt
1. Truy cập mục [Releases](https://github.com/ttan121/PC_Health_Dashboard/releases) trên GitHub.
2. Tải về file \PCHealthDashboard_Setup.exe\.
3. Chạy file cài đặt và làm theo hướng dẫn trên màn hình.
4. Chạy phần mềm với quyền **Administrator** (Bắt buộc để phần mềm có thể lấy thông số cảm biến và gọi API dọn RAM sâu).

### Cách 2: Bản Portable (Chạy ngay không cần cài đặt)
1. Tải file \PCHealthDashboard_Portable.zip\ từ trang Releases.
2. Giải nén vào một thư mục bất kỳ.
3. Chạy file \PCHealthDashboard.exe\ với quyền **Administrator**.

### Tự Build từ Mã nguồn
1. Clone repo về máy:
   \\\ash
   git clone https://github.com/ttan121/PC_Health_Dashboard.git
   \\\
2. Mở thư mục và chạy lệnh xuất file:
   \\\ash
   cd PC_Health_Dashboard
   dotnet publish -c Release -r win-x64 -o Publish
   \\\
3. Mở file \setup.iss\ bằng Inno Setup để tạo file cài đặt.

## Cơ Chế Hoạt Động
Khác với các phần mềm "tối ưu RAM" rẻ tiền thường dùng lệnh \EmptyWorkingSet\ (ép các phần mềm đang chạy đẩy dữ liệu ra ổ cứng, gây giật lag máy), PC Health Dashboard tương tác thẳng với Kernel để dọn dẹp **Standby List**. Điều này có nghĩa là nó chỉ dọn những phần bộ nhớ đệm (cache) mà hệ điều hành không còn dùng nữa, giữ cho các ứng dụng bạn đang mở vẫn chạy cực kỳ mượt mà.

## Đóng Góp Phát Triển
Dự án luôn hoan nghênh mọi đóng góp! Nếu bạn có ý tưởng cải tiến, sửa lỗi hay tính năng mới, vui lòng mở một Issue hoặc tạo Pull Request.

## Bản Quyền
Dự án được phân phối dưới giấy phép MIT License. Xem file \LICENSE\ để biết thêm chi tiết.