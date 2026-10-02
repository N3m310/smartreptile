import React, { useState } from 'react';
import {
  AlertTriangle,
  CheckCircle,
  Check,
  Clock,
  Filter,
  Thermometer,
  Droplets,
  WifiOff,
  Sun,
  ShieldAlert,
} from 'lucide-react';
import { initialAlerts, AlertItem } from '../data/mockData';

export const Alerts: React.FC = () => {
  const [alerts, setAlerts] = useState<AlertItem[]>(initialAlerts);
  const [statusFilter, setStatusFilter] = useState<'all' | 'pending' | 'resolved'>('all');

  const handleResolveAlert = (id: string) => {
    setAlerts((prev) =>
      prev.map((item) =>
        item.id === id
          ? {
              ...item,
              status: 'resolved',
              resolvedAt: 'Vừa xong bởi Quản trị viên',
            }
          : item
      )
    );
  };

  const filteredAlerts = alerts.filter((a) => {
    if (statusFilter === 'all') return true;
    return a.status === statusFilter;
  });

  const pendingCount = alerts.filter((a) => a.status === 'pending').length;

  const getAlertIcon = (type: AlertItem['type']) => {
    switch (type) {
      case 'temp_high':
      case 'temp_low':
        return <Thermometer className="w-5 h-5" />;
      case 'humidity_low':
      case 'humidity_high':
        return <Droplets className="w-5 h-5" />;
      case 'device_offline':
        return <WifiOff className="w-5 h-5" />;
      case 'light_abnormal':
        return <Sun className="w-5 h-5" />;
      default:
        return <AlertTriangle className="w-5 h-5" />;
    }
  };

  return (
    <div className="space-y-8">
      {/* Header */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <div>
          <h1 className="text-2xl sm:text-3xl font-heading font-extrabold text-[#dcd5c4]">
            Hệ thống Cảnh báo Ngưỡng
          </h1>
          <p className="text-sm text-[#8e9e8f] mt-1">
            Logic so sánh vi khí hậu trực tiếp (Không AI) – Phát hiện bất thường ngay khi giá trị vượt min/max
          </p>
        </div>

        <div className="flex items-center gap-2">
          {pendingCount > 0 ? (
            <span className="px-3.5 py-1.5 rounded-xl bg-[#e05530]/20 border border-[#e05530]/40 text-xs font-bold text-[#e05530] flex items-center gap-1.5 animate-pulse">
              <ShieldAlert className="w-4 h-4" />
              <span>{pendingCount} cảnh báo cần xử lý</span>
            </span>
          ) : (
            <span className="px-3.5 py-1.5 rounded-xl bg-[#4a9e6a]/20 border border-[#4a9e6a]/40 text-xs font-bold text-[#4a9e6a] flex items-center gap-1.5">
              <CheckCircle className="w-4 h-4" />
              <span>Tất cả đã an toàn</span>
            </span>
          )}
        </div>
      </div>

      {/* Filter Tabs */}
      <div className="flex items-center justify-between p-2 rounded-2xl bg-[#112016] border border-[#1e3825]">
        <div className="flex items-center gap-2">
          <button
            onClick={() => setStatusFilter('all')}
            className={`px-4 py-2 rounded-xl text-xs font-semibold transition-all cursor-pointer ${
              statusFilter === 'all'
                ? 'bg-[#4a9e6a] text-white shadow-md shadow-[#4a9e6a]/20'
                : 'text-[#8e9e8f] hover:text-[#dcd5c4]'
            }`}
          >
            Tất cả ({alerts.length})
          </button>
          <button
            onClick={() => setStatusFilter('pending')}
            className={`px-4 py-2 rounded-xl text-xs font-semibold transition-all cursor-pointer ${
              statusFilter === 'pending'
                ? 'bg-[#e05530] text-white shadow-md shadow-[#e05530]/20'
                : 'text-[#8e9e8f] hover:text-[#dcd5c4]'
            }`}
          >
            Chưa xử lý ({pendingCount})
          </button>
          <button
            onClick={() => setStatusFilter('resolved')}
            className={`px-4 py-2 rounded-xl text-xs font-semibold transition-all cursor-pointer ${
              statusFilter === 'resolved'
                ? 'bg-[#162a1d] text-[#4a9e6a] border border-[#4a9e6a]/40'
                : 'text-[#8e9e8f] hover:text-[#dcd5c4]'
            }`}
          >
            Đã xử lý ({alerts.length - pendingCount})
          </button>
        </div>

        <div className="text-xs text-[#8e9e8f] hidden sm:block pr-2">
          Quy tắc: <code className="text-[#4a9e6a] font-mono">if (value &gt; max || value &lt; min) trigger</code>
        </div>
      </div>

      {/* Alerts List */}
      <div className="space-y-4">
        {filteredAlerts.map((alert) => {
          const isDanger = alert.severity === 'danger';
          const isResolved = alert.status === 'resolved';

          return (
            <div
              key={alert.id}
              className={`p-5 rounded-3xl border transition-all duration-300 flex flex-col md:flex-row md:items-center justify-between gap-5 ${
                isResolved
                  ? 'bg-[#112016]/40 border-[#1e3825] opacity-75'
                  : isDanger
                  ? 'bg-[#112016] border-[#e05530]/40 shadow-lg shadow-[#e05530]/5 hover:border-[#e05530]'
                  : 'bg-[#112016] border-[#e8a832]/40 shadow-lg shadow-[#e8a832]/5 hover:border-[#e8a832]'
              }`}
            >
              {/* Alert Left Info */}
              <div className="flex items-start gap-4">
                <div
                  className={`p-3 rounded-2xl shrink-0 ${
                    isResolved
                      ? 'bg-[#1e3825] text-[#8e9e8f]'
                      : isDanger
                      ? 'bg-[#e05530]/20 text-[#e05530]'
                      : 'bg-[#e8a832]/20 text-[#e8a832]'
                  }`}
                >
                  {getAlertIcon(alert.type)}
                </div>

                <div className="space-y-1">
                  <div className="flex flex-wrap items-center gap-2.5">
                    <span className="font-mono text-xs font-bold text-[#8e9e8f]">
                      {alert.id}
                    </span>
                    <span
                      className={`px-2.5 py-0.5 rounded-full text-[10px] font-bold uppercase tracking-wider ${
                        isResolved
                          ? 'bg-[#1e3825] text-[#8e9e8f]'
                          : isDanger
                          ? 'bg-[#e05530] text-white'
                          : 'bg-[#e8a832] text-black'
                      }`}
                    >
                      {isResolved
                        ? 'Đã xử lý'
                        : isDanger
                        ? 'Nguy hiểm'
                        : 'Cảnh báo'}
                    </span>
                    <span className="text-xs font-semibold text-[#c87f3a]">
                      {alert.terrariumName}
                    </span>
                  </div>

                  <h3 className="text-base font-heading font-bold text-[#dcd5c4]">
                    {alert.title}
                  </h3>
                  <p className="text-xs text-[#8e9e8f] leading-relaxed max-w-2xl">
                    {alert.message}
                  </p>

                  <div className="flex items-center gap-3 text-[11px] text-[#8e9e8f] pt-1">
                    <span className="flex items-center gap-1">
                      <Clock className="w-3.5 h-3.5" />
                      {alert.timestamp}
                    </span>
                    {alert.resolvedAt && (
                      <span className="text-[#4a9e6a] flex items-center gap-1">
                        <Check className="w-3.5 h-3.5" />
                        {alert.resolvedAt}
                      </span>
                    )}
                  </div>
                </div>
              </div>

              {/* Action Button */}
              <div className="shrink-0 flex items-center gap-3 self-end md:self-center">
                {!isResolved ? (
                  <button
                    onClick={() => handleResolveAlert(alert.id)}
                    className="px-4 py-2.5 bg-[#4a9e6a] hover:bg-[#3d8558] text-white text-xs font-semibold rounded-xl shadow-lg shadow-[#4a9e6a]/20 transition-all flex items-center gap-1.5 cursor-pointer"
                  >
                    <Check className="w-4 h-4" />
                    <span>Đã kiểm tra & Xử lý</span>
                  </button>
                ) : (
                  <div className="flex items-center gap-1.5 text-xs text-[#4a9e6a] font-semibold px-3 py-1.5 rounded-xl bg-[#4a9e6a]/10 border border-[#4a9e6a]/20">
                    <CheckCircle className="w-4 h-4" />
                    <span>Hoàn tất</span>
                  </div>
                )}
              </div>
            </div>
          );
        })}

        {filteredAlerts.length === 0 && (
          <div className="text-center py-16 bg-[#112016] border border-[#1e3825] rounded-3xl text-[#8e9e8f]">
            <CheckCircle className="w-10 h-10 text-[#4a9e6a] mx-auto mb-2 opacity-60" />
            <p className="text-sm font-semibold text-[#dcd5c4]">
              Không có cảnh báo nào trong mục này
            </p>
            <p className="text-xs text-[#8e9e8f] mt-1">
              Hệ thống vi khí hậu đang hoạt động ổn định trong phạm vi ngưỡng cho phép.
            </p>
          </div>
        )}
      </div>
    </div>
  );
};

