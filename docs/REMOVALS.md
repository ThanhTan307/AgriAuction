# Thành phần không đưa vào bản bàn giao

| Thành phần | Lý do |
|---|---|
| `.local`, `.nuget`, `.dotnet` và Android build cache | Chứa secret, credential, cache máy cá nhân hoặc artifact sinh ra |
| `node_modules`, `bin`, `obj`, `.dart_tool`, `build` | Có thể tái tạo bằng package manager/build tool |
| `TestResults` | Log, ảnh, TRX và bằng chứng kiểm thử trung gian |
| `references` | Tài liệu nguồn không thuộc runtime/bàn giao sản phẩm |
| Launcher và runner dành cho môi trường cũ | Phụ thuộc database/schema/port khác hoặc chỉ phục vụ kiểm định |
| Báo cáo lịch sử và cấu hình môi trường cũ | Chứa thuật ngữ nội bộ, đường dẫn và target không dùng trong bản độc lập |

Migration, mã nghiệp vụ, trang web, asset, test lõi và cấu hình build vẫn được giữ.
