# Báo cáo kiểm thử nghiệm thu AgriAuction

**Thời điểm:** 2026-11-05 (Asia/Saigon)  
**Phiên bản source:** Không xác định được: máy hiện không có `git` trong PATH.  
**Môi trường:** Windows, .NET SDK 10.0.100, PostgreSQL 18 tại `127.0.0.1:5432`; Android SDK/ADB đã được xác minh.  
**Kết luận hiện tại:** Chưa đủ điều kiện bàn giao. Chờ người dùng nhập mật khẩu PostgreSQL tại terminal để tạo database độc lập; không dùng database demo.

## Môi trường và phạm vi đã xác minh

- Cấu hình sản phẩm cục bộ trỏ tới `agriauction_system` trên `127.0.0.1:5432`. Kết nối PostgreSQL chỉ được dùng cho truy vấn đọc: database hiện tại là `agriauction_system`, database `agriauction_acceptance_test` chưa tồn tại, và role ứng dụng không có quyền tạo database.
- PostgreSQL 18 chấp nhận kết nối. Truy vấn chỉ đọc xác nhận `agriauction_acceptance_test` chưa tồn tại; role sản phẩm không có quyền tạo DB. Không có `pgpass.conf` hoặc biến môi trường mật khẩu quản trị. Không tạo DB cho đến khi mật khẩu được nhập trực tiếp ở terminal.
- Đã chuẩn bị initializer cục bộ bị `.gitignore` bao phủ tại `.local/acceptance/Initialize-Acceptance.ps1`. Script chỉ tạo DB/role mang tên nghiệm thu sau khi kiểm tra chúng chưa tồn tại, tạo thông tin tài khoản ngẫu nhiên trong `.local/acceptance`, rồi chạy migrations và bootstrap-product. Cú pháp PowerShell đã được phân tích, chưa chạy vì cần mật khẩu quản trị.
- Cổng `5085` rảnh tại lần kiểm tra trước. HTTP trên `5084` không phản hồi; không khởi chạy Backend theo cấu hình sản phẩm vì nó trỏ tới database chính.
- ADB chạy được khi gọi ngoài sandbox; danh sách thiết bị online trống. AVD có sẵn: `Medium_Phone_API_36.1`. APK Farmer có sẵn nhưng cấu hình API cũ là `10.0.2.2:5084`, nên chưa cài/chạy. Flutter/Git chưa xác minh thêm: Git không có trong PATH, các vị trí cài mặc định và đường dẫn Git cục bộ của dự án đã kiểm tra đều không có `git.exe`.
- Không chụp ảnh giao diện hoặc ghi log phiên nghiệp vụ vì chưa có môi trường kiểm thử độc lập để thao tác an toàn.

## Kết quả

| Hạng mục | Trạng thái | Kết quả thực tế |
|---|---|---|
| Build solution .NET (Backend và các project .NET) | PASS | `dotnet build AgriAuction.slnx --no-restore`: thành công, 0 cảnh báo, 0 lỗi (1,99 giây). Web tĩnh chưa được kiểm tra runtime. |
| Unit tests | PASS | 127 passed, 0 failed, 0 skipped (64 ms). |
| Tạo database kiểm thử và migrations | BLOCKED | DB đã được xác minh chưa tồn tại; chờ nhập mật khẩu `postgres` tại terminal để chạy initializer cô lập. |
| TC01 Farmer tạo lô trên Flutter | BLOCKED | Chưa cài/chạy APK trên emulator. |
| TC02 Admin duyệt lô và kiểm tra API trái quyền | BLOCKED | Chưa có Backend kiểm thử và dữ liệu riêng. |
| TC03 Tạo, cấu hình và công bố phiên | BLOCKED | Chưa thực hiện trên ứng dụng. |
| TC04 Hai Enterprise đăng ký và xác nhận cọc | BLOCKED | Chưa có hai phiên trình duyệt và môi trường riêng. |
| TC05 Đấu giá realtime, giá sai, đồng thời, reconnect và gia hạn | BLOCKED | Chưa thực hiện; SignalR chưa được kiểm tra runtime. |
| TC06 Chốt kết quả và đối chiếu bốn vai trò/database | BLOCKED | Chưa có phiên đấu giá kiểm thử. |
| TC07 Thanh toán mô phỏng, giao nhận và hoàn tất | BLOCKED | Chưa có giao dịch kiểm thử. |
| TC08 Xử lý và đối soát cọc | BLOCKED | Chưa có hồ sơ cọc kiểm thử. |
| Khiếu nại/tranh chấp | BLOCKED | Chưa gửi khiếu nại hoặc xác minh quy trình trên ứng dụng. |
| NF01 Hiệu năng | NOT TESTED | Không thu thập số đo runtime. |
| NF02 Phân quyền/bảo mật | NOT TESTED | Unit tests không thay thế kiểm tra quyền qua ứng dụng/API runtime. |
| NF03 Ổn định/reconnect | NOT TESTED | Không có phiên runtime độc lập. |
| NF04 Viewport và Flutter UI | NOT TESTED | Không mở Web hoặc emulator để kiểm tra giao diện. |
| NF05 Toàn vẹn dữ liệu nghiệp vụ | NOT TESTED | Chỉ xác minh trạng thái database bằng truy vấn đọc; không có giao dịch nghiệm thu để đối chiếu. |
| NF06 Vận hành/bàn giao | BLOCKED | Build .NET đã qua; migrations/seed, Flutter APK trên cổng 5085, script trên môi trường cô lập, và backup/restore chưa kiểm tra. |

## Lỗi và bằng chứng

- Chưa sửa lỗi source. Không phát hiện lỗi nghiệp vụ vì luồng ứng dụng chưa chạy được trong môi trường độc lập.
- Bằng chứng thực tế: build solution .NET thành công; unit tests 127/127; truy vấn PostgreSQL chỉ đọc xác nhận DB nghiệm thu vắng mặt và role sản phẩm không có quyền tạo database; probe HTTP `5084` không phản hồi; ADB ngoài sandbox trả danh sách không có thiết bị online và tìm thấy AVD `Medium_Phone_API_36.1`.
- Không lưu ảnh chụp màn hình hay log có thông tin nhạy cảm.

## Điều kiện để tiếp tục

Cần chạy từ thư mục gốc dự án: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".local\acceptance\Initialize-Acceptance.ps1"`. Lệnh sẽ yêu cầu mật khẩu `postgres` bằng dấu nhắc nhập ẩn, không ghi vào chat, source hoặc log. Sau khi migrations và seed thành công, có thể tiếp tục Backend `5085`, build APK dùng `10.0.2.2:5085`, emulator và quy trình nghiệm thu. Cho tới lúc đó, end-to-end và bàn giao vẫn **BLOCKED / chưa đủ điều kiện**.
