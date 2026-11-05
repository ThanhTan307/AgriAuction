# AgriAuction

**AgriAuction** là hệ thống đấu giá nông sản mô phỏng, kết nối giao diện Web dành cho quản trị viên/doanh nghiệp với ứng dụng Flutter Android dành cho nông dân. Hệ thống sử dụng ASP.NET Core, PostgreSQL và SignalR.

> **Phạm vi mô phỏng:** Đấu giá, tiền cọc, thanh toán và giao nhận được thực hiện trong hệ thống để minh họa quy trình nghiệp vụ. Không phát sinh tiền thật, không kết nối ngân hàng, cổng thanh toán, email hoặc dịch vụ push.

## 1. Tổng quan dự án

| Thành phần            | Công nghệ / thư mục                         |
| --------------------- | ------------------------------------------- |
| Backend và API        | .NET 10, ASP.NET Core, EF Core — `src/`     |
| Dữ liệu và realtime   | PostgreSQL 18, SignalR                      |
| Giao diện Web         | HTML, CSS, JavaScript — `pages/`, `assets/` |
| Ứng dụng Farmer       | Flutter Android — `farmer_mobile/`          |
| Cài đặt và vận hành   | `scripts/`; cấu hình Flutter tại `farmer_mobile/config/` |
| Cấu hình runtime cục bộ | `.local/product/` (được tạo bởi script khởi tạo, không commit) |
| Kiểm thử và hướng dẫn | `tests/`, `docs/`                           |

**Địa chỉ mặc định khi chạy tại máy phát triển:**

| Dịch vụ                 | Địa chỉ                                  |
| ----------------------- | ---------------------------------------- |
| Web và API              | `http://127.0.0.1:5084`                  |
| Trang đăng nhập         | `http://127.0.0.1:5084/pages/login.html` |
| PostgreSQL              | `127.0.0.1:5432` (mặc định)              |
| API từ Android Emulator | `http://10.0.2.2:5084`                   |

## 2. Cài đặt và khởi động

### Yêu cầu

- Windows, Git for Windows, PowerShell và .NET SDK đúng phiên bản trong `global.json`.
- PostgreSQL 18 hoạt động ở `127.0.0.1:5432` hoặc cổng khác nếu máy đã cấu hình sẵn.
- Để chạy ứng dụng Farmer: Flutter SDK, Android SDK và Android Emulator.

### Khởi tạo lần đầu (chỉ khi chưa thiết lập)

Clone repository và mở **CMD** tại thư mục vừa clone. Lệnh `cd` dưới đây dùng đường dẫn tương đối; thay URL nếu clone từ fork riêng:

```bat
git clone https://github.com/ThanhTan307/AgriAuction.git
cd AgriAuction
set "AGRIAUCTION_POSTGRES_ADMIN_PASSWORD=THAY_BANG_MAT_KHAU_POSTGRESQL"
%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe -NoProfile -ExecutionPolicy Bypass -File "scripts\Initialize-AgriAuction.ps1"
```

Thay `THAY_BANG_MAT_KHAU_POSTGRESQL` bằng mật khẩu PostgreSQL có quyền tạo database/role. Script mặc định dùng cổng `5432`; nếu máy dùng cổng khác, thêm `-PostgresPort CONG`. Script sẽ khởi tạo database `agriauction_system`, role, schema `agriauction_app`, chạy migrations, tạo tài khoản/danh mục mẫu và lưu cấu hình cục bộ tại `.local\product`.

Script dùng user quản trị `postgres` và cổng `5432` mặc định. PostgreSQL 18 Command Line Tools (`psql.exe`, `pg_isready.exe`) cần có trong `PATH` hoặc ở thư mục cài đặt PostgreSQL dưới `C:\Program Files\PostgreSQL`. Có thể kiểm tra bằng:

```powershell
pg_isready.exe -h 127.0.0.1 -p 5432
```

Nếu dùng user quản trị hoặc cổng khác, truyền thêm `-PostgresAdminUser TEN_USER -PostgresPort CONG`.

> **Bảo mật:** Không đưa `.local\product` hoặc các thông tin đăng nhập vào Git, ảnh chụp màn hình hay tài liệu công khai.

### Khởi động / dừng hệ thống

Chạy từ **CMD** tại thư mục dự án:

**Khởi động:**

```bat
%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe -NoProfile -ExecutionPolicy Bypass -File "scripts\Start-AgriAuction.ps1"
```

**Dừng:**

```bat
%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe -NoProfile -ExecutionPolicy Bypass -File "scripts\Stop-AgriAuction.ps1"
```

Script khởi động sử dụng môi trường `Development`, kiểm tra PostgreSQL, chạy Backend ở cổng `5084`, mở trang đăng nhập và thử mở APK trên Android Emulator đang online. Nếu Backend đã chạy, script giữ tiến trình hiện tại. Script dừng chỉ dừng Backend do chính script khởi động, **không dừng PostgreSQL**.

Nếu dùng **PowerShell** thay cho CMD, có thể chạy trực tiếp `& ".\scripts\Start-AgriAuction.ps1"` hoặc `& ".\scripts\Stop-AgriAuction.ps1"` sau khi chuyển đến thư mục dự án.

### Tài khoản mẫu

Email và mật khẩu được tạo trong file cục bộ:

```text
.local\product\seed.json
```

Dùng các tài khoản này để thao tác theo vai trò Administrator, Farmer và Enterprise. Không chép mật khẩu vào README hoặc mã nguồn.

## 3. Quy trình sử dụng và trình diễn

1. **Farmer (Android):** Tạo lô nông sản, tải ảnh và gửi kiểm duyệt.
2. **Administrator (Web):** Xem hồ sơ, kiểm duyệt và phê duyệt lô.
3. **Administrator (Web):** Tạo, cấu hình và công bố phiên đấu giá mô phỏng.
4. **Enterprise (Web):** Đăng ký phiên; Administrator xác nhận tiền cọc mô phỏng để doanh nghiệp đủ điều kiện tham gia.
5. **Enterprise (Web):** Trả giá, theo dõi realtime, gia hạn và kết quả phiên.
6. **Sau đấu giá:** Doanh nghiệp thắng đề nghị thanh toán mô phỏng, Administrator xác nhận; Farmer ghi đã giao, Enterprise xác nhận đã nhận, Farmer xác nhận hoàn tất.
7. **Đối soát:** Administrator xử lý hoàn cọc mô phỏng theo điều kiện, theo dõi thông báo và báo cáo. Khi có vấn đề, sử dụng luồng khiếu nại/tranh chấp theo quyền.

**Lưu ý khi thao tác nhiều tài khoản:** Nên đăng nhập mỗi vai trò bằng một hồ sơ trình duyệt riêng để tránh dùng chung cookie phiên.

## 4. Chạy ứng dụng Farmer Android

Từ thư mục gốc dự án, chạy nhanh cả Backend, Emulator và Farmer Mobile:

```bat
%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe -NoProfile -ExecutionPolicy Bypass -File "scripts\Start-AgriAuction.ps1" -FarmerMobile
```

Lệnh này khởi động Backend rồi gọi launcher độc lập. Launcher tự chọn AVD `AgriCompletion` nếu có; nếu không sẽ dùng AVD sẵn có như `Medium_Phone_API_36.1`, khởi động `emulator.exe`, bỏ qua snapshot lỗi nhưng không xóa dữ liệu, chờ Android boot hoàn tất, cài APK và mở Farmer Mobile. Không cần mở Android Studio hoặc Flutter debugger. APK dùng API mới tại `http://10.0.2.2:5084`, không gọi API của project cũ.

Nếu Backend đã chạy, có thể chỉ chạy:

```bat
%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe -NoProfile -ExecutionPolicy Bypass -File "scripts\Start-FarmerMobile.ps1"
```

Kiểm tra thiết bị thủ công:

```bat
%LOCALAPPDATA%\Android\Sdk\platform-tools\adb.exe devices
```

Build lại APK khi source Flutter thay đổi:

```bat
%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe -NoProfile -ExecutionPolicy Bypass -File "scripts\Build-FarmerAndroid.ps1" -Profile Emulator
```

APK được lưu tại `.local\product\apk\agriauction-farmer-emulator-debug.apk`. Cài/mở thủ công:

```bat
%LOCALAPPDATA%\Android\Sdk\platform-tools\adb.exe install -r ".local\product\apk\agriauction-farmer-emulator-debug.apk"
%LOCALAPPDATA%\Android\Sdk\platform-tools\adb.exe shell monkey -p com.agriauction.farmer_mobile 1
```

Nếu ADB báo `offline`, đóng emulator bị treo, chạy `adb kill-server`, `adb start-server`, rồi chạy lại script; không dùng `Wipe Data` hoặc xóa AVD. Nếu app không gọi được Backend, kiểm tra Backend đang nghe cổng `5084` và emulator dùng đúng địa chỉ `10.0.2.2:5084`.

## 5. Build và kiểm thử

Từ thư mục gốc dự án:

```bat
:: Build toàn bộ solution
dotnet build AgriAuction.slnx --no-restore

:: Unit test
dotnet test tests\AgriAuction.UnitTests\AgriAuction.UnitTests.csproj --no-restore

:: Integration test (chỉ khi đã cấu hình đúng PostgreSQL cho kiểm thử)
dotnet test tests\AgriAuction.IntegrationTests\AgriAuction.IntegrationTests.csproj --no-restore
```

**Chú ý:** Integration test có thể tạo hoặc thay đổi dữ liệu; cần xác nhận đúng database/schema kiểm thử trước khi chạy. Không hướng bộ test ghi vào database đang dùng để trình diễn.

## 6. Xử lý sự cố và tài liệu

| Sự cố                       | Cách kiểm tra / xử lý                                                                            |
| --------------------------- | ------------------------------------------------------------------------------------------------ |
| PostgreSQL không kết nối    | Kiểm tra dịch vụ PostgreSQL 18 và cổng `5432` (hoặc cổng đã truyền bằng `-PostgresPort`).          |
| Backend báo chưa khởi tạo   | Chạy script `Initialize-AgriAuction.ps1` theo mục 2.                                             |
| Đăng nhập báo hệ thống lỗi  | Chạy lại script khởi tạo để tạo lại secret kết nối database, sau đó chạy script khởi động.           |
| Cổng `5084` đã được sử dụng | Kiểm tra `netstat -ano \| findstr :5084`; dùng script dừng nếu đó là Backend do dự án khởi động. |
| Android không mở            | Chạy `adb devices`, bảo đảm emulator ở trạng thái `device`; Web vẫn có thể sử dụng riêng.        |
| Backend có lỗi              | Xem `.local\product\backend.stdout.log` và `.local\product\backend.stderr.log`.                  |

**Tài liệu chi tiết:** [Cài đặt](docs/INSTALLATION.md) · [Vận hành](docs/OPERATIONS.md) · [Danh sách thành phần đã loại bỏ](docs/REMOVALS.md)
