import React, { useState } from 'react';
import { NavLink, Outlet, useNavigate } from 'react-router-dom';
import {
  LayoutDashboard,
  Box,
  Cpu,
  AlertTriangle,
  History,
  Settings,
  LogOut,
  Menu,
  X,
  Bell,
  ShieldCheck,
  User,
  ChevronRight,
} from 'lucide-react';
import { initialAlerts } from '../data/mockData';

export const Layout: React.FC = () => {
  const [sidebarOpen, setSidebarOpen] = useState(false);
  const navigate = useNavigate();
  const pendingAlertCount = initialAlerts.filter((a) => a.status === 'pending').length;

  const handleLogout = () => {
    navigate('/');
  };

  const navItems = [
    { name: 'Tổng quan', path: '/dashboard', icon: LayoutDashboard },
    { name: 'Terrarium', path: '/terrariums', icon: Box },
    { name: 'Thiết bị IoT', path: '/devices', icon: Cpu },
    {
      name: 'Cảnh báo',
      path: '/alerts',
      icon: AlertTriangle,
      badge: pendingAlertCount > 0 ? pendingAlertCount : null,
      badgeColor: 'bg-[#e05530]',
    },
    { name: 'Lịch sử dữ liệu', path: '/history', icon: History },
    { name: 'Cài đặt hệ thống', path: '/settings', icon: Settings },
  ];

  return (
    <div className="min-h-screen bg-[#0b1a0d] text-[#dcd5c4] flex flex-col md:flex-row">
      {/* Mobile Backdrop */}
      {sidebarOpen && (
        <div
          className="fixed inset-0 bg-black/60 z-40 md:hidden backdrop-blur-xs"
          onClick={() => setSidebarOpen(false)}
        />
      )}

      {/* Sidebar */}
      <aside
        className={`fixed md:sticky top-0 h-screen w-64 bg-[#112016] border-r border-[#1e3825] z-50 flex flex-col transition-transform duration-300 ease-in-out ${
          sidebarOpen ? 'translate-x-0' : '-translate-x-full md:translate-x-0'
        }`}
      >
        {/* Brand Logo */}
        <div className="h-18 px-6 border-b border-[#1e3825] flex items-center justify-between">
          <NavLink to="/dashboard" className="flex items-center gap-3">
            <div className="w-10 h-10 rounded-xl bg-gradient-to-br from-[#4a9e6a] to-[#204930] flex items-center justify-center text-white shadow-lg shadow-[#4a9e6a]/20">
              <ShieldCheck className="w-6 h-6 text-[#dcd5c4]" />
            </div>
            <div>
              <span className="text-xl font-heading font-extrabold tracking-wider text-[#dcd5c4] block">
                TERRA<span className="text-[#4a9e6a]">GUARD</span>
              </span>
              <span className="text-[10px] text-[#8e9e8f] tracking-widest uppercase block -mt-1">
                Terrarium Monitor
              </span>
            </div>
          </NavLink>
          <button
            onClick={() => setSidebarOpen(false)}
            className="md:hidden text-[#8e9e8f] hover:text-[#dcd5c4] p-1"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Navigation Links */}
        <nav className="flex-1 px-4 py-6 space-y-1.5 overflow-y-auto">
          {navItems.map((item) => {
            const Icon = item.icon;
            return (
              <NavLink
                key={item.path}
                to={item.path}
                onClick={() => setSidebarOpen(false)}
                className={({ isActive }) =>
                  `flex items-center justify-between px-3.5 py-3 rounded-xl text-sm font-medium transition-all ${
                    isActive
                      ? 'bg-[#4a9e6a] text-white shadow-md shadow-[#4a9e6a]/20 font-semibold'
                      : 'text-[#8e9e8f] hover:bg-[#162a1d] hover:text-[#dcd5c4]'
                  }`
                }
              >
                <div className="flex items-center gap-3">
                  <Icon className="w-5 h-5 shrink-0" />
                  <span>{item.name}</span>
                </div>
                {item.badge ? (
                  <span
                    className={`px-2 py-0.5 rounded-full text-xs font-mono font-bold text-white ${item.badgeColor}`}
                  >
                    {item.badge}
                  </span>
                ) : null}
              </NavLink>
            );
          })}
        </nav>

        {/* User Card & Logout */}
        <div className="p-4 border-t border-[#1e3825] bg-[#0e1d11]/50">
          <div className="flex items-center justify-between p-2 rounded-xl bg-[#162a1d]/60 mb-2">
            <div className="flex items-center gap-3 min-w-0">
              <div className="w-9 h-9 rounded-full bg-[#4a9e6a]/20 border border-[#4a9e6a]/40 flex items-center justify-center text-[#4a9e6a] shrink-0">
                <User className="w-5 h-5" />
              </div>
              <div className="min-w-0">
                <p className="text-xs font-semibold text-[#dcd5c4] truncate">Admin Quản Lý</p>
                <p className="text-[11px] text-[#8e9e8f] truncate">admin@terraguard.vn</p>
              </div>
            </div>
          </div>
          <button
            onClick={handleLogout}
            className="w-full flex items-center justify-center gap-2 px-3 py-2 text-xs font-medium text-[#e05530] hover:bg-[#e05530]/10 rounded-lg transition-colors border border-transparent hover:border-[#e05530]/20"
          >
            <LogOut className="w-4 h-4" />
            <span>Đăng xuất</span>
          </button>
        </div>
      </aside>

      {/* Main Content Area */}
      <div className="flex-1 flex flex-col min-w-0">
        {/* Header */}
        <header className="h-18 px-4 sm:px-8 border-b border-[#1e3825] bg-[#112016]/80 backdrop-blur-md sticky top-0 z-30 flex items-center justify-between">
          <div className="flex items-center gap-3">
            <button
              onClick={() => setSidebarOpen(true)}
              className="md:hidden p-2 rounded-lg bg-[#162a1d] text-[#dcd5c4] hover:bg-[#1e3825]"
            >
              <Menu className="w-5 h-5" />
            </button>
            <div className="flex items-center gap-2 text-xs text-[#8e9e8f]">
              <span className="hidden sm:inline">Trang chủ</span>
              <ChevronRight className="w-3.5 h-3.5 hidden sm:inline" />
              <span className="font-medium text-[#4a9e6a]">Bảng điều khiển Giám sát</span>
            </div>
          </div>

          {/* Quick status bar */}
          <div className="flex items-center gap-3 sm:gap-5">
            <div className="hidden sm:flex items-center gap-2 px-3 py-1.5 rounded-full bg-[#0b1a0d] border border-[#1e3825] text-xs">
              <span className="w-2 h-2 rounded-full bg-[#4a9e6a] animate-pulse"></span>
              <span className="text-[#8e9e8f]">Trạng thái:</span>
              <span className="font-semibold text-[#4a9e6a]">Trực tuyến (Live)</span>
            </div>

            <NavLink
              to="/alerts"
              className="relative p-2 rounded-xl bg-[#162a1d] border border-[#1e3825] text-[#dcd5c4] hover:bg-[#1e3825] transition-colors"
              title="Xem thông báo cảnh báo"
            >
              <Bell className="w-5 h-5" />
              {pendingAlertCount > 0 && (
                <span className="absolute -top-1 -right-1 w-5 h-5 rounded-full bg-[#e05530] text-white text-[10px] font-mono font-bold flex items-center justify-center animate-bounce">
                  {pendingAlertCount}
                </span>
              )}
            </NavLink>
          </div>
        </header>

        {/* Page body */}
        <main className="flex-1 p-4 sm:p-8 max-w-7xl w-full mx-auto">
          <Outlet />
        </main>
      </div>
    </div>
  );
};

