export interface Device {
  id: string;
  name: string;
  type: 'ESP32 Controller' | 'DHT22 Cảm biến Nhiệt/Ẩm' | 'LDR Cảm biến Ánh sáng';
  terrariumId: string;
  terrariumName: string;
  status: 'online' | 'offline';
  firmware: string;
  lastSeen: string;
  ipAddress: string;
}

export interface AlertItem {
  id: string;
  title: string;
  message: string;
  terrariumId: string;
  terrariumName: string;
  type: 'temp_high' | 'temp_low' | 'humidity_low' | 'humidity_high' | 'device_offline' | 'light_abnormal';
  severity: 'danger' | 'warning';
  status: 'pending' | 'resolved';
  timestamp: string;
  resolvedAt?: string;
}

export const initialDevices: Device[] = [
  {
    id: 'DEV-01-ESP',
    name: 'ESP32 Node 01',
    type: 'ESP32 Controller',
    terrariumId: 'T01',
    terrariumName: 'Terrarium #01 (Rồng Úc)',
    status: 'online',
    firmware: 'v1.4.2',
    lastSeen: '1 phút trước',
    ipAddress: '192.168.1.101',
  },
  {
    id: 'DEV-01-DHT',
    name: 'DHT22 Cảm biến T/H 01',
    type: 'DHT22 Cảm biến Nhiệt/Ẩm',
    terrariumId: 'T01',
    terrariumName: 'Terrarium #01 (Rồng Úc)',
    status: 'online',
    firmware: 'v1.2.0',
    lastSeen: 'Vừa xong',
    ipAddress: 'Nội bộ GPIO4',
  },
  {
    id: 'DEV-01-LDR',
    name: 'LDR Ánh sáng 01',
    type: 'LDR Cảm biến Ánh sáng',
    terrariumId: 'T01',
    terrariumName: 'Terrarium #01 (Rồng Úc)',
    status: 'online',
    firmware: 'v1.1.0',
    lastSeen: 'Vừa xong',
    ipAddress: 'Nội bộ ADC34',
  },
  {
    id: 'DEV-02-ESP',
    name: 'ESP32 Node 02',
    type: 'ESP32 Controller',
    terrariumId: 'T02',
    terrariumName: 'Terrarium #02 (Tắc kè Leopard)',
    status: 'online',
    firmware: 'v1.4.2',
    lastSeen: '3 phút trước',
    ipAddress: '192.168.1.102',
  },
  {
    id: 'DEV-02-DHT',
    name: 'DHT22 Cảm biến T/H 02',
    type: 'DHT22 Cảm biến Nhiệt/Ẩm',
    terrariumId: 'T02',
    terrariumName: 'Terrarium #02 (Tắc kè Leopard)',
    status: 'online',
    firmware: 'v1.2.0',
    lastSeen: 'Vừa xong',
    ipAddress: 'Nội bộ GPIO4',
  },
  {
    id: 'DEV-02-LDR',
    name: 'LDR Ánh sáng 02',
    type: 'LDR Cảm biến Ánh sáng',
    terrariumId: 'T02',
    terrariumName: 'Terrarium #02 (Tắc kè Leopard)',
    status: 'offline',
    firmware: 'v1.1.0',
    lastSeen: '45 phút trước',
    ipAddress: 'Nội bộ ADC34',
  },
  {
    id: 'DEV-03-ESP',
    name: 'ESP32 Node 03',
    type: 'ESP32 Controller',
    terrariumId: 'T03',
    terrariumName: 'Terrarium #03 (Rắn Ngô)',
    status: 'online',
    firmware: 'v1.4.2',
    lastSeen: 'Vừa xong',
    ipAddress: '192.168.1.103',
  },
  {
    id: 'DEV-03-DHT',
    name: 'DHT22 Cảm biến T/H 03',
    type: 'DHT22 Cảm biến Nhiệt/Ẩm',
    terrariumId: 'T03',
    terrariumName: 'Terrarium #03 (Rắn Ngô)',
    status: 'online',
    firmware: 'v1.2.0',
    lastSeen: 'Vừa xong',
    ipAddress: 'Nội bộ GPIO4',
  },
  {
    id: 'DEV-03-LDR',
    name: 'LDR Ánh sáng 03',
    type: 'LDR Cảm biến Ánh sáng',
    terrariumId: 'T03',
    terrariumName: 'Terrarium #03 (Rắn Ngô)',
    status: 'online',
    firmware: 'v1.1.0',
    lastSeen: 'Vừa xong',
    ipAddress: 'Nội bộ ADC34',
  },
];

export const initialAlerts: AlertItem[] = [
  {
    id: 'ALT-101',
    title: 'Nhiệt độ vượt ngưỡng an toàn',
    message: 'Nhiệt độ đo được 33.2°C vượt ngưỡng tối đa (30.0°C) cho Rắn Ngô.',
    terrariumId: 'T03',
    terrariumName: 'Terrarium #03 (Rắn Ngô)',
    type: 'temp_high',
    severity: 'danger',
    status: 'pending',
    timestamp: '10 phút trước',
  },
  {
    id: 'ALT-102',
    title: 'Thiết bị mất kết nối ngoại tuyến',
    message: 'Cảm biến LDR Ánh sáng 02 không phản hồi telemetry trong 45 phút.',
    terrariumId: 'T02',
    terrariumName: 'Terrarium #02 (Tắc kè Leopard)',
    type: 'device_offline',
    severity: 'warning',
    status: 'pending',
    timestamp: '45 phút trước',
  },
  {
    id: 'ALT-103',
    title: 'Độ ẩm xuống mức thấp',
    message: 'Độ ẩm 32.0% thấp hơn ngưỡng tối thiểu 45.0% đối với Rắn Ngô.',
    terrariumId: 'T03',
    terrariumName: 'Terrarium #03 (Rắn Ngô)',
    type: 'humidity_low',
    severity: 'danger',
    status: 'pending',
    timestamp: '1 giờ trước',
  },
  {
    id: 'ALT-104',
    title: 'Nhiệt độ ban đêm hạ thấp (Đã xử lý)',
    message: 'Nhiệt độ giảm xuống 26.5°C tại Terrarium #01. Đã bật đèn sưởi dự phòng.',
    terrariumId: 'T01',
    terrariumName: 'Terrarium #01 (Rồng Úc)',
    type: 'temp_low',
    severity: 'warning',
    status: 'resolved',
    timestamp: 'Hôm qua lúc 22:30',
    resolvedAt: 'Hôm qua lúc 22:45',
  },
];
