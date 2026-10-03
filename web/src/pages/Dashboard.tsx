import React, { useState } from 'react';
import { Link } from 'react-router-dom';
import {
  Box,
  Cpu,
  AlertTriangle,
  Activity,
  Thermometer,
  Droplets,
  Sun,
  ChevronRight,
  TrendingUp,
  Clock,
  ArrowUpRight,
} from 'lucide-react';
import {
  LineChart,
  Line,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  ResponsiveContainer,
  Legend,
} from 'recharts';
import {
  initialTerrariums,
  initialDevices,
  initialAlerts,
  generateHistoryData,
} from '../data/mockData';

export const Dashboard: React.FC = () => {
  const [selectedTerrariumId, setSelectedTerrariumId] = useState('T01');
  const [timeRange, setTimeRange] = useState<'1h' | '6h' | '24h' | '7d'>('24h');
  const [showTemp, setShowTemp] = useState(true);
  const [showHumidity, setShowHumidity] = useState(true);
  const [showLight, setShowLight] = useState(false);

  const terrariums = initialTerrariums;
  const devices = initialDevices;
  const alerts = initialAlerts.filter((a) => a.status === 'pending');
  const onlineDevices = devices.filter((d) => d.status === 'online').length;

  const currentTerrarium =
    terrariums.find((t) => t.id === selectedTerrariumId) || terrariums[0];
  const chartData = generateHistoryData(selectedTerrariumId);

  // Status badge helper
  const getStatusBadge = (status: 'normal' | 'warning' | 'danger') => {
    switch (status) {
      case 'normal':
        return (
          <span className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full text-xs font-semibold bg-[#4a9e6a]/20 text-[#4a9e6a] border border-[#4a9e6a]/30">
            <span className="w-1.5 h-1.5 rounded-full bg-[#4a9e6a]"></span>
            Bình thường
          </span>
        );
      case 'warning':
        return (
          <span className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full text-xs font-semibold bg-[#e8a832]/20 text-[#e8a832] border border-[#e8a832]/30">
            <span className="w-1.5 h-1.5 rounded-full bg-[#e8a832]"></span>
            Cảnh báo
          </span>
        );
      case 'danger':
        return (
          <span className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full text-xs font-semibold bg-[#e05530]/20 text-[#e05530] border border-[#e05530]/30 animate-pulse">
            <span className="w-1.5 h-1.5 rounded-full bg-[#e05530]"></span>
            Nguy hiểm
          </span>
        );
    }
  };

  return (
    <div className="space-y-8">
      {/* Page Title & Controls */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <div>
          <h1 className="text-2xl sm:text-3xl font-heading font-extrabold text-[#dcd5c4]">
            Bảng điều khiển Giám sát
          </h1>
          <p className="text-sm text-[#8e9e8f] mt-1">
            Theo dõi tức thời 3 terrarium, 9 cảm biến IoT và phân tích môi trường sống
          </p>
        </div>
        <div className="flex items-center gap-3">
          <Link
            to="/terrariums"
            className="px-4 py-2 bg-[#4a9e6a] hover:bg-[#3d8558] text-white text-xs font-semibold rounded-xl shadow-lg shadow-[#4a9e6a]/20 transition-all flex items-center gap-1.5"
          >
            <span>Xem tất cả Terrarium</span>
            <ChevronRight className="w-4 h-4" />
          </Link>
        </div>
      </div>

      {/* 4 Overview Stat Cards */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        {/* Stat 1 */}
        <div className="bg-[#112016] border border-[#1e3825] p-5 rounded-2xl relative overflow-hidden">
          <div className="flex items-center justify-between">
            <span className="text-xs font-semibold uppercase text-[#8e9e8f]">
              Terrarium hoạt động
            </span>
            <div className="p-2 rounded-xl bg-[#4a9e6a]/15 text-[#4a9e6a]">
              <Box className="w-5 h-5" />
            </div>
          </div>
          <div className="mt-4 flex items-baseline gap-2">
            <span className="text-3xl font-heading font-bold text-[#dcd5c4]">
              {terrariums.length}
            </span>
            <span className="text-xs text-[#8e9e8f]">/ 3 chuồng sinh thái</span>
          </div>
          <p className="text-[11px] text-[#4a9e6a] mt-2 flex items-center gap-1">
            <Activity className="w-3.5 h-3.5" />
            <span>100% đang được giám sát</span>
          </p>
        </div>

        {/* Stat 2 */}
        <div className="bg-[#112016] border border-[#1e3825] p-5 rounded-2xl relative overflow-hidden">
          <div className="flex items-center justify-between">
            <span className="text-xs font-semibold uppercase text-[#8e9e8f]">
              Thiết bị IoT Online
            </span>
            <div className="p-2 rounded-xl bg-[#c87f3a]/15 text-[#c87f3a]">
              <Cpu className="w-5 h-5" />
            </div>
          </div>
          <div className="mt-4 flex items-baseline gap-2">
            <span className="text-3xl font-heading font-bold text-[#dcd5c4]">
              {onlineDevices}
            </span>
            <span className="text-xs text-[#8e9e8f]">/ {devices.length} nodes</span>
          </div>
          <p className="text-[11px] text-[#e8a832] mt-2 flex items-center gap-1">
            <Clock className="w-3.5 h-3.5" />
            <span>1 thiết bị đang ngoại tuyến</span>
          </p>
        </div>

        {/* Stat 3 */}
        <div className="bg-[#112016] border border-[#1e3825] p-5 rounded-2xl relative overflow-hidden">
          <div className="flex items-center justify-between">
            <span className="text-xs font-semibold uppercase text-[#8e9e8f]">
              Cảnh báo hiện tại
            </span>
            <div className="p-2 rounded-xl bg-[#e05530]/15 text-[#e05530]">
              <AlertTriangle className="w-5 h-5" />
            </div>
          </div>
          <div className="mt-4 flex items-baseline gap-2">
            <span className="text-3xl font-heading font-bold text-[#e05530]">
              {alerts.length}
            </span>
            <span className="text-xs text-[#8e9e8f]">chưa xử lý</span>
          </div>
          <p className="text-[11px] text-[#e05530] mt-2 flex items-center gap-1">
            <span>2 mức nguy hiểm • 1 cảnh báo</span>
          </p>
        </div>

        {/* Stat 4 */}
        <div className="bg-[#112016] border border-[#1e3825] p-5 rounded-2xl relative overflow-hidden">
          <div className="flex items-center justify-between">
            <span className="text-xs font-semibold uppercase text-[#8e9e8f]">
              Trạng thái hệ thống
            </span>
            <div className="p-2 rounded-xl bg-[#4a9e6a]/15 text-[#4a9e6a]">
              <TrendingUp className="w-5 h-5" />
            </div>
          </div>
          <div className="mt-4 flex items-baseline gap-2">
            <span className="text-2xl font-heading font-bold text-[#4a9e6a]">
              Ổn định
            </span>
          </div>
          <p className="text-[11px] text-[#8e9e8f] mt-2 flex items-center gap-1">
            <span>Độ trễ MQTT: ~15ms</span>
          </p>
        </div>
      </div>

      {/* Main Terrarium Highlighted Sensor Card */}
      <div className="bg-[#112016] border border-[#1e3825] rounded-3xl p-6 sm:p-8 space-y-6">
        <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 pb-4 border-b border-[#1e3825]">
          <div className="flex items-center gap-3">
            <div className="w-3 h-8 bg-[#4a9e6a] rounded-full"></div>
            <div>
              <div className="flex items-center gap-3">
                <h2 className="text-xl font-heading font-bold text-[#dcd5c4]">
                  {currentTerrarium.name} – {currentTerrarium.species}
                </h2>
                {getStatusBadge(currentTerrarium.status)}
              </div>
              <p className="text-xs text-[#8e9e8f] mt-0.5">
                {currentTerrarium.description}
              </p>
            </div>
          </div>

          {/* Quick switcher */}
          <div className="flex items-center gap-2">
            <span className="text-xs text-[#8e9e8f]">Chọn chuồng:</span>
            <select
              value={selectedTerrariumId}
              onChange={(e) => setSelectedTerrariumId(e.target.value)}
              className="bg-[#0b1a0d] border border-[#1e3825] text-xs font-semibold text-[#dcd5c4] rounded-xl px-3 py-2 outline-none focus:border-[#4a9e6a]"
            >
              {terrariums.map((t) => (
                <option key={t.id} value={t.id}>
                  {t.name} ({t.species})
                </option>
              ))}
            </select>
          </div>
        </div>

        {/* 3 Sensor Live Gauges / Cards */}
        <div className="grid grid-cols-1 md:grid-cols-3 gap-5">
          {/* Temperature */}
          <div className="bg-[#0b1a0d] border border-[#1e3825] p-5 rounded-2xl relative overflow-hidden">
            <div className="flex items-center justify-between text-[#8e9e8f] mb-3">
              <span className="text-xs font-semibold uppercase flex items-center gap-2">
                <Thermometer className="w-4 h-4 text-[#e05530]" />
                Nhiệt độ hiện tại
              </span>
              <span className="text-[11px] text-[#8e9e8f]">
                Ngưỡng: {currentTerrarium.thresholds.tempMin}°C - {currentTerrarium.thresholds.tempMax}°C
              </span>
            </div>
            <div className="flex items-baseline gap-2">
              <span className="text-4xl font-mono font-extrabold text-[#dcd5c4]">
                {currentTerrarium.currentTemp}
              </span>
              <span className="text-xl font-mono text-[#8e9e8f]">°C</span>
            </div>
            {/* Progress bar */}
            <div className="mt-4">
              <div className="w-full h-2 bg-[#162a1d] rounded-full overflow-hidden">
                <div
                  className="h-full bg-gradient-to-r from-[#4a9e6a] via-[#e8a832] to-[#e05530] rounded-full"
                  style={{
                    width: `${Math.min(
                      100,
                      Math.max(
                        10,
                        ((currentTerrarium.currentTemp - 15) / (45 - 15)) * 100
                      )
                    )}%`,
                  }}
                ></div>
              </div>
              <div className="flex justify-between text-[10px] text-[#8e9e8f] mt-1.5 font-mono">
                <span>15°C</span>
                <span className="text-[#4a9e6a]">Vùng an toàn</span>
                <span>45°C</span>
              </div>
            </div>
          </div>

          {/* Humidity */}
          <div className="bg-[#0b1a0d] border border-[#1e3825] p-5 rounded-2xl relative overflow-hidden">
            <div className="flex items-center justify-between text-[#8e9e8f] mb-3">
              <span className="text-xs font-semibold uppercase flex items-center gap-2">
                <Droplets className="w-4 h-4 text-[#4a9e6a]" />
                Độ ẩm không khí
              </span>
              <span className="text-[11px] text-[#8e9e8f]">
                Ngưỡng: {currentTerrarium.thresholds.humidityMin}% - {currentTerrarium.thresholds.humidityMax}%
              </span>
            </div>
            <div className="flex items-baseline gap-2">
              <span className="text-4xl font-mono font-extrabold text-[#dcd5c4]">
                {currentTerrarium.currentHumidity}
              </span>
              <span className="text-xl font-mono text-[#8e9e8f]">%</span>
            </div>
            {/* Progress bar */}
            <div className="mt-4">
              <div className="w-full h-2 bg-[#162a1d] rounded-full overflow-hidden">
                <div
                  className="h-full bg-[#4a9e6a] rounded-full"
                  style={{ width: `${currentTerrarium.currentHumidity}%` }}
                ></div>
              </div>
              <div className="flex justify-between text-[10px] text-[#8e9e8f] mt-1.5 font-mono">
                <span>0%</span>
                <span className="text-[#4a9e6a]">Độ ẩm chuẩn</span>
                <span>100%</span>
              </div>
            </div>
          </div>

          {/* Light */}
          <div className="bg-[#0b1a0d] border border-[#1e3825] p-5 rounded-2xl relative overflow-hidden">
            <div className="flex items-center justify-between text-[#8e9e8f] mb-3">
              <span className="text-xs font-semibold uppercase flex items-center gap-2">
                <Sun className="w-4 h-4 text-[#c87f3a]" />
                Cường độ ánh sáng
              </span>
              <span className="text-[11px] text-[#8e9e8f]">
                Ngưỡng: {currentTerrarium.thresholds.lightMin} - {currentTerrarium.thresholds.lightMax} Lux
              </span>
            </div>
            <div className="flex items-baseline gap-2">
              <span className="text-4xl font-mono font-extrabold text-[#dcd5c4]">
                {currentTerrarium.currentLight}
              </span>
              <span className="text-xl font-mono text-[#8e9e8f]">Lux</span>
            </div>
            {/* Progress bar */}
            <div className="mt-4">
              <div className="w-full h-2 bg-[#162a1d] rounded-full overflow-hidden">
                <div
                  className="h-full bg-[#c87f3a] rounded-full"
                  style={{
                    width: `${Math.min(
                      100,
                      (currentTerrarium.currentLight / 1500) * 100
                    )}%`,
                  }}
                ></div>
              </div>
              <div className="flex justify-between text-[10px] text-[#8e9e8f] mt-1.5 font-mono">
                <span>0 Lux</span>
                <span className="text-[#c87f3a]">Mô phỏng tự nhiên</span>
                <span>1500 Lux</span>
              </div>
            </div>
          </div>
        </div>

        {/* Trend Chart with Filters */}
        <div className="pt-4">
          <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 mb-6">
            <div>
              <h3 className="text-lg font-heading font-bold text-[#dcd5c4]">
                Biểu đồ diễn biến môi trường
              </h3>
              <p className="text-xs text-[#8e9e8f]">
                Dữ liệu đo đạc liên tục qua giao thức MQTT từ các node cảm biến
              </p>
            </div>

            {/* Toggle series and time filters */}
            <div className="flex flex-wrap items-center gap-3">
              {/* Parameter Toggles */}
              <div className="flex items-center gap-1.5 p-1 rounded-xl bg-[#0b1a0d] border border-[#1e3825] text-xs">
                <button
                  onClick={() => setShowTemp(!showTemp)}
                  className={`px-2.5 py-1 rounded-lg font-semibold transition-all ${
                    showTemp
                      ? 'bg-[#e05530] text-white'
                      : 'text-[#8e9e8f] hover:text-[#dcd5c4]'
                  }`}
                >
                  Nhiệt độ (°C)
                </button>
                <button
                  onClick={() => setShowHumidity(!showHumidity)}
                  className={`px-2.5 py-1 rounded-lg font-semibold transition-all ${
                    showHumidity
                      ? 'bg-[#4a9e6a] text-white'
                      : 'text-[#8e9e8f] hover:text-[#dcd5c4]'
                  }`}
                >
                  Độ ẩm (%)
                </button>
                <button
                  onClick={() => setShowLight(!showLight)}
                  className={`px-2.5 py-1 rounded-lg font-semibold transition-all ${
                    showLight
                      ? 'bg-[#c87f3a] text-white'
                      : 'text-[#8e9e8f] hover:text-[#dcd5c4]'
                  }`}
                >
                  Ánh sáng (Lux)
                </button>
              </div>

              {/* Time Range */}
              <div className="flex items-center p-1 rounded-xl bg-[#0b1a0d] border border-[#1e3825] text-xs">
                {(['1h', '6h', '24h', '7d'] as const).map((r) => (
                  <button
                    key={r}
                    onClick={() => setTimeRange(r)}
                    className={`px-2.5 py-1 rounded-lg font-medium transition-all ${
                      timeRange === r
                        ? 'bg-[#162a1d] text-[#4a9e6a] font-bold'
                        : 'text-[#8e9e8f] hover:text-[#dcd5c4]'
                    }`}
                  >
                    {r}
                  </button>
                ))}
              </div>
            </div>
          </div>

          {/* Line Chart */}
          <div className="h-72 w-full">
            <ResponsiveContainer width="100%" height="100%">
              <LineChart data={chartData}>
                <CartesianGrid strokeDasharray="3 3" stroke="#1e3825" />
                <XAxis
                  dataKey="time"
                  stroke="#8e9e8f"
                  tick={{ fontSize: 11, fill: '#8e9e8f' }}
                />
                <YAxis
                  stroke="#8e9e8f"
                  tick={{ fontSize: 11, fill: '#8e9e8f' }}
                />
                <Tooltip
                  contentStyle={{
                    backgroundColor: '#112016',
                    borderColor: '#1e3825',
                    borderRadius: '12px',
                    color: '#dcd5c4',
                    fontSize: '12px',
                  }}
                />
                <Legend />
                {showTemp && (
                  <Line
                    type="monotone"
                    dataKey="temperature"
                    name="Nhiệt độ (°C)"
                    stroke="#e05530"
                    strokeWidth={2.5}
                    dot={false}
                  />
                )}
                {showHumidity && (
                  <Line
                    type="monotone"
                    dataKey="humidity"
                    name="Độ ẩm (%)"
                    stroke="#4a9e6a"
                    strokeWidth={2.5}
                    dot={false}
                  />
                )}
                {showLight && (
                  <Line
                    type="monotone"
                    dataKey="light"
                    name="Ánh sáng (Lux)"
                    stroke="#c87f3a"
                    strokeWidth={2}
                    dot={false}
                  />
                )}
              </LineChart>
            </ResponsiveContainer>
          </div>
        </div>
      </div>

      {/* Bottom Section: Quick Terrariums List + Recent Alerts */}
      <div className="grid grid-cols-1 lg:grid-cols-12 gap-8">
        {/* Quick Terrarium Cards (7 cols) */}
        <div className="lg:col-span-7 bg-[#112016] border border-[#1e3825] rounded-3xl p-6">
          <div className="flex items-center justify-between mb-5">
            <div>
              <h3 className="text-lg font-heading font-bold text-[#dcd5c4]">
                Tất cả Terrarium đang theo dõi
              </h3>
              <p className="text-xs text-[#8e9e8f]">
                Trực quan hóa trạng thái từng chuồng sinh thái
              </p>
            </div>
            <Link
              to="/terrariums"
              className="text-xs text-[#4a9e6a] hover:underline flex items-center gap-1 font-semibold"
            >
              <span>Xem chi tiết</span>
              <ArrowUpRight className="w-4 h-4" />
            </Link>
          </div>

          <div className="space-y-3">
            {terrariums.map((t) => (
              <Link
                key={t.id}
                to={`/terrariums/${t.id}`}
                className="flex items-center justify-between p-3.5 rounded-2xl bg-[#0b1a0d] border border-[#1e3825] hover:border-[#4a9e6a]/50 hover:bg-[#162a1d]/40 transition-all group"
              >
                <div className="flex items-center gap-3.5">
                  <img
                    src={t.image}
                    alt={t.species}
                    className="w-12 h-12 rounded-xl object-cover border border-[#1e3825]"
                  />
                  <div>
                    <h4 className="text-sm font-heading font-bold text-[#dcd5c4] group-hover:text-[#4a9e6a] transition-colors">
                      {t.name}
                    </h4>
                    <p className="text-xs text-[#8e9e8f]">{t.species}</p>
                  </div>
                </div>

                <div className="flex items-center gap-4 sm:gap-6">
                  <div className="text-right">
                    <span className="text-xs font-mono font-bold text-[#dcd5c4] block">
                      {t.currentTemp}°C • {t.currentHumidity}%
                    </span>
                    <span className="text-[10px] text-[#8e9e8f] block">
                      {t.currentLight} Lux
                    </span>
                  </div>
                  <div>{getStatusBadge(t.status)}</div>
                </div>
              </Link>
            ))}
          </div>
        </div>

        {/* Recent Alerts (5 cols) */}
        <div className="lg:col-span-5 bg-[#112016] border border-[#1e3825] rounded-3xl p-6">
          <div className="flex items-center justify-between mb-5">
            <div>
              <h3 className="text-lg font-heading font-bold text-[#dcd5c4]">
                Cảnh báo gần đây
              </h3>
              <p className="text-xs text-[#8e9e8f]">
                Phát hiện vi phạm ngưỡng tức thời
              </p>
            </div>
            <Link
              to="/alerts"
              className="text-xs text-[#4a9e6a] hover:underline flex items-center gap-1 font-semibold"
            >
              <span>Xem tất cả ({alerts.length})</span>
              <ArrowUpRight className="w-4 h-4" />
            </Link>
          </div>

          <div className="space-y-3">
            {alerts.slice(0, 3).map((a) => (
              <div
                key={a.id}
                className={`p-3.5 rounded-2xl border ${
                  a.severity === 'danger'
                    ? 'bg-[#e05530]/10 border-[#e05530]/30'
                    : 'bg-[#e8a832]/10 border-[#e8a832]/30'
                }`}
              >
                <div className="flex items-start gap-3">
                  <div
                    className={`p-2 rounded-xl shrink-0 ${
                      a.severity === 'danger'
                        ? 'bg-[#e05530]/20 text-[#e05530]'
                        : 'bg-[#e8a832]/20 text-[#e8a832]'
                    }`}
                  >
                    <AlertTriangle className="w-4 h-4" />
                  </div>
                  <div className="min-w-0 flex-1">
                    <div className="flex items-center justify-between gap-2">
                      <h5 className="text-xs font-bold text-[#dcd5c4] truncate">
                        {a.title}
                      </h5>
                      <span className="text-[10px] font-mono text-[#8e9e8f] shrink-0">
                        {a.timestamp}
                      </span>
                    </div>
                    <p className="text-[11px] text-[#8e9e8f] mt-1 line-clamp-2">
                      {a.message}
                    </p>
                    <p className="text-[10px] text-[#4a9e6a] mt-1 font-medium">
                      {a.terrariumName}
                    </p>
                  </div>
                </div>
              </div>
            ))}
          </div>
        </div>
      </div>
    </div>
  );
};

