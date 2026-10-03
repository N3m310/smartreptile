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
│   └── Layout.tsx             # Sidebar + Header dùng chung
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
