# CleanMate: trạng thái và phần còn thiếu

Rà soát mã nguồn ngày 16/10/2026. Tài liệu phản ánh các repository local;
không xác nhận trạng thái hosting, tài khoản dịch vụ hoặc bản app đã phát hành.

## Đã có

- Web/app khách: đặt đơn với giá từ server, liên hệ, đổi/hủy lịch khi chưa được
  nhận, theo dõi ca, xác nhận hoàn thành và đánh giá.
- Web/app cleaner riêng: đăng ký/chờ duyệt, nhận ca, chống trùng lịch, bắt đầu/
  hoàn thành, xác nhận tiền mặt, lọc lịch/công việc và liên hệ khách.
- Web Admin/Staff riêng: dashboard, duyệt cleaner, cấu hình giá, tra cứu đơn,
  quản lý Staff và nhật ký thao tác.
- Thông báo trong ứng dụng, trạng thái đã đọc, phân trang và mở đúng khu vực.
- Hỗ trợ theo đơn: khách và cleaner được giao ca gửi báo cáo; staff phản hồi,
  cập nhật trạng thái và gửi thông báo cho người báo. Có chống gửi trùng khi
  thử lại, kiểm tra quyền và chống ghi đè cập nhật giữa nhiều staff.
- Backend có migration SQL Server/PostgreSQL và các bộ kiểm thử tự động trên
  SQL tạm. Web được test desktop/mobile browser; app được export Android.

Tiền mặt được khách trả trực tiếp cho cleaner sau ca. Số tiền cleaner xác nhận
đã nhận không phải số dư hoặc doanh thu của nền tảng.

## Các phần cần làm tiếp, theo thứ tự đề xuất

1. **Tài khoản:** đổi mật khẩu, quên mật khẩu và xác minh email; hiện API auth
   chưa có các luồng này. Cần thiết kế thu hồi phiên cũ và dịch vụ gửi email.
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

## Các repository cập nhật trong bước hỗ trợ

`backend`, `admin`, `web`, `mobile`, `cleaner-web`, `cleaner-mobile`.
Branch đề xuất: `booking-support`.

Commit text dùng cho từng repository:

```text
Add booking support reports and staff resolution workflow
```

Hai provider có migration `AddSupportTickets`. Startup API áp dụng migration
theo connection string đã cấu hình. Các kiểm thử không dùng database ứng dụng;
chỉ tạo/migrate/xóa database có tên test. Kiểm thử SQL/snapshot PostgreSQL không
thay thế việc thử nâng cấp trên PostgreSQL thật trước triển khai provider đó.
