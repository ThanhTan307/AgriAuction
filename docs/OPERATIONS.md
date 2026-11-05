# Vận hành và sử dụng

## Khởi động

Từ CMD, chạy `%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\Start-AgriAuction.ps1`. Script kiểm tra PostgreSQL, giữ backend đang chạy nếu có, mở web và thử mở APK trên emulator đang online. Script không migration hoặc seed lại mỗi lần mở.

Web dùng `http://127.0.0.1:5084`. Android emulator dùng `http://10.0.2.2:5084`; điện thoại USB dùng `http://127.0.0.1:5084` cùng `adb reverse`.

## Luồng trình diễn

Đăng nhập Administrator để quản lý danh mục và duyệt tài khoản/lô. Farmer tạo lô, tải ảnh và gửi duyệt. Administrator kiểm tra và duyệt lô, sau đó chuẩn bị phiên. Enterprise đăng ký, được xác nhận cọc mô phỏng, tham gia phiên và theo dõi kết quả. Các bước thanh toán mô phỏng, giao nhận, hoàn tất, khiếu nại và báo cáo chỉ dùng dữ liệu cục bộ.

## Dừng

Từ CMD, chạy `%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\Stop-AgriAuction.ps1`. Chỉ backend trên cổng 5084 được dừng; PostgreSQL dùng chung không bị ảnh hưởng.
