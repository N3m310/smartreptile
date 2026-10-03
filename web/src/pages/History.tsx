import React, { useState } from 'react';
import {
  History as HistoryIcon,
  Download,
  Filter,
  Calendar,
  Thermometer,
  Droplets,
  Sun,
  FileSpreadsheet,
} from 'lucide-react';
import {
  initialTerrariums,
  generateHistoryData,
  HistoryDataPoint,
} from '../data/mockData';

export const History: React.FC = () => {
  const [selectedTerrariumId, setSelectedTerrariumId] = useState('T01');
  const [selectedSensor, setSelectedSensor] = useState<'all' | 'temp' | 'humidity' | 'light'>('all');
  const [selectedRange, setSelectedRange] = useState<'today' | '24h' | '7d'>('24h');

  const terrariums = initialTerrariums;
  const currentTerrarium =
    terrariums.find((t) => t.id === selectedTerrariumId) || terrariums[0];
  const historyList = generateHistoryData(selectedTerrariumId);

  // Xuất file CSV
  const handleExportCSV = () => {
    const headers = ['Thời gian', 'Nhiệt độ (°C)', 'Độ ẩm (%)', 'Ánh sáng (Lux)', 'Trạng thái'];
    const rows = historyList.map((item) => [
      item.time,
      item.temperature,
      item.humidity,
      item.light,
      item.status === 'normal' ? 'Bình thường' : item.status === 'warning' ? 'Cảnh báo' : 'Nguy hiểm',
    ]);

    const csvContent =
      'data:text/csv;charset=utf-8,\uFEFF' +
      [headers.join(','), ...rows.map((e) => e.join(','))].join('\n');

    const encodedUri = encodeURI(csvContent);
    const link = document.createElement('a');
    link.setAttribute('href', encodedUri);
    link.setAttribute('download', `terraguard_history_${selectedTerrariumId}.csv`);
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
  };

  const getStatusBadge = (status: HistoryDataPoint['status']) => {
    switch (status) {
      case 'normal':
        return (
          <span className="px-2.5 py-0.5 rounded-full text-[11px] font-semibold bg-[#4a9e6a]/20 text-[#4a9e6a]">
            Bình thường
          </span>
        );
      case 'warning':
        return (
          <span className="px-2.5 py-0.5 rounded-full text-[11px] font-semibold bg-[#e8a832]/20 text-[#e8a832]">
            Cảnh báo
          </span>
        );
      case 'danger':
        return (
          <span className="px-2.5 py-0.5 rounded-full text-[11px] font-semibold bg-[#e05530]/20 text-[#e05530]">
            Nguy hiểm
          </span>
        );
    }
  };

  return (
    <div className="space-y-8">
      {/* Header */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <div>
          <h1 className="text-2xl sm:text-3xl font-heading font-extrabold text-[#dcd5c4]">
            Lịch sử Dữ liệu Vi khí hậu
          </h1>
          <p className="text-sm text-[#8e9e8f] mt-1">
            Tra cứu log đo đạc chu kỳ cảm biến và xuất báo cáo lưu trữ
          </p>
        </div>

        <button
          onClick={handleExportCSV}
          className="px-4 py-2.5 bg-[#4a9e6a] hover:bg-[#3d8558] text-white text-xs font-semibold rounded-xl shadow-lg shadow-[#4a9e6a]/20 transition-all flex items-center gap-2 cursor-pointer self-start sm:self-auto"
        >
          <Download className="w-4 h-4" />
          <span>Xuất dữ liệu CSV</span>
        </button>
      </div>

      {/* Filter Toolbar */}
      <div className="p-5 rounded-3xl bg-[#112016] border border-[#1e3825] grid grid-cols-1 sm:grid-cols-3 gap-4">
        {/* Chọn Terrarium */}
        <div>
          <label className="block text-xs font-semibold uppercase text-[#8e9e8f] mb-2">
            Chọn Terrarium
          </label>
          <select
            value={selectedTerrariumId}
            onChange={(e) => setSelectedTerrariumId(e.target.value)}
            className="w-full bg-[#0b1a0d] border border-[#1e3825] text-xs font-medium text-[#dcd5c4] rounded-xl px-3.5 py-2.5 outline-none focus:border-[#4a9e6a]"
          >
            {terrariums.map((t) => (
              <option key={t.id} value={t.id}>
                {t.name} – {t.species}
              </option>
            ))}
          </select>
        </div>

        {/* Lọc loại cảm biến */}
        <div>
          <label className="block text-xs font-semibold uppercase text-[#8e9e8f] mb-2">
            Loại cảm biến hiển thị
          </label>
          <select
            value={selectedSensor}
            onChange={(e) => setSelectedSensor(e.target.value as any)}
            className="w-full bg-[#0b1a0d] border border-[#1e3825] text-xs font-medium text-[#dcd5c4] rounded-xl px-3.5 py-2.5 outline-none focus:border-[#4a9e6a]"
          >
            <option value="all">Tất cả thông số (Nhiệt / Ẩm / Sáng)</option>
            <option value="temp">Chỉ Nhiệt độ (°C)</option>
            <option value="humidity">Chỉ Độ ẩm (%)</option>
            <option value="light">Chỉ Cường độ Ánh sáng (Lux)</option>
          </select>
        </div>

        {/* Khoảng thời gian */}
        <div>
          <label className="block text-xs font-semibold uppercase text-[#8e9e8f] mb-2">
            Khoảng thời gian
          </label>
          <select
            value={selectedRange}
            onChange={(e) => setSelectedRange(e.target.value as any)}
            className="w-full bg-[#0b1a0d] border border-[#1e3825] text-xs font-medium text-[#dcd5c4] rounded-xl px-3.5 py-2.5 outline-none focus:border-[#4a9e6a]"
          >
            <option value="today">Hôm nay</option>
            <option value="24h">24 giờ qua</option>
            <option value="7d">7 ngày qua</option>
          </select>
        </div>
      </div>

      {/* History Data Table */}
      <div className="bg-[#112016] border border-[#1e3825] rounded-3xl overflow-hidden shadow-xl">
        <div className="p-5 border-b border-[#1e3825] flex items-center justify-between">
          <div className="flex items-center gap-2">
            <FileSpreadsheet className="w-5 h-5 text-[#4a9e6a]" />
            <h3 className="text-sm font-heading font-bold text-[#dcd5c4]">
              Nhật ký mẫu đo: {currentTerrarium.name} ({historyList.length} điểm ghi nhận)
            </h3>
          </div>
          <span className="text-xs text-[#8e9e8f] font-mono">
            Chu kỳ đo: 60 giây / mẫu
          </span>
        </div>

        <div className="overflow-x-auto">
          <table className="w-full text-left text-xs">
            <thead>
              <tr className="border-b border-[#1e3825] bg-[#0e1d11] text-[#8e9e8f] uppercase">
                <th className="py-3.5 px-6 font-semibold">Thời gian đo</th>
                {(selectedSensor === 'all' || selectedSensor === 'temp') && (
                  <th className="py-3.5 px-6 font-semibold">Nhiệt độ (°C)</th>
                )}
                {(selectedSensor === 'all' || selectedSensor === 'humidity') && (
                  <th className="py-3.5 px-6 font-semibold">Độ ẩm (%)</th>
                )}
                {(selectedSensor === 'all' || selectedSensor === 'light') && (
                  <th className="py-3.5 px-6 font-semibold">Ánh sáng (Lux)</th>
                )}
                <th className="py-3.5 px-6 font-semibold">Đánh giá ngưỡng</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-[#1e3825]">
              {historyList.map((row, index) => (
                <tr
                  key={index}
                  className="hover:bg-[#162a1d]/50 transition-colors"
                >
                  <td className="py-3.5 px-6 font-mono font-medium text-[#dcd5c4]">
                    {row.time}
                  </td>
                  {(selectedSensor === 'all' || selectedSensor === 'temp') && (
                    <td className="py-3.5 px-6 font-mono font-bold text-[#e05530]">
                      {row.temperature}°C
                    </td>
                  )}
                  {(selectedSensor === 'all' || selectedSensor === 'humidity') && (
                    <td className="py-3.5 px-6 font-mono font-bold text-[#4a9e6a]">
                      {row.humidity}%
                    </td>
                  )}
                  {(selectedSensor === 'all' || selectedSensor === 'light') && (
                    <td className="py-3.5 px-6 font-mono font-bold text-[#c87f3a]">
                      {row.light} Lux
                    </td>
                  )}
                  <td className="py-3.5 px-6">{getStatusBadge(row.status)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
};

