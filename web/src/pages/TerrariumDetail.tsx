import React from 'react';
import { useParams, Link } from 'react-router-dom';
import {
  ArrowLeft,
  Thermometer,
  Droplets,
  Sun,
  Cpu,
  AlertCircle,
  Radio,
  Sliders,
} from 'lucide-react';
import {
  LineChart,
  Line,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  ResponsiveContainer,
  ReferenceLine,
} from 'recharts';
import {
  initialTerrariums,
  initialDevices,
  generateHistoryData,
} from '../data/mockData';

export const TerrariumDetail: React.FC = () => {
  const { id } = useParams<{ id: string }>();
  const terrarium =
    initialTerrariums.find((t) => t.id === id) || initialTerrariums[0];
  const devices = initialDevices.filter((d) => d.terrariumId === terrarium.id);
  const chartData = generateHistoryData(terrarium.id);

  const getStatusBadge = (status: 'normal' | 'warning' | 'danger') => {
    switch (status) {
      case 'normal':
        return (
          <span className="px-3 py-1 rounded-full text-xs font-semibold bg-[#4a9e6a] text-white">
            Bình thường
          </span>
        );
      case 'warning':
        return (
          <span className="px-3 py-1 rounded-full text-xs font-semibold bg-[#e8a832] text-black">
            Cảnh báo ngưỡng
          </span>
        );
      case 'danger':
        return (
          <span className="px-3 py-1 rounded-full text-xs font-semibold bg-[#e05530] text-white animate-pulse">
            Vi phạm nghiêm trọng
          </span>
        );
    }
  };

  return (
    <div className="space-y-8">
      {/* Back button and Header */}
      <div>
        <Link
          to="/terrariums"
          className="inline-flex items-center gap-2 text-xs font-semibold text-[#8e9e8f] hover:text-[#4a9e6a] transition-colors mb-4"
        >
          <ArrowLeft className="w-4 h-4" />
          <span>Quay lại danh sách Terrarium</span>
        </Link>

        {/* Hero Card */}
        <div className="bg-[#112016] border border-[#1e3825] rounded-3xl overflow-hidden shadow-2xl grid grid-cols-1 lg:grid-cols-12">
          <div className="lg:col-span-5 h-64 lg:h-auto relative">
            <img
              src={terrarium.image}
              alt={terrarium.species}
              className="w-full h-full object-cover"
            />
            <div className="absolute inset-0 bg-gradient-to-t lg:bg-gradient-to-r from-transparent to-[#112016]/90"></div>
          </div>

          <div className="lg:col-span-7 p-6 sm:p-8 flex flex-col justify-between space-y-4">
            <div>
              <div className="flex items-center justify-between gap-4">
                <span className="text-xs font-mono font-bold text-[#4a9e6a] uppercase tracking-wider">
                  Mã chuồng: {terrarium.id}
                </span>
                {getStatusBadge(terrarium.status)}
              </div>
              <h1 className="text-2xl sm:text-3xl font-heading font-extrabold text-[#dcd5c4] mt-1">
                {terrarium.name}
              </h1>
              <p className="text-sm font-medium text-[#c87f3a] mt-0.5">
                {terrarium.species}
              </p>
              <p className="text-xs text-[#8e9e8f] mt-3 leading-relaxed">
                {terrarium.description}
              </p>
            </div>

            <div className="pt-4 border-t border-[#1e3825] flex flex-wrap items-center justify-between gap-4 text-xs text-[#8e9e8f]">
              <div className="flex items-center gap-2">
                <Radio className="w-4 h-4 text-[#4a9e6a] animate-pulse" />
                <span>MQTT Broker kết nối: <strong className="text-[#dcd5c4]">1883</strong></span>
              </div>
              <Link
                to="/settings"
                className="flex items-center gap-1.5 text-[#4a9e6a] hover:underline font-semibold"
              >
                <Sliders className="w-4 h-4" />
                <span>Điều chỉnh khoảng ngưỡng</span>
              </Link>
            </div>
          </div>
        </div>
      </div>

      {/* 3 Live Gauges with Progress & Min/Max */}
      <div>
        <h2 className="text-lg font-heading font-bold text-[#dcd5c4] mb-4">
          Thông số môi trường hiện tại & Ngưỡng an toàn
        </h2>
        <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
          {/* Temperature */}
          <div className="bg-[#112016] border border-[#1e3825] p-6 rounded-3xl space-y-4">
            <div className="flex items-center justify-between">
              <span className="text-xs font-semibold uppercase text-[#8e9e8f] flex items-center gap-2">
                <Thermometer className="w-4 h-4 text-[#e05530]" />
                Nhiệt độ
              </span>
              <span className="text-[11px] font-mono text-[#8e9e8f]">
                {terrarium.thresholds.tempMin}°C - {terrarium.thresholds.tempMax}°C
              </span>
            </div>
            <div className="flex items-baseline gap-2">
              <span className="text-4xl font-mono font-extrabold text-[#dcd5c4]">
                {terrarium.currentTemp}
              </span>
              <span className="text-xl font-mono text-[#8e9e8f]">°C</span>
            </div>
            <div className="space-y-1.5">
              <div className="w-full h-2.5 bg-[#0b1a0d] rounded-full overflow-hidden border border-[#1e3825]">
                <div
                  className="h-full bg-[#e05530] rounded-full"
                  style={{
                    width: `${Math.min(
                      100,
                      ((terrarium.currentTemp - 15) / (45 - 15)) * 100
                    )}%`,
                  }}
                ></div>
              </div>
              <div className="flex justify-between text-[10px] text-[#8e9e8f] font-mono">
                <span>Min: {terrarium.thresholds.tempMin}°C</span>
                <span>Max: {terrarium.thresholds.tempMax}°C</span>
              </div>
            </div>
          </div>

          {/* Humidity */}
          <div className="bg-[#112016] border border-[#1e3825] p-6 rounded-3xl space-y-4">
            <div className="flex items-center justify-between">
              <span className="text-xs font-semibold uppercase text-[#8e9e8f] flex items-center gap-2">
                <Droplets className="w-4 h-4 text-[#4a9e6a]" />
                Độ ẩm
              </span>
              <span className="text-[11px] font-mono text-[#8e9e8f]">
                {terrarium.thresholds.humidityMin}% - {terrarium.thresholds.humidityMax}%
              </span>
            </div>
            <div className="flex items-baseline gap-2">
              <span className="text-4xl font-mono font-extrabold text-[#dcd5c4]">
                {terrarium.currentHumidity}
              </span>
              <span className="text-xl font-mono text-[#8e9e8f]">%</span>
            </div>
            <div className="space-y-1.5">
              <div className="w-full h-2.5 bg-[#0b1a0d] rounded-full overflow-hidden border border-[#1e3825]">
                <div
                  className="h-full bg-[#4a9e6a] rounded-full"
                  style={{ width: `${terrarium.currentHumidity}%` }}
                ></div>
              </div>
              <div className="flex justify-between text-[10px] text-[#8e9e8f] font-mono">
                <span>Min: {terrarium.thresholds.humidityMin}%</span>
                <span>Max: {terrarium.thresholds.humidityMax}%</span>
              </div>
            </div>
          </div>

          {/* Light */}
          <div className="bg-[#112016] border border-[#1e3825] p-6 rounded-3xl space-y-4">
            <div className="flex items-center justify-between">
              <span className="text-xs font-semibold uppercase text-[#8e9e8f] flex items-center gap-2">
                <Sun className="w-4 h-4 text-[#c87f3a]" />
                Ánh sáng
              </span>
              <span className="text-[11px] font-mono text-[#8e9e8f]">
                {terrarium.thresholds.lightMin} - {terrarium.thresholds.lightMax} Lux
              </span>
            </div>
            <div className="flex items-baseline gap-2">
              <span className="text-4xl font-mono font-extrabold text-[#dcd5c4]">
                {terrarium.currentLight}
              </span>
              <span className="text-xl font-mono text-[#8e9e8f]">Lux</span>
            </div>
            <div className="space-y-1.5">
              <div className="w-full h-2.5 bg-[#0b1a0d] rounded-full overflow-hidden border border-[#1e3825]">
                <div
                  className="h-full bg-[#c87f3a] rounded-full"
                  style={{
                    width: `${Math.min(
                      100,
                      (terrarium.currentLight / 1500) * 100
                    )}%`,
                  }}
                ></div>
              </div>
              <div className="flex justify-between text-[10px] text-[#8e9e8f] font-mono">
                <span>Min: {terrarium.thresholds.lightMin} Lx</span>
                <span>Max: {terrarium.thresholds.lightMax} Lx</span>
              </div>
            </div>
          </div>
        </div>
      </div>

      {/* 3 Detailed Charts with Threshold Reference Lines */}
      <div className="space-y-6">
        <h2 className="text-lg font-heading font-bold text-[#dcd5c4]">
          Biểu đồ phân tích chuyên sâu 24 giờ kèm đường ngưỡng
        </h2>

        {/* 1. Temp Chart */}
        <div className="bg-[#112016] border border-[#1e3825] p-6 rounded-3xl">
          <div className="flex items-center justify-between mb-4">
            <h3 className="text-sm font-semibold uppercase text-[#e05530] flex items-center gap-2">
              <Thermometer className="w-4 h-4" />
              Diễn biến Nhiệt độ (°C)
            </h3>
            <span className="text-xs font-mono text-[#8e9e8f]">
              Đường đỏ: Ngưỡng Max ({terrarium.thresholds.tempMax}°C) • Đường vàng: Ngưỡng Min ({terrarium.thresholds.tempMin}°C)
            </span>
          </div>
          <div className="h-60 w-full">
            <ResponsiveContainer width="100%" height="100%">
              <LineChart data={chartData}>
                <CartesianGrid strokeDasharray="3 3" stroke="#1e3825" />
                <XAxis dataKey="time" stroke="#8e9e8f" tick={{ fontSize: 11 }} />
                <YAxis stroke="#8e9e8f" tick={{ fontSize: 11 }} domain={['dataMin - 2', 'dataMax + 2']} />
                <Tooltip
                  contentStyle={{
                    backgroundColor: '#112016',
                    borderColor: '#1e3825',
                    borderRadius: '12px',
                    color: '#dcd5c4',
                    fontSize: '12px',
                  }}
                />
                <ReferenceLine
                  y={terrarium.thresholds.tempMax}
                  label={{ value: 'Max Temp', fill: '#e05530', fontSize: 10 }}
                  stroke="#e05530"
                  strokeDasharray="4 4"
                />
                <ReferenceLine
                  y={terrarium.thresholds.tempMin}
                  label={{ value: 'Min Temp', fill: '#e8a832', fontSize: 10 }}
                  stroke="#e8a832"
                  strokeDasharray="4 4"
                />
                <Line
                  type="monotone"
                  dataKey="temperature"
                  stroke="#e05530"
                  strokeWidth={2.5}
                  dot={{ r: 2 }}
                />
              </LineChart>
            </ResponsiveContainer>
          </div>
        </div>

        {/* 2. Humidity Chart */}
        <div className="bg-[#112016] border border-[#1e3825] p-6 rounded-3xl">
          <div className="flex items-center justify-between mb-4">
            <h3 className="text-sm font-semibold uppercase text-[#4a9e6a] flex items-center gap-2">
              <Droplets className="w-4 h-4" />
              Diễn biến Độ ẩm (%)
            </h3>
            <span className="text-xs font-mono text-[#8e9e8f]">
              Đường xanh: Min ({terrarium.thresholds.humidityMin}%) - Max ({terrarium.thresholds.humidityMax}%)
            </span>
          </div>
          <div className="h-60 w-full">
            <ResponsiveContainer width="100%" height="100%">
              <LineChart data={chartData}>
                <CartesianGrid strokeDasharray="3 3" stroke="#1e3825" />
                <XAxis dataKey="time" stroke="#8e9e8f" tick={{ fontSize: 11 }} />
                <YAxis stroke="#8e9e8f" tick={{ fontSize: 11 }} />
                <Tooltip
                  contentStyle={{
                    backgroundColor: '#112016',
                    borderColor: '#1e3825',
                    borderRadius: '12px',
                    color: '#dcd5c4',
                    fontSize: '12px',
                  }}
                />
                <ReferenceLine
                  y={terrarium.thresholds.humidityMax}
                  stroke="#4a9e6a"
                  strokeDasharray="4 4"
                  label={{ value: 'Max Hum', fill: '#4a9e6a', fontSize: 10 }}
                />
                <ReferenceLine
                  y={terrarium.thresholds.humidityMin}
                  stroke="#e8a832"
                  strokeDasharray="4 4"
                  label={{ value: 'Min Hum', fill: '#e8a832', fontSize: 10 }}
                />
                <Line
                  type="monotone"
                  dataKey="humidity"
                  stroke="#4a9e6a"
                  strokeWidth={2.5}
                  dot={{ r: 2 }}
                />
              </LineChart>
            </ResponsiveContainer>
          </div>
        </div>

        {/* 3. Light Chart */}
        <div className="bg-[#112016] border border-[#1e3825] p-6 rounded-3xl">
          <div className="flex items-center justify-between mb-4">
            <h3 className="text-sm font-semibold uppercase text-[#c87f3a] flex items-center gap-2">
              <Sun className="w-4 h-4" />
              Cường độ Ánh sáng (Lux)
            </h3>
            <span className="text-xs font-mono text-[#8e9e8f]">
              Quang chu kỳ ngày/đêm
            </span>
          </div>
          <div className="h-60 w-full">
            <ResponsiveContainer width="100%" height="100%">
              <LineChart data={chartData}>
                <CartesianGrid strokeDasharray="3 3" stroke="#1e3825" />
                <XAxis dataKey="time" stroke="#8e9e8f" tick={{ fontSize: 11 }} />
                <YAxis stroke="#8e9e8f" tick={{ fontSize: 11 }} />
                <Tooltip
                  contentStyle={{
                    backgroundColor: '#112016',
                    borderColor: '#1e3825',
                    borderRadius: '12px',
                    color: '#dcd5c4',
                    fontSize: '12px',
                  }}
                />
                <Line
                  type="monotone"
                  dataKey="light"
                  stroke="#c87f3a"
                  strokeWidth={2}
                  dot={false}
                />
              </LineChart>
            </ResponsiveContainer>
          </div>
        </div>
      </div>

      {/* Terrarium IoT Devices List */}
      <div className="bg-[#112016] border border-[#1e3825] rounded-3xl p-6 sm:p-8">
        <h2 className="text-lg font-heading font-bold text-[#dcd5c4] mb-4 flex items-center gap-2">
          <Cpu className="w-5 h-5 text-[#4a9e6a]" />
          <span>Danh sách thiết bị kết nối với {terrarium.name}</span>
        </h2>

        <div className="overflow-x-auto">
          <table className="w-full text-left text-xs">
            <thead>
              <tr className="border-b border-[#1e3825] text-[#8e9e8f] uppercase">
                <th className="pb-3 font-semibold">Tên thiết bị</th>
                <th className="pb-3 font-semibold">Loại phần cứng</th>
                <th className="pb-3 font-semibold">Trạng thái</th>
                <th className="pb-3 font-semibold">Firmware</th>
                <th className="pb-3 font-semibold">Lần cuối phản hồi</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-[#1e3825]">
              {devices.map((device) => (
                <tr key={device.id} className="hover:bg-[#162a1d]/40 transition-colors">
                  <td className="py-3.5 font-bold text-[#dcd5c4]">
                    {device.name}
                    <span className="block text-[10px] text-[#8e9e8f] font-mono">
                      {device.id}
                    </span>
                  </td>
                  <td className="py-3.5 text-[#8e9e8f]">{device.type}</td>
                  <td className="py-3.5">
                    {device.status === 'online' ? (
                      <span className="inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full text-[11px] font-semibold bg-[#4a9e6a]/20 text-[#4a9e6a] border border-[#4a9e6a]/30">
                        <span className="w-1.5 h-1.5 rounded-full bg-[#4a9e6a]"></span>
                        Trực tuyến
                      </span>
                    ) : (
                      <span className="inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full text-[11px] font-semibold bg-[#556055]/20 text-[#8e9e8f] border border-[#556055]/30">
                        <span className="w-1.5 h-1.5 rounded-full bg-[#556055]"></span>
                        Ngoại tuyến
                      </span>
                    )}
                  </td>
                  <td className="py-3.5 font-mono text-[#dcd5c4]">
                    {device.firmware}
                  </td>
                  <td className="py-3.5 text-[#8e9e8f]">{device.lastSeen}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
};

