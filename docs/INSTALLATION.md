# Cài đặt AgriAuction

## Yêu cầu

- Windows 11, .NET SDK theo `global.json`.
- PostgreSQL 18 đang chạy tại `127.0.0.1:5432` hoặc một cổng đã cấu hình sẵn.
- Flutter và Android SDK chỉ cần khi build/chạy ứng dụng Android.

## Khởi tạo lần đầu

Không dùng database cũ. Đặt mật khẩu của tài khoản PostgreSQL có quyền tạo role/database vào `AGRIAUCTION_POSTGRES_ADMIN_PASSWORD`, sau đó chạy:

```powershell
%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\Initialize-AgriAuction.ps1
```

Lệnh mặc định dùng cổng `5432`; nếu PostgreSQL dùng cổng khác, thêm `-PostgresPort CONG`. Lệnh tạo database `agriauction_system`, role `agriauction_system`, schema `agriauction_app`, chạy migration và tạo bốn tài khoản cùng bốn danh mục mẫu. Lệnh có tính lặp lại và không reset dữ liệu.

User quản trị mặc định là `postgres`; dùng `-PostgresAdminUser TEN_USER` nếu khác.

Script cũng tạo secret kết nối riêng tại `.local\product\database-secret.json`; không xóa hoặc đưa file này vào Git.

## Build

```powershell
dotnet build AgriAuction.slnx --no-restore
%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\Build-FarmerAndroid.ps1 -Profile Emulator
```

APK được đặt trong `.local\product\apk`, là bản debug dùng cho môi trường cục bộ.
