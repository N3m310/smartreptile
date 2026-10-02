export interface ThresholdConfig {
  tempMin: number;
  tempMax: number;
  humidityMin: number;
  humidityMax: number;
  lightMin: number;
  lightMax: number;
}

export interface Terrarium {
  id: string;
  name: string;
  species: string;
  image: string;
  status: 'normal' | 'warning' | 'danger';
  currentTemp: number; // °C
  currentHumidity: number; // %
  currentLight: number; // Lux
  thresholds: ThresholdConfig;
  description: string;
  deviceCount: number;
}

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

export interface HistoryDataPoint {
  time: string;
  temperature: number;
  humidity: number;
  light: number;
  status: 'normal' | 'warning' | 'danger';
}

export const initialTerrariums: Terrarium[] = [
  {
    id: 'T01',
    name: 'Terrarium #01',
    species: 'Rồng Úc (Bearded Dragon)',
    image: 'https://images.unsplash.com/photo-1574063413132-355dbfd83e25?auto=format&fit=crop&w=800&q=80',
    status: 'normal',
    currentTemp: 34.5,
    currentHumidity: 38.0,
    currentLight: 850,
    thresholds: {
      tempMin: 28.0,
      tempMax: 38.0,
      humidityMin: 30.0,
      humidityMax: 45.0,
      lightMin: 500,
      lightMax: 1200,
    },
    description: 'Khu vực sa mạc khô nóng, cần nhiệt độ sưởi ấm cao vào ban ngày.',
    deviceCount: 3,
  },
  {
    id: 'T02',
    name: 'Terrarium #02',
    species: 'Tắc kè Leopard (Leopard Gecko)',
    image: 'https://images.unsplash.com/photo-1508817628294-5a453fa0b8fb?auto=format&fit=crop&w=800&q=80',
    status: 'warning',
    currentTemp: 29.8,
    currentHumidity: 42.0,
    currentLight: 120,
    thresholds: {
      tempMin: 26.0,
      tempMax: 32.0,
      humidityMin: 30.0,
      humidityMax: 40.0,
      lightMin: 50,
      lightMax: 400,
    },
    description: 'Loài hoạt động hoàng hôn/đêm, kiểm soát nhiệt nền ổn định.',
    deviceCount: 3,
  },
  {
    id: 'T03',
    name: 'Terrarium #03',
    species: 'Rắn Ngô (Corn Snake)',
    image: 'https://images.unsplash.com/photo-1531386151447-fd76ad50012f?auto=format&fit=crop&w=800&q=80',
    status: 'danger',
    currentTemp: 33.2,
    currentHumidity: 32.0,
    currentLight: 340,
    thresholds: {
      tempMin: 24.0,
      tempMax: 30.0,
      humidityMin: 45.0,
      humidityMax: 65.0,
      lightMin: 100,
      lightMax: 600,
    },
    description: 'Cần duy trì độ ẩm vừa phải để hỗ trợ chu kỳ lột xác an toàn.',
    deviceCount: 3,
  },
];

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

// Sinh dữ liệu lịch sử 24 giờ cho 1 terrarium
export const generateHistoryData = (terrariumId: string): HistoryDataPoint[] => {
  const points: HistoryDataPoint[] = [];
  const baseTemp = terrariumId === 'T01' ? 34 : terrariumId === 'T02' ? 29 : 27;
  const baseHumidity = terrariumId === 'T01' ? 38 : terrariumId === 'T02' ? 38 : 55;
  const baseLight = terrariumId === 'T01' ? 800 : terrariumId === 'T02' ? 150 : 300;

  for (let i = 23; i >= 0; i--) {
    const hour = (24 - i).toString().padStart(2, '0') + ':00';
    // Dao động nhẹ theo giờ
    const tempOffset = Math.sin(i / 3) * 3 + (Math.random() * 0.8 - 0.4);
    const humOffset = -Math.sin(i / 3) * 4 + (Math.random() * 1.2 - 0.6);
    const lightFactor = i >= 6 && i <= 18 ? 1 : 0.05;

    const temp = Number((baseTemp + tempOffset).toFixed(1));
    const hum = Number((baseHumidity + humOffset).toFixed(1));
    const light = Math.max(0, Math.round(baseLight * lightFactor + Math.random() * 50));

    let status: 'normal' | 'warning' | 'danger' = 'normal';
    if (terrariumId === 'T03' && i < 3) {
      status = 'danger';
    } else if (terrariumId === 'T02' && i < 2) {
      status = 'warning';
    }

    points.push({
      time: hour,
      temperature: temp,
      humidity: hum,
      light: light,
      status: status,
    });
  }
  return points;
};

