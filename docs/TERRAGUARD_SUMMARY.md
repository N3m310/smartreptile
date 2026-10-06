# TERRAGUARD – Tóm tắt Prototype

**Ngày:** 30/09/2026  
**Công nghệ:** React 19 + Vite + Tailwind CSS v4 + TypeScript

---

## Cấu trúc dự án

```
src/
├── index.css                  # Theme màu sắc, font, CSS toàn cục
├── App.tsx                    # Router chính (BrowserRouter)
├── data/
│   └── mockData.ts            # Dữ liệu giả lập (terrarium, thiết bị, cảnh báo)
├── components/
│   ├── Layout.tsx             # Sidebar + Header dùng chung
│   └── MockDataNotice.tsx     # Nhãn "Dữ liệu mô phỏng" — hiện ở trang đăng nhập và trong app shell (task 4.14)
└── pages/
    ├── Login.tsx              # Trang đăng nhập
    ├── Dashboard.tsx          # Dashboard tổng quan
    ├── Terrariums.tsx         # Danh sách terrarium
    ├── TerrariumDetail.tsx    # Chi tiết một terrarium
    ├── Devices.tsx            # Danh sách thiết bị IoT
    ├── Alerts.tsx             # Trang cảnh báo
    ├── History.tsx            # Lịch sử dữ liệu
    └── Settings.tsx           # Cài đặt hệ thống
```

---

## Thiết kế (Design System)

| Yếu tố | Giá trị |
|--------|---------|
| Font hiển thị | Outfit (heading, logo) |
| Font nội dung | DM Sans (body text) |
| Font dữ liệu | JetBrains Mono (giá trị cảm biến) |
| Màu nền | `#0b1a0d` – xanh rừng tối |
| Màu card | `#112016` |
| Màu text | `#dcd5c4` – beige/off-white |
| Màu primary | `#4a9e6a` – xanh lá |
| Màu accent | `#c87f3a` – hổ phách |
| Bình thường | `#4a9e6a` (xanh) |
| Cảnh báo | `#e8a832` (vàng) |
| Nguy hiểm | `#e05530` (đỏ) |
| Ngoại tuyến | `#556055` (xám) |

> **Lưu ý (2026-10-03).** Đây là **nguyên mẫu giao diện với dữ liệu mô phỏng**: không gọi API, không hiển thị số đo
> thật, và mọi màn hình đều mang nhãn "Dữ liệu mô phỏng" (`src/components/MockDataNotice.tsx`). Bảng màu trạng thái
> ở trên là **riêng cho nguyên mẫu** — lý do và tỉ số tương phản đo được nằm ở đầu `src/index.css`. Nguyên mẫu
> **không phải** web dashboard của đồ án: dashboard là `web/legacy/` và chạy trên API thật (`ADR-018`).

> **Cập nhật (2026-10-06) — `ADR-019` thu hồi (`revoke`) quyết định trên.** Nguyên mẫu TERRAGUARD **trở thành** giao diện web
> của mốc M4, và `web/legacy/` sẽ bị gỡ bỏ khi hoàn tất việc chuyển đổi (task 4.19). Việc "thăng cấp" này đi kèm
> các nghĩa vụ mà trước đây nguyên mẫu được miễn: nối API thật (4.15–4.16), lấy kết luận ngưỡng từ máy chủ thay vì
> `value > max` trong màn hình (4.17), song ngữ vi+en với bộ key dùng chung với app (4.18), dùng token màu của
> `02-design/04` §3 ở mức ≥ 4.5:1 (4.18), dựng lại màn hình wallboard bằng React (4.19), và nối các màn hình còn lại
> khi endpoint của chúng hoàn thành (4.20). Cho đến khi một màn hình được nối xong, màn hình đó vẫn giữ nhãn "Dữ
> liệu mô phỏng" và **không** được tính vào DoD. Bảng màu riêng của nguyên mẫu cũng sẽ được thay bằng token của §3.

---

## Các trang đã xây dựng

### 1. Đăng nhập (`/`)
- Form email + mật khẩu với toggle hiện/ẩn mật khẩu
- Panel trái: logo, slogan, 3 stat card (terrarium / cảm biến / cảnh báo)
- Checkbox "Ghi nhớ đăng nhập" + link "Quên mật khẩu?"
- Click "Đăng nhập" → chuyển đến Dashboard

### 2. Dashboard (`/dashboard`)
- **4 card tổng quan:** Terrarium hoạt động, Thiết bị online, Cảnh báo hiện tại, Trạng thái hệ thống
- **Card cảm biến:** Nhiệt độ / Độ ẩm / Ánh sáng của Terrarium #01 với trạng thái màu sắc
- **Biểu đồ đường:** Bật/tắt từng thông số; lọc theo 1h / 6h / 24h / 7 ngày
- **Danh sách terrarium** nhanh + **cảnh báo gần đây** ở phía dưới

### 3. Terrarium (`/terrariums`)
- Grid card với ảnh bìa (Unsplash), tên loài, badge trạng thái
- 3 mini stat: nhiệt độ / độ ẩm / ánh sáng
- Nút "+ Thêm Terrarium"
- Click card → chuyển đến chi tiết

### 4. Chi tiết Terrarium (`/terrariums/:id`)
- Ảnh terrarium, tên loài, badge trạng thái
- Gauge cảm biến với progress bar + khoảng ngưỡng cài đặt
- 3 biểu đồ riêng lẻ (nhiệt độ / độ ẩm / ánh sáng) có đường ngưỡng min/max
- Danh sách thiết bị của terrarium đó

### 5. Thiết bị (`/devices`)
- Bảng danh sách 9 thiết bị với tìm kiếm
- Hiển thị: tên, loại, terrarium, trạng thái online/offline, firmware, last seen
- Banner cảnh báo khi có thiết bị offline

### 6. Cảnh báo (`/alerts`)
- Lọc theo: Tất cả / Chưa xử lý / Đã xử lý
- Thẻ cảnh báo với icon theo loại, màu severity
- Nút "Đã kiểm tra" → cập nhật trạng thái sang "Đã xử lý"
- **Logic ngưỡng** (không AI): so sánh giá trị cảm biến vs min/max đã cài

### 7. Lịch sử dữ liệu (`/history`)
- Bộ lọc: chọn terrarium, loại cảm biến, khoảng thời gian
- Bảng dữ liệu với cột Thời gian / Nhiệt độ / Độ ẩm / Ánh sáng / Trạng thái
- Nút "Xuất CSV"

### 8. Cài đặt (`/settings`)
- **Thông tin tài khoản:** chỉnh sửa tên, email, SĐT
- **Thông báo:** toggle bật/tắt từng loại cảnh báo
- **Cấu hình ngưỡng:** chọn terrarium → nhập min/max cho nhiệt độ/độ ẩm/ánh sáng → Lưu
- **Đăng xuất** → quay về trang Login

---

## Dữ liệu giả lập (`mockData.ts`)

| Loại | Nội dung |
|------|---------|
| Terrarium | 3 (Rồng Úc, Tắc kè Leopard, Rắn Ngô) |
| Thiết bị | 9 (ESP32, DHT22, LDR cho mỗi terrarium) |
| Trạng thái | T01: bình thường · T02: cảnh báo (offline) · T03: nguy hiểm |
| Cảnh báo | 4 (nhiệt độ cao, mất kết nối, độ ẩm thấp, đã xử lý) |
| Lịch sử | 24 điểm dữ liệu mỗi terrarium (sinh ngẫu nhiên) |

---

## Luồng demo chính

```
Đăng nhập
  → Dashboard (tổng quan, biểu đồ)
    → Terrarium #01 (chi tiết + biểu đồ + thiết bị)
  → Cảnh báo (xem + đánh dấu đã kiểm tra)
  → Cài đặt → Cấu hình ngưỡng → Lưu
```

---

## Thư viện sử dụng

| Thư viện | Mục đích |
|----------|---------|
| `react-router-dom` | Điều hướng giữa các trang |
| `recharts` | Biểu đồ đường (LineChart) |
| `lucide-react` | Bộ icon |

---

## Lưu ý quan trọng

- **Không sử dụng AI** – toàn bộ cảnh báo dựa trên logic ngưỡng `if value > max → alert`
- **Không có backend** – mọi dữ liệu là mock, sẵn sàng thay thế bằng MQTT/HTTP thật
- Giao diện **hoàn toàn tiếng Việt**
- Responsive: Desktop / Tablet / Mobile (sidebar collapse)
