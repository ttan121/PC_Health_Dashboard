# PC Health Dashboard

*Read in English: [README](README.md).*

> **Nắm nhanh tình trạng PC của bạn trên một màn hình.**

PC Health Dashboard là ứng dụng Windows tập trung các chỉ số phần cứng và hệ thống thường dùng vào cùng một nơi. Ứng dụng giúp bạn theo dõi tải CPU/GPU, nhiệt độ cảm biến, mức dùng RAM, dung lượng ổ đĩa và lưu lượng mạng, kèm một số công cụ bảo trì thường ngày.

## Why — Tại sao

Khi máy nóng, chậm hoặc hoạt động không ổn định, thông tin cần xem thường nằm rải rác trong Task Manager, phần mềm theo dõi phần cứng và cài đặt Windows. Dashboard gom các chỉ số và cảnh báo phổ biến để bạn nhanh chóng nhận ra thay đổi và biết nên kiểm tra tiếp ở đâu.

Điểm sức khỏe chỉ là phần tóm tắt một số chỉ số, không phải kết quả chẩn đoán được chứng nhận cho máy tính hay linh kiện.

## Who — Dành cho ai

Ứng dụng dành cho người dùng Windows muốn xem tổng quan hệ thống một cách dễ hiểu. Người chơi game, lập trình viên, người sáng tạo nội dung và người yêu thích phần cứng có thể quan sát tải và nhiệt độ khi chơi, làm việc hoặc xử lý sự cố. Ứng dụng phù hợp cho việc kiểm tra hằng ngày, không thay thế công cụ chẩn đoán của nhà sản xuất hoặc trình quản lý tác vụ đầy đủ.

## What — Chức năng

- **CPU và đồ họa:** tải hiện tại cùng nhiệt độ mà cảm biến khả dụng cung cấp.
- **Bộ nhớ:** lượng RAM đang dùng và tổng RAM, kèm cửa sổ tùy chọn bảo trì RAM.
- **Ổ đĩa:** dung lượng ổ hệ thống. Giá trị `SSD Health` đang hiển thị chỉ là giá trị giữ chỗ; phiên bản mã nguồn này **chưa đọc dữ liệu hao mòn SMART/NVMe** và không thể báo tuổi thọ SSD.
- **Mạng:** tốc độ tải xuống/tải lên và biểu đồ lưu lượng ngắn hạn. Các trường ping và mất gói đang hiển thị chưa được nối với dữ liệu cập nhật trực tiếp.
- **Tình trạng hệ thống:** điểm sức khỏe và một số cảnh báo về nhiệt độ, tải, bộ nhớ và dung lượng ổ thấp. Điểm mạng hiện cố định, còn đầu vào SSD Health dùng giá trị mặc định nên không đánh giá được chính xác chất lượng mạng hay độ hao mòn ổ.
- **Bảo trì và hiển thị:** công cụ bảo trì RAM, dọn file tạm, widget desktop và chế độ hiển thị thu gọn.

## How — Cách hoạt động

Ứng dụng dùng WPF trên nền .NET. Ứng dụng đọc cảm biến CPU/GPU qua LibreHardwareMonitor, lấy thông tin bộ nhớ và dung lượng ổ qua giao diện Windows, đồng thời lấy mẫu lưu lượng mạng. Các điểm dữ liệu gần đây của biểu đồ được giữ trong RAM thay vì ghi liên tục xuống ổ đĩa.

Khả năng đọc cảm biến tùy thuộc PC, firmware, phiên bản Windows và driver. Một số trường GPU hoặc nhiệt độ có thể không có hoặc dùng số liệu thay thế. `app.manifest` yêu cầu quyền Administrator nên Windows sẽ hiện hộp thoại UAC khi mở ứng dụng.

## Điều kiện và build

- Windows x64.
- Khi chạy cần chấp nhận hộp thoại Administrator/UAC.
- Build từ mã nguồn cần **.NET 10 SDK** và Internet cho lần khôi phục gói NuGet đầu tiên. Bản framework-dependent cần .NET 10 Desktop Runtime trên máy chạy; lệnh self-contained bên dưới đóng gói kèm runtime.

```powershell
dotnet restore PCHealthDashboard.slnx
dotnet publish PCHealthDashboard.csproj -c Release -r win-x64 --self-contained true -o Publish
```

Nén toàn bộ thư mục `Publish` để tạo bản portable. Muốn tạo bộ cài, cài **Inno Setup 6** rồi mở `setup.iss`; kết quả được ghi vào `Installer`.

## License

MIT. Xem [LICENSE](LICENSE).
