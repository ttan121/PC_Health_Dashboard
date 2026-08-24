# Original User Request

## 2026-08-24T05:31:37Z

This is a single self-contained fix; keep it small and focused. Sua loi giao dien (UI) va logic Compact Mode cho du an PC Health Dashboard. Bao gom viec format hien thi dung luong (MB/GB), sua trang thai nut bam, xoa nut dieu khien thua tren title bar, va tach biet hoan toan logic giua Compact Mode va Widget de app chay ngam dung chuan.

Working directory: D:\PC_Health_Dashboard
Integrity mode: development

## Requirements

### R1. UI Formatting & State Fixes
- **RamOptimizerWindow & JunkCleanerWindow**: Hien thi tong dung luong da chon. Neu >= 1024 MB thi hien thi them format GB, vi du: "1024 MB (1.00 GB)". Nguoc lai chi hien thi MB.
- **JunkCleanerWindow**: Nut "Don Dep Ngay" dang bi loi hien thi (mau trang) khi chua quet. Phai vo hieu hoa (disabled/gray out) nut nay khi chua quet hoac Total = 0 MB.
- **MainWindow (Dashboard)**: Xoa bo 3 nut dieu khien cua so (minimize, maximize, close) bi du thua (do custom title bar va default title bar dang bi chong len nhau).

### R2. Compact Mode & Widget Independence
- **Dong Compact Mode**: Khi nguoi dung dong Compact Mode, khong duoc tu dong bat Widget len. Ung dung phai thu nho hoan toan xuong khay he thong (System Tray) va chay ngam o trang thai ngu dong.
- **Doc lap hoan toan**: Compact Mode va Widget phai hoat dong doc lap, khong phu thuoc vong doi vao nhau. Khi mo lai Widget tu System Tray, cac chi so phan cung van phai duoc cap nhat theo thoi gian thuc ma khong bi "dong bang" (freeze).

## Acceptance Criteria

### UI Fixes Verification
- [ ] Chon >= 1024 MB tren RamOptimizer hoac JunkCleaner se hien thi chuoi "(X GB)".
- [ ] Nut "Don Dep Ngay" chuyen sang xam (disabled) va khong the click khi chua quet hoac chua chon muc nao.
- [ ] Khong con 2 hang nut X, _, [] chong cheo tren giao dien MainWindow.

### Logic Fixes Verification
- [ ] Bam nut X tren Compact Mode -> Giao dien bien mat hoan toan. Widget KHONG hien ra.
- [ ] Mo rieng Widget -> Cac con so nhiet do/RAM van nhay (cap nhat) binh thuong.

## 2026-08-24T06:42:53Z

This is a single self-contained fix; keep it small and focused. Sua loi treo giao dien (UI freeze) khi thuc hien chuc nang don RAM tren Dashboard. Khi user bam don RAM, chuc nang don hoat dong thanh cong nhung thanh tien trinh (progress bar) va chi so RAM tren giao dien bi dung hinh, khong tiep tuc cap nhat so lieu moi nhat.

Working directory: D:\PC_Health_Dashboard
Integrity mode: development

## Requirements

### R1. Fix RAM UI Freeze Bug
- Khoi phuc viec cap nhat giao dien sau khi chay chuc nang don RAM (`NativeMemoryService`). Nguyen nhan co the do luong cap nhat UI (Dispatcher) hoac vong lap lay mau (HardwarePoller) bi ngat ket noi (disconnected/cancelled) hoac bi deadlock khi thao tac don RAM duoc goi.
- Phai dam bao sau khi don RAM thanh cong, chi so RAM tren Dashboard phai lap tuc giam xuong giong voi Task Manager va tiep tuc cap nhat lien tuc theo thoi gian thuc.

## Acceptance Criteria

### UI Freeze Verification
- [ ] Bam nut Don RAM tren Dashboard, sau do kiem tra chi so RAM. Chi so nay phai tu dong giam xuong dung voi thuc te (giong voi Task Manager).
- [ ] Thanh tien trinh mau xanh va con so hien thi phai tiep tuc nhay the hien muc do su dung RAM moi ma khong bi treo vinh vien sau khi goi ham don dep.
