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
  Mail,
  Phone,
} from 'lucide-react';
import { useSession } from '../state/session';

export const Settings: React.FC = () => {
  const navigate = useNavigate();
  const { signOut } = useSession();

  // Profile state
  const [profile, setProfile] = useState({
    name: 'Nguyễn Văn Quản Trị',
    email: 'admin@vivariumguard.vn',
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

  // Threshold editing lives on the terrarium's own page now, so this screen keeps no threshold state at all.

  const handleProfileSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    setProfileSaved(true);
    setTimeout(() => setProfileSaved(false), 2500);
  };

  return (
    <div className="space-y-8 max-w-5xl">
      {/* Header */}
      <div>
        <h1 className="text-2xl sm:text-3xl font-heading font-extrabold text-[#dcd5c4]">
          Cài đặt Hệ thống
        </h1>
        <p className="text-sm text-[#8e9e8f] mt-1">
          Quản lý tài khoản và cấu hình nhận thông báo cảnh báo
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

      {/* 3. Threshold Configuration - editing lives on the terrarium page (real API), not here. */}
      <div className="bg-[#112016] border border-[#1e3825] rounded-3xl p-6 sm:p-8 space-y-4">
        <div className="flex items-center gap-3 pb-4 border-b border-[#1e3825]">
          <div className="p-2.5 rounded-2xl bg-[#4a9e6a]/15 text-[#4a9e6a]">
            <Sliders className="w-5 h-5" />
          </div>
          <div>
            <h2 className="text-lg font-heading font-bold text-[#dcd5c4]">
              Ngưỡng vi khí hậu (Threshold)
            </h2>
            <p className="text-xs text-[#8e9e8f]">
              Cấu hình theo từng bể nuôi, trên dữ liệu thật của máy chủ
            </p>
          </div>
        </div>
        <p className="text-xs text-[#8e9e8f] leading-relaxed">
          Mở một bể nuôi để xem ngưỡng hiệu lực của từng chỉ số (kèm nguồn: hồ sơ loài hoặc ghi đè) và sửa
          trực tiếp tại đó. Form nhập tay trước đây trên màn hình này đã bỏ, vì nút "lưu" ở đó không gửi gì
          lên máy chủ.
        </p>
        <button
          type="button"
          onClick={() => navigate('/terrariums')}
          className="inline-flex items-center gap-2 px-5 py-2.5 bg-[#4a9e6a] hover:bg-[#3d8558] text-white text-xs font-semibold rounded-xl transition-all cursor-pointer"
        >
          <Sliders className="w-4 h-4" />
          <span>Mở danh sách bể nuôi</span>
        </button>
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
          onClick={async () => {
            await signOut();
            navigate('/');
          }}
          className="px-5 py-2.5 bg-[#e8613a]/15 hover:bg-[#e8613a] text-[#e8613a] hover:text-white border border-[#e8613a]/30 text-xs font-semibold rounded-xl transition-all flex items-center gap-2 cursor-pointer"
        >
          <LogOut className="w-4 h-4" />
          <span>Đăng xuất ngay</span>
        </button>
      </div>
    </div>
  );
};

