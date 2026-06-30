# OrderManager

OrderManager là một Bot cho cTrader dùng để lưu lại các lệnh chờ ra một file JSON và khôi phục lại các lệnh đó sau này.

## Bot này làm được gì?

- Lưu danh sách lệnh chờ hiện có ra file.
- Đọc lại file đã lưu và tạo lại các lệnh chờ.
- Có thể xóa các lệnh chờ đang tồn tại trước khi khôi phục.
- Bỏ qua các lệnh trùng lặp để không tạo ra hai lệnh giống nhau.
- Ghi log để bạn biết Bot đã lưu hoặc khôi phục được bao nhiêu lệnh.

## Cách dùng ngắn gọn

1. Chọn chế độ `Save` nếu muốn xuất các lệnh chờ hiện tại ra file.
2. Chọn chế độ `Restore` nếu muốn nạp lại các lệnh chờ từ file đã lưu.
3. Chọn `None` nếu chỉ muốn Bot khởi động nhưng không làm gì.

## File dữ liệu được lưu ở đâu?

- Mặc định, file JSON sẽ được lưu trong thư mục `Documents\\cTrader\\OrderManager` của máy bạn.
- Bạn cũng có thể nhập đường dẫn đầy đủ nếu muốn lưu ở nơi khác.

## Khi khôi phục lệnh

- Bot sẽ bỏ qua file không tồn tại thay vì tạo lỗi.
- Nếu symbol không còn tồn tại, lệnh đó sẽ bị bỏ qua an toàn.
- Bot chỉ khôi phục các loại lệnh chờ mà nó hỗ trợ.

## Ghi chú

Bot này phù hợp khi bạn muốn sao lưu nhanh các lệnh chờ trước khi thay đổi hệ thống, tắt máy, hoặc chuyển sang môi trường khác.