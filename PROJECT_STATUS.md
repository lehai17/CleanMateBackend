# CleanMate: trạng thái và phần còn thiếu

Rà soát mã nguồn ngày 16/10/2026. Tài liệu phản ánh các repository local;
không xác nhận trạng thái hosting, tài khoản dịch vụ hoặc bản app đã phát hành.

## Đã có

- Giới hạn đăng nhập/đăng ký/Google theo IP, trả thời gian chờ rõ ràng;
  mật khẩu mới được kiểm tra giới hạn byte. Xem [AUTH_RATE_LIMITS.md](AUTH_RATE_LIMITS.md).

- Web/app khách: đặt đơn với giá từ server, liên hệ, đổi/hủy lịch khi chưa được
  nhận, theo dõi ca, xác nhận hoàn thành và đánh giá.
- Web/app cleaner riêng: đăng ký/chờ duyệt, nhận ca, chống trùng lịch, bắt đầu/
  hoàn thành, xác nhận tiền mặt, lọc lịch/công việc và liên hệ khách.
- Web Admin/Staff riêng: dashboard, duyệt cleaner, cấu hình giá, tra cứu đơn,
  quản lý Staff và nhật ký thao tác.
- Thông báo trong ứng dụng, trạng thái đã đọc, phân trang và mở đúng khu vực.
- Đổi mật khẩu cho khách, cleaner, Admin/Staff, có giới hạn thử; đổi thành công
  vô hiệu hóa các JWT cũ và yêu cầu đăng nhập lại. Có audit và thông báo cá nhân.
- Đăng xuất tất cả thiết bị mà không đổi mật khẩu, có xác nhận trên giao diện;
  dùng được cả cho tài khoản Google và chỉ thu hồi các phiên CleanMate.
- Hỗ trợ theo đơn: khách và cleaner được giao ca gửi báo cáo; staff phản hồi,
  cập nhật trạng thái và gửi thông báo cho người báo. Có chống gửi trùng khi
  thử lại, kiểm tra quyền và chống ghi đè cập nhật giữa nhiều staff.
- Backend có migration SQL Server/PostgreSQL và các bộ kiểm thử tự động trên
  SQL tạm. Web được test desktop/mobile browser; app được export Android.

Tiền mặt được khách trả trực tiếp cho cleaner sau ca. Số tiền cleaner xác nhận
đã nhận không phải số dư hoặc doanh thu của nền tảng.

## Các phần cần làm tiếp, theo thứ tự đề xuất

1. **Tài khoản:** quên mật khẩu và xác minh email cần luồng token dùng một lần
   và dịch vụ gửi email. Login/signup/Google đã có giới hạn theo IP; đổi mật khẩu
   và thu hồi phiên cũ đã có, không thay thế luồng quên mật khẩu.
2. **Hồ sơ cleaner:** tải giấy tờ/ảnh xác minh, lưu tệp riêng tư và quy trình
   staff kiểm tra hồ sơ. Hiện duyệt dựa trên thông tin văn bản.
3. **Thông báo đẩy:** thông báo khi app không mở. Hiện inbox/polling hoạt động
   trong ứng dụng; chưa có tích hợp push và quản lý token thiết bị.
4. **Vận hành sự cố:** bổ sung bằng chứng/ảnh và hội thoại cho yêu cầu hỗ trợ;
   quy trình ca trễ, không đến và điều phối lại do staff thực hiện. Hiện hỗ trợ
   lưu phản hồi mới nhất; phản hồi không tự sửa đơn hoặc giao dịch tiền mặt.
5. **Dọn luồng cũ:** thống nhất khách sử dụng app/web khách và cleaner sử dụng
   hai dự án riêng; rà các màn hình cleaner cũ còn nằm trong ứng dụng khách.
6. **Phát hành:** kiểm thử UI trên điện thoại thật, tạo bản cài, xác nhận API
   HTTPS trên mạng thật, logging và thử phục hồi backup. Export Android hiện
   tại chỉ kiểm tra bundle, chưa phải APK/IPA hay kiểm thử thiết bị.

## Hỗ trợ đơn hàng: phạm vi bản hiện tại

`GET /api/support/bookings` gợi ý tối đa 100 đơn mới nhất của tài khoản; có thể
nhập thủ công mã đơn cũ. `GET /api/support?page=...` trả 20 yêu cầu mỗi trang,
chỉ của người đang đăng nhập. `POST /api/support` yêu cầu mã đơn thuộc người
đặt hoặc cleaner đã được giao, loại sự cố, mô tả 10–2000 ký tự và `requestId`.
Retry cùng mã/nội dung không tạo yêu cầu hoặc thông báo mới; đổi nội dung với
cùng mã trả 409. Mỗi bên chỉ xem được yêu cầu do chính mình gửi.

Admin/Staff dùng `/api/management/support` để tra cứu và cập nhật. Gửi kèm
`version` đang xem; dữ liệu bị sửa ở nơi khác sẽ trả 409. Đóng yêu cầu phải
có phản hồi ít nhất 10 ký tự. Thao tác cập nhật, audit và thông báo nằm trong
cùng transaction. Có thể mở lại yêu cầu; đây chưa phải hệ thống chat.

Chưa có tệp đính kèm, hội thoại nhiều tin nhắn, phân công nhân viên hoặc SLA.
Không có cơ chế tự thu/hoàn tiền hay sửa trạng thái đơn qua xử lý hỗ trợ.

## Đổi mật khẩu: phạm vi bản hiện tại

`POST /api/auth/change-password` yêu cầu JWT, `currentPassword` và `newPassword`.
Mật khẩu mới tối thiểu 12 ký tự, tối đa 72 byte UTF-8 để không bị BCrypt cắt
ngầm; kiểm tra mật khẩu hiện tại, không cho dùng lại mật khẩu cũ. Hai endpoint
bảo mật dùng chung giới hạn 8 lần thử/5 phút/tài khoản trên mỗi API instance,
trả 429 và Retry-After.
Giới hạn này chưa phải bộ hạn chế phân tán cho login/signup.

Cập nhật hash, tăng TokenVersion, audit và thông báo cùng transaction; hai lần
đổi đồng thời chỉ một lần được lưu. Audit không lưu mật khẩu hoặc hash. Sau
thành công, kể cả phiên đang thao tác cũng phải đăng nhập lại. Sai mật khẩu
hiện tại trả 400 và giữ phiên. JWT thiếu claim phiên bản chỉ được chấp nhận
cho tài khoản TokenVersion=0; tương thích phiên cũ trước lần đổi đầu tiên.

`POST /api/auth/logout-all` tăng TokenVersion, lưu audit và thông báo trong cùng
transaction, không đổi hash mật khẩu. Chỉ tác động tài khoản của JWT hiện tại;
hai yêu cầu đồng thời chỉ một lần được lưu. Không cần mật khẩu nên dùng được
cho tài khoản Google. Không đăng xuất phiên tại Google hoặc chặn đăng nhập mới.

Tài khoản chỉ đăng nhập Google (chưa có password hash) quản lý mật khẩu qua
Google; luồng đổi mật khẩu không tự tạo mật khẩu riêng. Quên mật khẩu, xác minh
email, danh sách thiết bị và thu hồi từng thiết bị chưa được triển khai.

Hai provider có migration `AddTokenVersion`; startup API áp dụng migration.
Cần cập nhật tất cả API instance để cùng kiểm tra phiên bản token. Các test
chỉ chạy trên SQL tạm, không nâng cấp database ứng dụng. App export Android
không thay thế kiểm thử đổi mật khẩu trên điện thoại thật.

## Các repository cập nhật trong bước tài khoản

`backend`, `admin`, `web`, `mobile`, `cleaner-web`, `cleaner-mobile`.
Branch đề xuất: `account-security`.

Commit text dùng cho từng repository:

```text
Add password changes and session revocation controls
```

Hai provider có migration `AddSupportTickets`. Startup API áp dụng migration
theo connection string đã cấu hình. Các kiểm thử không dùng database ứng dụng;
chỉ tạo/migrate/xóa database có tên test. Kiểm thử SQL/snapshot PostgreSQL không
thay thế việc thử nâng cấp trên PostgreSQL thật trước triển khai provider đó.
