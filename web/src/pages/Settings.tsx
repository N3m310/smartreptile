import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  User,
  Bell,
  Sliders,
  LogOut,
  Save,
  Check,
  ShieldCheck,
  Thermometer,
  Droplets,
  Sun,
  Mail,
  Phone,
} from 'lucide-react';
import { initialTerrariums, Terrarium } from '../data/mockData';

export const Settings: React.FC = () => {
  const navigate = useNavigate();

  // Profile state
  const [profile, setProfile] = useState({
    name: 'Nguyễn Văn Quản Trị',
    email: 'admin@terraguard.vn',
    phone: '0987 654 321',
  });
  const [profileSaved, setProfileSaved] = useState(false);

  // Notification toggles
  const [notifications, setNotifications] = useState({
    tempAlert: true,
    humidityAlert: true,
    deviceOffline: true,
    dailyReport: false,
  });

  // Threshold config state
  const [terrariums, setTerrariums] = useState<Terrarium[]>(initialTerrariums);
  const [selectedTerrariumId, setSelectedTerrariumId] = useState('T01');
  const [thresholdSaved, setThresholdSaved] = useState(false);

  const currentTerrarium =
    terrariums.find((t) => t.id === selectedTerrariumId) || terrariums[0];

  const [thresholdForm, setThresholdForm] = useState(currentTerrarium.thresholds);

  // Khi đổi dropdown terrarium thì update form
  const handleSelectTerrarium = (id: string) => {
    setSelectedTerrariumId(id);
    const target = terrariums.find((t) => t.id === id);
    if (target) {
      setThresholdForm(target.thresholds);
    }
  };

  const handleProfileSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    setProfileSaved(true);
    setTimeout(() => setProfileSaved(false), 2500);
  };

  const handleThresholdSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    setTerrariums((prev) =>
      prev.map((t) =>
        t.id === selectedTerrariumId
          ? { ...t, thresholds: { ...thresholdForm } }
          : t
      )
    );
    setThresholdSaved(true);
    setTimeout(() => setThresholdSaved(false), 2500);
  };

  return (
    <div className="space-y-8 max-w-5xl">
      {/* Header */}
      <div>
        <h1 className="text-2xl sm:text-3xl font-heading font-extrabold text-[#dcd5c4]">
          Cài đặt Hệ thống
        </h1>
        <p className="text-sm text-[#8e9e8f] mt-1">
          Quản lý tài khoản, cấu hình nhận thông báo và tinh chỉnh khoảng ngưỡng vi khí hậu
        </p>
      </div>

      {/* 1. Account Profile Card */}
      <div className="bg-[#112016] border border-[#1e3825] rounded-3xl p-6 sm:p-8 space-y-6">
        <div className="flex items-center gap-3 pb-4 border-b border-[#1e3825]">
          <div className="p-2.5 rounded-2xl bg-[#4a9e6a]/15 text-[#4a9e6a]">
            <User className="w-5 h-5" />
          </div>
          <div>
            <h2 className="text-lg font-heading font-bold text-[#dcd5c4]">
              Thông tin Người quản lý
            </h2>
            <p className="text-xs text-[#8e9e8f]">
              Cập nhật hồ sơ cá nhân và phương thức liên hệ nhận thông báo
            </p>
          </div>
        </div>

        <form onSubmit={handleProfileSubmit} className="space-y-4">
          <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
            <div>
              <label className="block text-xs font-semibold uppercase text-[#8e9e8f] mb-1.5">
                Họ và tên
              </label>
              <input
                type="text"
                value={profile.name}
                onChange={(e) => setProfile({ ...profile, name: e.target.value })}
                className="w-full px-4 py-2.5 bg-[#0b1a0d] border border-[#1e3825] rounded-xl text-xs text-[#dcd5c4] outline-none focus:border-[#4a9e6a]"
              />
            </div>
            <div>
              <label className="block text-xs font-semibold uppercase text-[#8e9e8f] mb-1.5">
                Email nhận tin
              </label>
              <input
                type="email"
                value={profile.email}
                onChange={(e) => setProfile({ ...profile, email: e.target.value })}
                className="w-full px-4 py-2.5 bg-[#0b1a0d] border border-[#1e3825] rounded-xl text-xs text-[#dcd5c4] outline-none focus:border-[#4a9e6a]"
              />
            </div>
            <div>
              <label className="block text-xs font-semibold uppercase text-[#8e9e8f] mb-1.5">
                Số điện thoại (SMS / Zalo)
              </label>
              <input
                type="text"
                value={profile.phone}
                onChange={(e) => setProfile({ ...profile, phone: e.target.value })}
                className="w-full px-4 py-2.5 bg-[#0b1a0d] border border-[#1e3825] rounded-xl text-xs text-[#dcd5c4] outline-none focus:border-[#4a9e6a]"
              />
            </div>
          </div>

          <div className="flex items-center justify-end pt-2">
            <button
              type="submit"
              className="px-5 py-2.5 bg-[#4a9e6a] hover:bg-[#3d8558] text-white text-xs font-semibold rounded-xl shadow-md shadow-[#4a9e6a]/20 flex items-center gap-2 cursor-pointer"
            >
              {profileSaved ? (
                <>
                  <Check className="w-4 h-4" />
                  <span>Đã lưu thông tin!</span>
                </>
              ) : (
                <>
                  <Save className="w-4 h-4" />
                  <span>Lưu thay đổi hồ sơ</span>
                </>
              )}
            </button>
          </div>
        </form>
      </div>

      {/* 2. Notification Preferences */}
      <div className="bg-[#112016] border border-[#1e3825] rounded-3xl p-6 sm:p-8 space-y-6">
        <div className="flex items-center gap-3 pb-4 border-b border-[#1e3825]">
          <div className="p-2.5 rounded-2xl bg-[#c87f3a]/15 text-[#c87f3a]">
            <Bell className="w-5 h-5" />
          </div>
          <div>
            <h2 className="text-lg font-heading font-bold text-[#dcd5c4]">
              Kênh & Loại Cảnh báo
            </h2>
            <p className="text-xs text-[#8e9e8f]">
              Chọn các trường hợp gửi thông báo tức thì đến người quản lý
            </p>
          </div>
        </div>

        <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
          <label className="flex items-center justify-between p-4 rounded-2xl bg-[#0b1a0d] border border-[#1e3825] cursor-pointer hover:border-[#4a9e6a]/40">
            <div>
              <p className="text-sm font-semibold text-[#dcd5c4]">
                Cảnh báo nhiệt độ vượt ngưỡng
              </p>
              <p className="text-xs text-[#8e9e8f] mt-0.5">
                Gửi ngay khi phát hiện nhiệt vượt quá Min/Max
              </p>
            </div>
            <input
              type="checkbox"
              checked={notifications.tempAlert}
              onChange={(e) =>
                setNotifications({ ...notifications, tempAlert: e.target.checked })
              }
              className="w-5 h-5 accent-[#4a9e6a]"
            />
          </label>

          <label className="flex items-center justify-between p-4 rounded-2xl bg-[#0b1a0d] border border-[#1e3825] cursor-pointer hover:border-[#4a9e6a]/40">
            <div>
              <p className="text-sm font-semibold text-[#dcd5c4]">
                Cảnh báo độ ẩm vi phạm
              </p>
              <p className="text-xs text-[#8e9e8f] mt-0.5">
                Cảnh báo khi độ ẩm quá thấp hoặc quá cao
              </p>
            </div>
            <input
              type="checkbox"
              checked={notifications.humidityAlert}
              onChange={(e) =>
                setNotifications({
                  ...notifications,
                  humidityAlert: e.target.checked,
                })
              }
              className="w-5 h-5 accent-[#4a9e6a]"
            />
          </label>

          <label className="flex items-center justify-between p-4 rounded-2xl bg-[#0b1a0d] border border-[#1e3825] cursor-pointer hover:border-[#4a9e6a]/40">
            <div>
              <p className="text-sm font-semibold text-[#dcd5c4]">
                Thiết bị mất kết nối (Offline)
              </p>
              <p className="text-xs text-[#8e9e8f] mt-0.5">
                Thông báo khi node ESP32 không gửi tin sau 3 phút
              </p>
            </div>
            <input
              type="checkbox"
              checked={notifications.deviceOffline}
              onChange={(e) =>
                setNotifications({
                  ...notifications,
                  deviceOffline: e.target.checked,
                })
              }
              className="w-5 h-5 accent-[#4a9e6a]"
            />
          </label>

          <label className="flex items-center justify-between p-4 rounded-2xl bg-[#0b1a0d] border border-[#1e3825] cursor-pointer hover:border-[#4a9e6a]/40">
            <div>
              <p className="text-sm font-semibold text-[#dcd5c4]">
                Báo cáo tổng kết mỗi ngày
              </p>
              <p className="text-xs text-[#8e9e8f] mt-0.5">
                Gửi email bản tóm tắt trung bình chỉ số vào 21:00
              </p>
            </div>
            <input
              type="checkbox"
              checked={notifications.dailyReport}
              onChange={(e) =>
                setNotifications({
                  ...notifications,
                  dailyReport: e.target.checked,
                })
              }
              className="w-5 h-5 accent-[#4a9e6a]"
            />
          </label>
        </div>
      </div>

      {/* 3. Threshold Configuration */}
      <div className="bg-[#112016] border border-[#1e3825] rounded-3xl p-6 sm:p-8 space-y-6">
        <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 pb-4 border-b border-[#1e3825]">
          <div className="flex items-center gap-3">
            <div className="p-2.5 rounded-2xl bg-[#4a9e6a]/15 text-[#4a9e6a]">
              <Sliders className="w-5 h-5" />
            </div>
            <div>
              <h2 className="text-lg font-heading font-bold text-[#dcd5c4]">
                Cấu hình Ngưỡng Vi khí hậu (Threshold Settings)
              </h2>
              <p className="text-xs text-[#8e9e8f]">
                Tùy biến khoảng an toàn Min/Max độc lập cho từng chuồng bò sát
              </p>
            </div>
          </div>

          <div>
            <select
              value={selectedTerrariumId}
              onChange={(e) => handleSelectTerrarium(e.target.value)}
              className="bg-[#0b1a0d] border border-[#1e3825] text-xs font-bold text-[#4a9e6a] rounded-xl px-4 py-2 outline-none focus:border-[#4a9e6a]"
            >
              {terrariums.map((t) => (
                <option key={t.id} value={t.id}>
                  {t.name} – {t.species}
                </option>
              ))}
            </select>
          </div>
        </div>

        <form onSubmit={handleThresholdSubmit} className="space-y-6">
          <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
            {/* Temp config */}
            <div className="p-5 rounded-2xl bg-[#0b1a0d] border border-[#1e3825] space-y-4">
              <div className="flex items-center gap-2 text-xs font-bold text-[#e05530] uppercase">
                <Thermometer className="w-4 h-4" />
                <span>Nhiệt độ (°C)</span>
              </div>
              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="block text-[11px] text-[#8e9e8f] mb-1">
                    Ngưỡng Min (°C)
                  </label>
                  <input
                    type="number"
                    step="0.5"
                    value={thresholdForm.tempMin}
                    onChange={(e) =>
                      setThresholdForm({
                        ...thresholdForm,
                        tempMin: Number(e.target.value),
                      })
                    }
                    className="w-full px-3 py-2 bg-[#112016] border border-[#1e3825] rounded-xl text-xs font-mono font-bold text-[#dcd5c4] outline-none focus:border-[#4a9e6a]"
                  />
                </div>
                <div>
                  <label className="block text-[11px] text-[#8e9e8f] mb-1">
                    Ngưỡng Max (°C)
                  </label>
                  <input
                    type="number"
                    step="0.5"
                    value={thresholdForm.tempMax}
                    onChange={(e) =>
                      setThresholdForm({
                        ...thresholdForm,
                        tempMax: Number(e.target.value),
                      })
                    }
                    className="w-full px-3 py-2 bg-[#112016] border border-[#1e3825] rounded-xl text-xs font-mono font-bold text-[#dcd5c4] outline-none focus:border-[#4a9e6a]"
                  />
                </div>
              </div>
            </div>

            {/* Humidity config */}
            <div className="p-5 rounded-2xl bg-[#0b1a0d] border border-[#1e3825] space-y-4">
              <div className="flex items-center gap-2 text-xs font-bold text-[#4a9e6a] uppercase">
                <Droplets className="w-4 h-4" />
                <span>Độ ẩm (%)</span>
              </div>
              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="block text-[11px] text-[#8e9e8f] mb-1">
                    Ngưỡng Min (%)
                  </label>
                  <input
                    type="number"
                    value={thresholdForm.humidityMin}
                    onChange={(e) =>
                      setThresholdForm({
                        ...thresholdForm,
                        humidityMin: Number(e.target.value),
                      })
                    }
                    className="w-full px-3 py-2 bg-[#112016] border border-[#1e3825] rounded-xl text-xs font-mono font-bold text-[#dcd5c4] outline-none focus:border-[#4a9e6a]"
                  />
                </div>
                <div>
                  <label className="block text-[11px] text-[#8e9e8f] mb-1">
                    Ngưỡng Max (%)
                  </label>
                  <input
                    type="number"
                    value={thresholdForm.humidityMax}
                    onChange={(e) =>
                      setThresholdForm({
                        ...thresholdForm,
                        humidityMax: Number(e.target.value),
                      })
                    }
                    className="w-full px-3 py-2 bg-[#112016] border border-[#1e3825] rounded-xl text-xs font-mono font-bold text-[#dcd5c4] outline-none focus:border-[#4a9e6a]"
                  />
                </div>
              </div>
            </div>

            {/* Light config */}
            <div className="p-5 rounded-2xl bg-[#0b1a0d] border border-[#1e3825] space-y-4">
              <div className="flex items-center gap-2 text-xs font-bold text-[#c87f3a] uppercase">
                <Sun className="w-4 h-4" />
                <span>Ánh sáng (Lux)</span>
              </div>
              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="block text-[11px] text-[#8e9e8f] mb-1">
                    Ngưỡng Min (Lx)
                  </label>
                  <input
                    type="number"
                    value={thresholdForm.lightMin}
                    onChange={(e) =>
                      setThresholdForm({
                        ...thresholdForm,
                        lightMin: Number(e.target.value),
                      })
                    }
                    className="w-full px-3 py-2 bg-[#112016] border border-[#1e3825] rounded-xl text-xs font-mono font-bold text-[#dcd5c4] outline-none focus:border-[#4a9e6a]"
                  />
                </div>
                <div>
                  <label className="block text-[11px] text-[#8e9e8f] mb-1">
                    Ngưỡng Max (Lx)
                  </label>
                  <input
                    type="number"
                    value={thresholdForm.lightMax}
                    onChange={(e) =>
                      setThresholdForm({
                        ...thresholdForm,
                        lightMax: Number(e.target.value),
                      })
                    }
                    className="w-full px-3 py-2 bg-[#112016] border border-[#1e3825] rounded-xl text-xs font-mono font-bold text-[#dcd5c4] outline-none focus:border-[#4a9e6a]"
                  />
                </div>
              </div>
            </div>
          </div>

          <div className="flex items-center justify-between pt-2">
            <span className="text-xs text-[#8e9e8f]">
              Quy tắc so sánh: Khi vượt ngưỡng sẽ tự động kích hoạt cảnh báo tương ứng.
            </span>
            <button
              type="submit"
              className="px-6 py-2.5 bg-[#4a9e6a] hover:bg-[#3d8558] text-white text-xs font-semibold rounded-xl shadow-md shadow-[#4a9e6a]/20 flex items-center gap-2 cursor-pointer"
            >
              {thresholdSaved ? (
                <>
                  <Check className="w-4 h-4" />
                  <span>Đã lưu ngưỡng thành công!</span>
                </>
              ) : (
                <>
                  <Save className="w-4 h-4" />
                  <span>Lưu cấu hình ngưỡng</span>
                </>
              )}
            </button>
          </div>
        </form>
      </div>

      {/* 4. Logout Session */}
      <div className="bg-[#112016] border border-[#1e3825] rounded-3xl p-6 sm:p-8 flex items-center justify-between gap-4">
        <div>
          <h3 className="text-base font-heading font-bold text-[#dcd5c4]">
            Đăng xuất khỏi phiên làm việc
          </h3>
          <p className="text-xs text-[#8e9e8f] mt-0.5">
            Xóa token xác thực và chuyển hướng quay lại màn hình đăng nhập
          </p>
        </div>
        <button
          onClick={() => navigate('/')}
          className="px-5 py-2.5 bg-[#e05530]/15 hover:bg-[#e05530] text-[#e05530] hover:text-white border border-[#e05530]/30 text-xs font-semibold rounded-xl transition-all flex items-center gap-2 cursor-pointer"
        >
          <LogOut className="w-4 h-4" />
          <span>Đăng xuất ngay</span>
        </button>
      </div>
    </div>
  );
};

