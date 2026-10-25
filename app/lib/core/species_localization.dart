/// Provides Vietnamese display names and descriptions for built-in system species profiles.
class SpeciesLocalization {
  const SpeciesLocalization._();

  static const Map<String, String> _displayNames = {
    'Arid (desert)': 'Rồng râu Úc (Arid desert)',
    'Arid-cool (bearded dragon, ambient)': 'Rồng râu Úc - Vùng mát (Ambient)',
    'Leopard gecko (semi-desert)': 'Thằn lằn da báo (Leopard gecko)',
    'Tropical (humid forest)': 'Tắc kè mào - Rừng nhiệt đới ẩm (Tropical)',
  };

  static const Map<String, String> _notes = {
    'Arid (desert)':
        'Loài bò sát sưởi nắng thực thụ: bắt buộc có điểm sưởi nóng (basking spot), nhiệt độ không khí đơn thuần không phản ánh đủ nhu cầu. Bắt buộc trang bị đèn tia UVB; nếu thiếu đèn UVB chỉ số UVI sẽ không đạt và hệ thống báo dưới ngưỡng.',
    'Arid-cool (bearded dragon, ambient)':
        'Biến thể vùng mát của hồ sơ Arid: cùng loài và cùng chuồng nuôi, nhưng cảm biến đặt ở khu vực mát (nhiệt độ môi trường 28-33°C) thay vì điểm sưởi basking. Chỉ áp dụng các dải nhiệt độ môi trường và độ ẩm, không đo nhiệt độ bề mặt hay tia UV tại đây.',
    'Leopard gecko (semi-desert)':
        'Loài bò sát cần dải chênh lệch nhiệt: nhiệt độ không khí gần vùng ấm 30-34°C, vùng mát khoảng 25°C. Ban đêm hạ 3-5°C có lợi cho sinh thái. Dải độ ẩm mô tả độ ẩm không khí chung; hang trú ẩm cần 70-80% RH do cảm biến riêng theo dõi.',
    'Tropical (humid forest)':
        'Dải thông số chuẩn cho loài tắc kè hoạt động lúc hoàng hôn ở rừng ẩm. Nhiệt độ cao > 30°C là nguyên nhân gây chết phổ biến trong nhà; độ ẩm cao liên tục, khô hạn sẽ gây khó khăn khi lột da. Tia UVB mức nhẹ là tùy chọn.',
  };

  /// Returns localized Vietnamese display name for built-in profiles,
  /// or original name if not in dictionary.
  static String displayName(String name) {
    return _displayNames[name] ?? name;
  }

  /// Returns localized Vietnamese notes for built-in profiles,
  /// or original notes if not in dictionary.
  static String displayNotes(String name, String? originalNotes) {
    final localized = _notes[name];
    if (localized != null && localized.isNotEmpty) {
      return localized;
    }
    return originalNotes ?? '';
  }

  /// Checks if search query matches either original English name, Vietnamese display name,
  /// scientific name, or notes.
  static bool matchesQuery({
    required String name,
    required String scientificName,
    String? notes,
    required String query,
  }) {
    if (query.trim().isEmpty) return true;
    final q = query.trim().toLowerCase();
    final vnName = displayName(name).toLowerCase();
    final origName = name.toLowerCase();
    final sci = scientificName.toLowerCase();
    final nts = displayNotes(name, notes).toLowerCase();
    final origNotes = (notes ?? '').toLowerCase();

    return origName.contains(q) ||
        vnName.contains(q) ||
        sci.contains(q) ||
        nts.contains(q) ||
        origNotes.contains(q);
  }
}

