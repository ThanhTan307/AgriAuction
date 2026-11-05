# AgriAuction Farmer Android

Flutter Farmer dùng chung API ASP.NET Core. Profile Completion chỉ mô phỏng. Môi trường và bằng chứng tại [docs/37](../docs/37-completion-implementation.md).

## Chạy

Khởi động PostgreSQL test 55432 hiện có, rồi từ thư mục repo:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Initialize-WebCompletion.ps1 -Start
```

Lệnh dùng riêng DB/role `agri_web_completion_test`, Testing/5083, không dừng host demo. Tài khoản synthetic nằm tại `.local/web-completion/credentials.json`; Admin cần duyệt tài khoản để thao tác nghiệp vụ. Không đưa credential vào source hoặc APK.

Từ `farmer_mobile`, với Flutter/Android SDK và Git có sẵn trong PATH:

```powershell
flutter pub get --offline
flutter analyze --no-pub
flutter test --no-pub
flutter run --dart-define=API_BASE_URL=http://10.0.2.2:5083
flutter build apk --debug --no-pub --dart-define=API_BASE_URL=http://10.0.2.2:5083
```

Máy hiện tại dùng Git portable đã kiểm SHA256 trong `.local/tools`, không cài hệ thống. Nếu Flutter báo thiếu Git, từ repo chạy `powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Prepare-FlutterGit.ps1`; trong cửa sổ PowerShell chạy Flutter, thêm thư mục `.local/tools/mingit-2.56.0.4/cmd` của repo vào `$env:PATH` trước khi chuyển vào `farmer_mobile`.

APK: `build/app/outputs/flutter-apk/app-debug.apk`. Cài đúng APK debug trên thiết bị đã kết nối bằng:

```powershell
flutter install --debug -d emulator-5556 --use-application-binary=build/app/outputs/flutter-apk/app-debug.apk
```

Emulator dùng `10.0.2.2`. Điện thoại thật không dùng địa chỉ này: qua USB chạy `adb reverse tcp:5083 tcp:5083` rồi build/run với `ANDROID_TARGET=usb` và `API_BASE_URL=http://127.0.0.1:5083`; qua Wi-Fi dùng `ANDROID_TARGET=lan` và địa chỉ LAN của máy chạy Backend:

```powershell
flutter run --dart-define=ANDROID_TARGET=usb --dart-define=API_BASE_URL=http://127.0.0.1:5083
flutter run --dart-define=ANDROID_TARGET=lan --dart-define=API_BASE_URL=http://192.168.1.10:5083
```

HTTP được cho phép riêng debug manifest; chưa có bản release vận hành thật. Không thay địa chỉ DB bằng địa chỉ API.

## Luồng Farmer

Đăng ký Farmer → Admin duyệt trên Web → đăng nhập → tạo/sửa lô và ảnh JPEG/PNG → gửi kiểm định. Farmer theo dõi phiên và lịch sử giá (không đặt giá), kết quả, giao dịch/giao nhận/hoàn tất theo action server cho phép, inbox, khiếu nại có ảnh/giải trình và báo cáo cá nhân. Có sửa tên hiển thị, đổi mật khẩu và đăng xuất; đổi mật khẩu thu hồi phiên.

Phòng/inbox dùng invalidation SignalR rồi đọc lại API có kiểm quyền, reconnect và polling dự phòng. Yêu cầu giao nhận/khiếu nại giữ Idempotency-Key khi chưa xác nhận được kết quả mạng. Tiền gửi bằng chuỗi decimal. Legacy giữ policy cũ. Uy tín/cấp độ: **CHƯA XÁC ĐỊNH NGHIỆP VỤ**.

Kiểm chứng 02/11/2026: 13 Flutter tests đạt, analyzer không lỗi; APK debug mới cài được trên AVD `emulator-5556`. Các luồng native trước đó và giới hạn theo từng bản APK xem tại [docs/37](../docs/37-completion-implementation.md). Upload ảnh chọn filename/MIME theo signature thực vì Android picker có thể nén JPEG nhưng giữ đuôi PNG; server vẫn kiểm codec. Hồ sơ gửi expectedVersion, dialog tự quản lý controller; đổi mật khẩu đã kiểm thu hồi phiên thực.
