// Bilingual strings for the dashboard.
//
// The key set mirrors app/lib/l10n/app_*.arb so a translation cannot drift silently between the two clients
// (checked by TC-I-15). Vietnamese is the default because the users are Vietnamese; the code and comments stay
// English (ADR-013).

const strings = {
  vi: {
    appTitle: 'SmartReptile',
    live: 'Trực tiếp',
    wallboard: 'Bảng hiển thị',
    statusInRange: 'Trong ngưỡng',
    statusOutOfRange: 'Ngoài ngưỡng',
    statusCritical: 'Nguy hiểm',
    statusNoData: 'Không có dữ liệu',
    statusUnavailable: 'Cảm biến lỗi',
    statusMaintenance: 'Đang bảo trì',
    deviceOnline: 'trực tuyến',
    deviceOffline: 'ngoại tuyến',
    livePaused: 'Đã tạm dừng cập nhật trực tiếp',
    emptyStateTitle: 'Chưa có bể nuôi',
    emptyStateBody: 'Tạo bể nuôi, kết nối thiết bị cảm biến, dữ liệu sẽ hiển thị tại đây.',
    errorBackendUnreachable: 'Không kết nối được máy chủ. Đang hiển thị giá trị gần nhất.',
    brokerDown: 'MQTT broker không chạy — dữ liệu mới sẽ không đến.',
    databaseDown: 'Không kết nối được cơ sở dữ liệu.',
    band: 'Ngưỡng',
    lastUpdated: 'Cập nhật',
    metricTempC: 'Nhiệt độ',
    metricHumidityPct: 'Độ ẩm',
    metricLightLux: 'Ánh sáng',
    metricUvIndex: 'Chỉ số UV',
    metricSurfaceTempC: 'Nhiệt độ bề mặt',
  },
  en: {
    appTitle: 'SmartReptile',
    live: 'Live',
    wallboard: 'Wallboard',
    statusInRange: 'In range',
    statusOutOfRange: 'Out of range',
    statusCritical: 'Critical',
    statusNoData: 'No data',
    statusUnavailable: 'Sensor unavailable',
    statusMaintenance: 'Maintenance',
    deviceOnline: 'online',
    deviceOffline: 'offline',
    livePaused: 'Live updates paused',
    emptyStateTitle: 'No terrarium yet',
    emptyStateBody: 'Create a terrarium, claim a sensor node, and readings will appear here.',
    errorBackendUnreachable: 'Cannot reach the server. Showing the last known values.',
    brokerDown: 'The MQTT broker is not running — new data will not arrive.',
    databaseDown: 'The database is unreachable.',
    band: 'band',
    lastUpdated: 'updated',
    metricTempC: 'Temperature',
    metricHumidityPct: 'Humidity',
    metricLightLux: 'Light',
    metricUvIndex: 'UV index',
    metricSurfaceTempC: 'Surface temperature',
  },
};

/** Active locale, persisted in the URL (?lang=en) so the demo can be switched without a rebuild. */
const params = new URLSearchParams(window.location.search);
export const locale = params.get('lang') === 'en' ? 'en' : 'vi';

/** Translates a key for the active locale. */
export function t(key) {
  return strings[locale][key] ?? strings.en[key] ?? key;
}

/** Localised metric name for a metric code. */
export function metricName(code) {
  const key = `metric${code.charAt(0).toUpperCase()}${code.slice(1)}`;
  return t(key);
}

/** Localised status label for an API status value. */
export function statusLabel(status) {
  switch (status) {
    case 'InRange': return t('statusInRange');
    case 'OutOfRange': return t('statusOutOfRange');
    case 'Critical': return t('statusCritical');
    case 'Unavailable': return t('statusUnavailable');
    case 'Maintenance': return t('statusMaintenance');
    default: return t('statusNoData');
  }
}

/** CSS class suffix for an API status value. */
export function statusClass(status) {
  switch (status) {
    case 'InRange': return 'in-range';
    case 'OutOfRange': return 'out-of-range';
    case 'Critical': return 'critical';
    case 'Unavailable': return 'unavailable';
    case 'Maintenance': return 'maintenance';
    default: return 'no-data';
  }
}
