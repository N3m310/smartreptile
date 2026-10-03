import React, { useState } from 'react';
import { Link } from 'react-router-dom';
import {
  Plus,
  Thermometer,
  Droplets,
  Sun,
  Cpu,
  ChevronRight,
  X,
  Check,
} from 'lucide-react';
import { initialTerrariums, Terrarium } from '../data/mockData';

export const Terrariums: React.FC = () => {
  const [terrariums, setTerrariums] = useState<Terrarium[]>(initialTerrariums);
  const [showAddModal, setShowAddModal] = useState(false);
  const [newTerrarium, setNewTerrarium] = useState({
    name: '',
    species: '',
    description: '',
  });

  const handleAddSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!newTerrarium.name || !newTerrarium.species) return;

    const added: Terrarium = {
      id: `T0${terrariums.length + 1}`,
      name: newTerrarium.name,
      species: newTerrarium.species,
      image:
        'https://images.unsplash.com/photo-1548767797-d8c844163c4c?auto=format&fit=crop&w=800&q=80',
      status: 'normal',
      currentTemp: 28.5,
      currentHumidity: 50.0,
      currentLight: 400,
      thresholds: {
        tempMin: 24,
        tempMax: 32,
        humidityMin: 40,
        humidityMax: 60,
        lightMin: 200,
        lightMax: 800,
      },
      description: newTerrarium.description || 'Terrarium mới được tạo.',
      deviceCount: 3,
    };

    setTerrariums([...terrariums, added]);
    setShowAddModal(false);
    setNewTerrarium({ name: '', species: '', description: '' });
  };

  const getStatusBadge = (status: 'normal' | 'warning' | 'danger') => {
    switch (status) {
      case 'normal':
        return (
          <span className="px-3 py-1 rounded-full text-xs font-semibold bg-[#4a9e6a] text-white shadow-md shadow-[#4a9e6a]/20">
            Bình thường
          </span>
        );
      case 'warning':
        return (
          <span className="px-3 py-1 rounded-full text-xs font-semibold bg-[#e8a832] text-black shadow-md shadow-[#e8a832]/20">
            Cảnh báo
          </span>
        );
      case 'danger':
        return (
          <span className="px-3 py-1 rounded-full text-xs font-semibold bg-[#e05530] text-white shadow-md shadow-[#e05530]/20 animate-pulse">
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
            Danh sách Terrarium
          </h1>
          <p className="text-sm text-[#8e9e8f] mt-1">
            Quản lý và theo dõi môi trường vi khí hậu theo từng loài bò sát
          </p>
        </div>
        <button
          onClick={() => setShowAddModal(true)}
          className="px-4 py-2.5 bg-[#4a9e6a] hover:bg-[#3d8558] text-white text-xs font-semibold rounded-xl shadow-lg shadow-[#4a9e6a]/25 transition-all flex items-center gap-2 cursor-pointer"
        >
          <Plus className="w-4 h-4" />
          <span>+ Thêm Terrarium</span>
        </button>
      </div>

      {/* Terrarium Cards Grid */}
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
        {terrariums.map((item) => (
          <div
            key={item.id}
            className="bg-[#112016] border border-[#1e3825] hover:border-[#4a9e6a]/50 rounded-3xl overflow-hidden shadow-xl transition-all duration-300 flex flex-col group"
          >
            {/* Image & Status Badge */}
            <div className="relative h-48 w-full overflow-hidden">
              <img
                src={item.image}
                alt={item.species}
                className="w-full h-full object-cover group-hover:scale-105 transition-transform duration-500"
              />
              <div className="absolute inset-0 bg-gradient-to-t from-[#112016] via-transparent to-black/40"></div>
              <div className="absolute top-4 right-4">
                {getStatusBadge(item.status)}
              </div>
              <div className="absolute bottom-3 left-4 right-4">
                <span className="text-xs font-bold text-[#4a9e6a] tracking-wider uppercase">
                  {item.id}
                </span>
                <h3 className="text-xl font-heading font-bold text-[#dcd5c4] drop-shadow-md">
                  {item.name}
                </h3>
                <p className="text-xs text-[#8e9e8f] italic">{item.species}</p>
              </div>
            </div>

            {/* Body */}
            <div className="p-5 flex-1 flex flex-col justify-between space-y-5">
              <p className="text-xs text-[#8e9e8f] line-clamp-2">
                {item.description}
              </p>

              {/* 3 Mini Stats */}
              <div className="grid grid-cols-3 gap-2.5 p-3 rounded-2xl bg-[#0b1a0d] border border-[#1e3825]">
                {/* Temp */}
                <div className="text-center">
                  <span className="text-[10px] text-[#8e9e8f] flex items-center justify-center gap-1">
                    <Thermometer className="w-3 h-3 text-[#e05530]" />
                    Nhiệt độ
                  </span>
                  <p className="text-sm font-mono font-bold text-[#dcd5c4] mt-0.5">
                    {item.currentTemp}°C
                  </p>
                </div>

                {/* Humidity */}
                <div className="text-center border-x border-[#1e3825]">
                  <span className="text-[10px] text-[#8e9e8f] flex items-center justify-center gap-1">
                    <Droplets className="w-3 h-3 text-[#4a9e6a]" />
                    Độ ẩm
                  </span>
                  <p className="text-sm font-mono font-bold text-[#dcd5c4] mt-0.5">
                    {item.currentHumidity}%
                  </p>
                </div>

                {/* Light */}
                <div className="text-center">
                  <span className="text-[10px] text-[#8e9e8f] flex items-center justify-center gap-1">
                    <Sun className="w-3 h-3 text-[#c87f3a]" />
                    Ánh sáng
                  </span>
                  <p className="text-sm font-mono font-bold text-[#dcd5c4] mt-0.5">
                    {item.currentLight} Lx
                  </p>
                </div>
              </div>

              {/* Action Link */}
              <div className="flex items-center justify-between pt-2 border-t border-[#1e3825]/60 text-xs text-[#8e9e8f]">
                <span className="flex items-center gap-1.5">
                  <Cpu className="w-3.5 h-3.5 text-[#4a9e6a]" />
                  <span>{item.deviceCount} thiết bị IoT</span>
                </span>
                <Link
                  to={`/terrariums/${item.id}`}
                  className="font-semibold text-[#4a9e6a] hover:text-[#3d8558] flex items-center gap-1 group-hover:translate-x-1 transition-transform"
                >
                  <span>Vào chi tiết</span>
                  <ChevronRight className="w-4 h-4" />
                </Link>
              </div>
            </div>
          </div>
        ))}
      </div>

      {/* Add Modal */}
      {showAddModal && (
        <div className="fixed inset-0 z-50 bg-black/70 flex items-center justify-center p-4 backdrop-blur-xs">
          <div className="bg-[#112016] border border-[#1e3825] rounded-3xl w-full max-w-lg p-6 sm:p-8 space-y-6">
            <div className="flex items-center justify-between">
              <h3 className="text-xl font-heading font-bold text-[#dcd5c4]">
                Thêm Terrarium Mới
              </h3>
              <button
                onClick={() => setShowAddModal(false)}
                className="text-[#8e9e8f] hover:text-[#dcd5c4]"
              >
                <X className="w-5 h-5" />
              </button>
            </div>

            <form onSubmit={handleAddSubmit} className="space-y-4">
              <div>
                <label className="block text-xs font-semibold text-[#8e9e8f] mb-1.5 uppercase">
                  Tên chuồng Terrarium
                </label>
                <input
                  type="text"
                  required
                  placeholder="Ví dụ: Terrarium #04"
                  value={newTerrarium.name}
                  onChange={(e) =>
                    setNewTerrarium({ ...newTerrarium, name: e.target.value })
                  }
                  className="w-full px-4 py-2.5 bg-[#0b1a0d] border border-[#1e3825] rounded-xl text-sm text-[#dcd5c4] outline-none focus:border-[#4a9e6a]"
                />
              </div>

              <div>
                <label className="block text-xs font-semibold text-[#8e9e8f] mb-1.5 uppercase">
                  Tên loài bò sát
                </label>
                <input
                  type="text"
                  required
                  placeholder="Ví dụ: Trăn Cây Xanh (Green Tree Python)"
                  value={newTerrarium.species}
                  onChange={(e) =>
                    setNewTerrarium({ ...newTerrarium, species: e.target.value })
                  }
                  className="w-full px-4 py-2.5 bg-[#0b1a0d] border border-[#1e3825] rounded-xl text-sm text-[#dcd5c4] outline-none focus:border-[#4a9e6a]"
                />
              </div>

              <div>
                <label className="block text-xs font-semibold text-[#8e9e8f] mb-1.5 uppercase">
                  Mô tả đặc điểm sinh học
                </label>
                <textarea
                  rows={3}
                  placeholder="Yêu cầu vi khí hậu, độ ẩm, chu kỳ chiếu sáng..."
                  value={newTerrarium.description}
                  onChange={(e) =>
                    setNewTerrarium({ ...newTerrarium, description: e.target.value })
                  }
                  className="w-full px-4 py-2.5 bg-[#0b1a0d] border border-[#1e3825] rounded-xl text-sm text-[#dcd5c4] outline-none focus:border-[#4a9e6a]"
                ></textarea>
              </div>

              <div className="flex items-center justify-end gap-3 pt-3">
                <button
                  type="button"
                  onClick={() => setShowAddModal(false)}
                  className="px-4 py-2 rounded-xl text-xs font-semibold text-[#8e9e8f] hover:bg-[#162a1d]"
                >
                  Hủy bỏ
                </button>
                <button
                  type="submit"
                  className="px-5 py-2.5 rounded-xl text-xs font-semibold bg-[#4a9e6a] hover:bg-[#3d8558] text-white flex items-center gap-1.5 shadow-md shadow-[#4a9e6a]/20"
                >
                  <Check className="w-4 h-4" />
                  <span>Xác nhận thêm</span>
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};

