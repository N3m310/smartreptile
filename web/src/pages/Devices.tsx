import React, { useState } from 'react';
import {
  Cpu,
  Search,
  Wifi,
  WifiOff,
  AlertTriangle,
  RefreshCw,
  SlidersHorizontal,
} from 'lucide-react';
import { initialDevices, Device } from '../data/mockData';

export const Devices: React.FC = () => {
  const [devices, setDevices] = useState<Device[]>(initialDevices);
  const [searchTerm, setSearchTerm] = useState('');
  const [statusFilter, setStatusFilter] = useState<'all' | 'online' | 'offline'>('all');

  const filteredDevices = devices.filter((d) => {
    const matchesSearch =
      d.name.toLowerCase().includes(searchTerm.toLowerCase()) ||
      d.terrariumName.toLowerCase().includes(searchTerm.toLowerCase()) ||
      d.type.toLowerCase().includes(searchTerm.toLowerCase()) ||
      d.id.toLowerCase().includes(searchTerm.toLowerCase());

    const matchesStatus =
      statusFilter === 'all' ? true : d.status === statusFilter;

    return matchesSearch && matchesStatus;
  });

  const offlineDevices = devices.filter((d) => d.status === 'offline');

  return (
    <div className="space-y-8">
      {/* Header */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <div>
          <h1 className="text-2xl sm:text-3xl font-heading font-extrabold text-[#dcd5c4]">
            Thiết bị & Cảm biến IoT
          </h1>
          <p className="text-sm text-[#8e9e8f] mt-1">
            Quản lý phần cứng ESP32, DHT22 và LDR thu thập dữ liệu thời gian thực
          </p>
        </div>
        <div className="flex items-center gap-2">
          <span className="px-3 py-1.5 rounded-xl bg-[#112016] border border-[#1e3825] text-xs font-mono text-[#dcd5c4]">
            Tổng: <strong className="text-[#4a9e6a]">{devices.length}</strong> thiết bị
          </span>
        </div>
      </div>

      {/* Offline Alert Banner */}
      {offlineDevices.length > 0 && (
        <div className="p-4 sm:p-5 rounded-3xl bg-[#e8a832]/10 border border-[#e8a832]/30 flex items-start sm:items-center justify-between gap-4">
          <div className="flex items-center gap-3">
            <div className="p-2.5 rounded-2xl bg-[#e8a832]/20 text-[#e8a832] shrink-0">
              <AlertTriangle className="w-5 h-5" />
            </div>
            <div>
              <h4 className="text-sm font-bold text-[#dcd5c4]">
                Phát hiện {offlineDevices.length} thiết bị đang ngoại tuyến (Offline)
              </h4>
              <p className="text-xs text-[#8e9e8f] mt-0.5">
                {offlineDevices.map((d) => `${d.name} (${d.terrariumName})`).join(', ')} – Vui lòng kiểm tra nguồn điện hoặc kết nối WiFi/MQTT.
              </p>
            </div>
          </div>
          <button
            onClick={() => setStatusFilter('offline')}
            className="px-3 py-1.5 bg-[#e8a832] hover:bg-[#d69827] text-black text-xs font-bold rounded-xl shrink-0 transition-colors cursor-pointer"
          >
            Lọc thiết bị lỗi
          </button>
        </div>
      )}

      {/* Search & Filter Bar */}
      <div className="flex flex-col sm:flex-row items-stretch sm:items-center justify-between gap-4 p-4 rounded-2xl bg-[#112016] border border-[#1e3825]">
        {/* Search Input */}
        <div className="relative flex-1">
          <div className="absolute inset-y-0 left-0 pl-3.5 flex items-center pointer-events-none text-[#8e9e8f]">
            <Search className="w-4 h-4" />
          </div>
          <input
            type="text"
            placeholder="Tìm theo tên thiết bị, mã ID, terrarium hoặc loại cảm biến..."
            value={searchTerm}
            onChange={(e) => setSearchTerm(e.target.value)}
            className="w-full pl-10 pr-4 py-2 bg-[#0b1a0d] border border-[#1e3825] rounded-xl text-xs text-[#dcd5c4] placeholder-[#556055] outline-none focus:border-[#4a9e6a]"
          />
        </div>

        {/* Filter buttons */}
        <div className="flex items-center gap-2">
          <span className="text-xs text-[#8e9e8f] hidden md:inline">Trạng thái:</span>
          <div className="flex items-center p-1 rounded-xl bg-[#0b1a0d] border border-[#1e3825] text-xs">
            <button
              onClick={() => setStatusFilter('all')}
              className={`px-3 py-1.5 rounded-lg font-medium transition-all ${
                statusFilter === 'all'
                  ? 'bg-[#162a1d] text-[#4a9e6a] font-bold'
                  : 'text-[#8e9e8f] hover:text-[#dcd5c4]'
              }`}
            >
              Tất cả ({devices.length})
            </button>
            <button
              onClick={() => setStatusFilter('online')}
              className={`px-3 py-1.5 rounded-lg font-medium transition-all ${
                statusFilter === 'online'
                  ? 'bg-[#4a9e6a] text-white font-bold'
                  : 'text-[#8e9e8f] hover:text-[#dcd5c4]'
              }`}
            >
              Online ({devices.filter((d) => d.status === 'online').length})
            </button>
            <button
              onClick={() => setStatusFilter('offline')}
              className={`px-3 py-1.5 rounded-lg font-medium transition-all ${
                statusFilter === 'offline'
                  ? 'bg-[#e05530] text-white font-bold'
                  : 'text-[#8e9e8f] hover:text-[#dcd5c4]'
              }`}
            >
              Offline ({offlineDevices.length})
            </button>
          </div>
        </div>
      </div>

      {/* Devices Table */}
      <div className="bg-[#112016] border border-[#1e3825] rounded-3xl overflow-hidden shadow-xl">
        <div className="overflow-x-auto">
          <table className="w-full text-left text-xs">
            <thead>
              <tr className="border-b border-[#1e3825] bg-[#0e1d11] text-[#8e9e8f] uppercase tracking-wider">
                <th className="py-4 px-6 font-semibold">Mã & Tên Thiết bị</th>
                <th className="py-4 px-6 font-semibold">Loại cảm biến / Node</th>
                <th className="py-4 px-6 font-semibold">Chuồng quản lý</th>
                <th className="py-4 px-6 font-semibold">Trạng thái</th>
                <th className="py-4 px-6 font-semibold">Firmware</th>
                <th className="py-4 px-6 font-semibold">IP / Giao tiếp</th>
                <th className="py-4 px-6 font-semibold">Lần cuối thấy</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-[#1e3825]">
              {filteredDevices.map((device) => (
                <tr
                  key={device.id}
                  className="hover:bg-[#162a1d]/50 transition-colors"
                >
                  <td className="py-4 px-6">
                    <div className="flex items-center gap-3">
                      <div className="p-2 rounded-xl bg-[#0b1a0d] border border-[#1e3825] text-[#4a9e6a]">
                        <Cpu className="w-4 h-4" />
                      </div>
                      <div>
                        <span className="font-bold text-[#dcd5c4] text-sm block">
                          {device.name}
                        </span>
                        <span className="text-[10px] font-mono text-[#8e9e8f]">
                          {device.id}
                        </span>
                      </div>
                    </div>
                  </td>

                  <td className="py-4 px-6 text-[#dcd5c4] font-medium">
                    {device.type}
                  </td>

                  <td className="py-4 px-6 text-[#8e9e8f]">
                    <span className="font-medium text-[#c87f3a]">
                      {device.terrariumName}
                    </span>
                  </td>

                  <td className="py-4 px-6">
                    {device.status === 'online' ? (
                      <span className="inline-flex items-center gap-1.5 px-3 py-1 rounded-full text-xs font-semibold bg-[#4a9e6a]/20 text-[#4a9e6a] border border-[#4a9e6a]/30">
                        <Wifi className="w-3.5 h-3.5" />
                        Trực tuyến
                      </span>
                    ) : (
                      <span className="inline-flex items-center gap-1.5 px-3 py-1 rounded-full text-xs font-semibold bg-[#e05530]/20 text-[#e05530] border border-[#e05530]/30 animate-pulse">
                        <WifiOff className="w-3.5 h-3.5" />
                        Ngoại tuyến
                      </span>
                    )}
                  </td>

                  <td className="py-4 px-6 font-mono text-[#dcd5c4]">
                    {device.firmware}
                  </td>

                  <td className="py-4 px-6 font-mono text-[#8e9e8f]">
                    {device.ipAddress}
                  </td>

                  <td className="py-4 px-6 text-[#8e9e8f]">
                    {device.lastSeen}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        {filteredDevices.length === 0 && (
          <div className="text-center py-12 text-[#8e9e8f] text-sm">
            Không tìm thấy thiết bị nào khớp với điều kiện tìm kiếm.
          </div>
        )}
      </div>
    </div>
  );
};

