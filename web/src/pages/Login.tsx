import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { ShieldCheck, Eye, EyeOff, Lock, Mail, CheckCircle2, Box, Cpu, AlertTriangle, ArrowRight } from 'lucide-react';

export const Login: React.FC = () => {
  const [email, setEmail] = useState('admin@terraguard.vn');
  const [password, setPassword] = useState('123456');
  const [showPassword, setShowPassword] = useState(false);
  const [rememberMe, setRememberMe] = useState(true);
  const [isLoading, setIsLoading] = useState(false);
  const navigate = useNavigate();

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    setIsLoading(true);
    setTimeout(() => {
      setIsLoading(false);
      navigate('/dashboard');
    }, 400);
  };

  return (
    <div className="min-h-screen bg-[#0b1a0d] flex items-center justify-center p-4 sm:p-6 lg:p-8">
      <div className="w-full max-w-5xl bg-[#112016] border border-[#1e3825] rounded-3xl shadow-2xl overflow-hidden grid grid-cols-1 lg:grid-cols-12 min-h-[620px]">
        {/* Left Hero Panel */}
        <div className="lg:col-span-5 bg-gradient-to-br from-[#162a1d] via-[#112016] to-[#0e1d11] p-8 sm:p-10 flex flex-col justify-between border-b lg:border-b-0 lg:border-r border-[#1e3825]">
          <div>
            <div className="flex items-center gap-3 mb-6">
              <div className="w-12 h-12 rounded-2xl bg-gradient-to-br from-[#4a9e6a] to-[#204930] flex items-center justify-center text-white shadow-xl shadow-[#4a9e6a]/20">
                <ShieldCheck className="w-7 h-7 text-[#dcd5c4]" />
              </div>
              <div>
                <span className="text-2xl font-heading font-extrabold tracking-wider text-[#dcd5c4] block">
                  TERRA<span className="text-[#4a9e6a]">GUARD</span>
                </span>
                <span className="text-xs text-[#8e9e8f] tracking-widest uppercase block -mt-1">
                  Hệ sinh thái Bò sát Thông minh
                </span>
              </div>
            </div>

            <h2 className="text-xl sm:text-2xl font-heading font-bold text-[#dcd5c4] leading-snug mb-3">
              Giám sát vi khí hậu & cảnh báo ngưỡng chuẩn xác
            </h2>
            <p className="text-sm text-[#8e9e8f] leading-relaxed">
              Giải pháp IoT chuyên dụng theo dõi nhiệt độ, độ ẩm và ánh sáng cho chuồng nuôi bò sát, bảo vệ sức khỏe loài nuôi 24/7.
            </p>
          </div>

          {/* 3 Stat Cards */}
          <div className="space-y-3 my-8">
            <div className="flex items-center gap-4 p-3.5 rounded-2xl bg-[#0b1a0d]/60 border border-[#1e3825]">
              <div className="w-10 h-10 rounded-xl bg-[#4a9e6a]/15 text-[#4a9e6a] flex items-center justify-center shrink-0">
                <Box className="w-5 h-5" />
              </div>
              <div className="min-w-0">
                <p className="text-xs text-[#8e9e8f]">Terrarium Đang Quản Lý</p>
                <p className="text-base font-heading font-bold text-[#dcd5c4]">03 Chuồng sinh thái</p>
              </div>
            </div>

            <div className="flex items-center gap-4 p-3.5 rounded-2xl bg-[#0b1a0d]/60 border border-[#1e3825]">
              <div className="w-10 h-10 rounded-xl bg-[#c87f3a]/15 text-[#c87f3a] flex items-center justify-center shrink-0">
                <Cpu className="w-5 h-5" />
              </div>
              <div className="min-w-0">
                <p className="text-xs text-[#8e9e8f]">Cảm biến & Bộ điều khiển</p>
                <p className="text-base font-heading font-bold text-[#dcd5c4]">09 Node kết nối IoT</p>
              </div>
            </div>

            <div className="flex items-center gap-4 p-3.5 rounded-2xl bg-[#0b1a0d]/60 border border-[#1e3825]">
              <div className="w-10 h-10 rounded-xl bg-[#e05530]/15 text-[#e05530] flex items-center justify-center shrink-0">
                <AlertTriangle className="w-5 h-5" />
              </div>
              <div className="min-w-0">
                <p className="text-xs text-[#8e9e8f]">Hệ thống cảnh báo ngưỡng</p>
                <p className="text-base font-heading font-bold text-[#dcd5c4]">Thời gian thực & Phản hồi tức thì</p>
              </div>
            </div>
          </div>

          <div className="text-xs text-[#8e9e8f] flex items-center gap-2">
            <CheckCircle2 className="w-4 h-4 text-[#4a9e6a]" />
            <span>Phiên bản v1.0 • Không AI • Logic ngưỡng tin cậy</span>
          </div>
        </div>

        {/* Right Form Panel */}
        <div className="lg:col-span-7 p-8 sm:p-12 flex flex-col justify-center">
          <div className="max-w-md mx-auto w-full">
            <div className="mb-8">
              <h3 className="text-2xl font-heading font-bold text-[#dcd5c4]">Đăng nhập hệ thống</h3>
              <p className="text-sm text-[#8e9e8f] mt-1.5">
                Nhập thông tin xác thực để truy cập bảng điều khiển TerraGuard
              </p>
            </div>

            <form onSubmit={handleSubmit} className="space-y-5">
              {/* Email */}
              <div>
                <label className="block text-xs font-semibold uppercase tracking-wider text-[#8e9e8f] mb-2">
                  Địa chỉ Email
                </label>
                <div className="relative">
                  <div className="absolute inset-y-0 left-0 pl-3.5 flex items-center pointer-events-none text-[#8e9e8f]">
                    <Mail className="w-5 h-5" />
                  </div>
                  <input
                    type="email"
                    value={email}
                    onChange={(e) => setEmail(e.target.value)}
                    required
                    placeholder="admin@terraguard.vn"
                    className="w-full pl-11 pr-4 py-3 bg-[#0b1a0d] border border-[#1e3825] focus:border-[#4a9e6a] rounded-xl text-sm text-[#dcd5c4] placeholder-[#556055] outline-none transition-all focus:ring-1 focus:ring-[#4a9e6a]"
                  />
                </div>
              </div>

              {/* Password */}
              <div>
                <label className="block text-xs font-semibold uppercase tracking-wider text-[#8e9e8f] mb-2">
                  Mật khẩu
                </label>
                <div className="relative">
                  <div className="absolute inset-y-0 left-0 pl-3.5 flex items-center pointer-events-none text-[#8e9e8f]">
                    <Lock className="w-5 h-5" />
                  </div>
                  <input
                    type={showPassword ? 'text' : 'password'}
                    value={password}
                    onChange={(e) => setPassword(e.target.value)}
                    required
                    placeholder="••••••••"
                    className="w-full pl-11 pr-11 py-3 bg-[#0b1a0d] border border-[#1e3825] focus:border-[#4a9e6a] rounded-xl text-sm text-[#dcd5c4] placeholder-[#556055] outline-none transition-all focus:ring-1 focus:ring-[#4a9e6a]"
                  />
                  <button
                    type="button"
                    onClick={() => setShowPassword(!showPassword)}
                    className="absolute inset-y-0 right-0 pr-3.5 flex items-center text-[#8e9e8f] hover:text-[#dcd5c4]"
                  >
                    {showPassword ? <EyeOff className="w-5 h-5" /> : <Eye className="w-5 h-5" />}
                  </button>
                </div>
              </div>

              {/* Remember me & Forgot pass */}
              <div className="flex items-center justify-between text-xs pt-1">
                <label className="flex items-center gap-2 cursor-pointer select-none text-[#8e9e8f] hover:text-[#dcd5c4]">
                  <input
                    type="checkbox"
                    checked={rememberMe}
                    onChange={(e) => setRememberMe(e.target.checked)}
                    className="w-4 h-4 rounded bg-[#0b1a0d] border-[#1e3825] accent-[#4a9e6a] focus:ring-0"
                  />
                  <span>Ghi nhớ đăng nhập</span>
                </label>
                <a
                  href="#forgot"
                  onClick={(e) => {
                    e.preventDefault();
                    alert('Vui lòng liên hệ quản trị viên để khôi phục mật khẩu tài khoản.');
                  }}
                  className="text-[#c87f3a] hover:underline"
                >
                  Quên mật khẩu?
                </a>
              </div>

              {/* Submit button */}
              <button
                type="submit"
                disabled={isLoading}
                className="w-full mt-2 py-3.5 px-4 bg-[#4a9e6a] hover:bg-[#3d8558] text-white font-medium text-sm rounded-xl shadow-lg shadow-[#4a9e6a]/25 transition-all flex items-center justify-center gap-2 group cursor-pointer disabled:opacity-70"
              >
                <span>{isLoading ? 'Đang xác thực...' : 'Đăng nhập vào hệ thống'}</span>
                <ArrowRight className="w-4 h-4 transition-transform group-hover:translate-x-1" />
              </button>
            </form>

            <div className="mt-8 pt-6 border-t border-[#1e3825]/80 text-center text-xs text-[#8e9e8f]">
              <span>Tài khoản mẫu: </span>
              <span className="font-mono text-[#dcd5c4]">admin@terraguard.vn</span>
              <span className="mx-2">•</span>
              <span>Pass: </span>
              <span className="font-mono text-[#dcd5c4]">123456</span>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
};

