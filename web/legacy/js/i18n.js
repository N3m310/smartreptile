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
    errorEndpointMissing: 'Chức năng này chưa có trên máy chủ — phần tương ứng chưa được xây dựng.',
    errorNotFound: 'Không tìm thấy dữ liệu này, hoặc bạn không có quyền xem.',
    errorRequestFailed: 'Máy chủ đã trả lời nhưng từ chối yêu cầu.',
    readinessMissing: 'Máy chủ chưa có chức năng kiểm tra tình trạng.',
    brokerDown: 'MQTT broker không chạy — dữ liệu mới sẽ không đến.',
    databaseDown: 'Không kết nối được cơ sở dữ liệu.',
    signIn: 'Đăng nhập',
    signOut: 'Đăng xuất',
    usernameOrEmail: 'Tên đăng nhập hoặc email',
    password: 'Mật khẩu',
    signInPrompt: 'Đăng nhập để xem dữ liệu của bạn.',
    signingIn: 'Đang đăng nhập…',
    invalidCredentials: 'Sai tên đăng nhập hoặc mật khẩu.',
    sessionExpired: 'Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.',
    signedInAs: 'Đăng nhập với',
    forgotPassword: 'Quên mật khẩu?',
    backToSignIn: 'Quay lại đăng nhập',
    recoverPrompt: 'Nhập mã khôi phục bạn đã lưu khi tạo tài khoản.',
    recoveryCode: 'Mã khôi phục',
    newPassword: 'Mật khẩu mới',
    recoverButton: 'Đặt lại mật khẩu',
    recovering: 'Đang xử lý…',
    recoveryFailed: 'Tài khoản hoặc mã khôi phục không đúng.',
    passwordPolicyViolation: 'Mật khẩu mới chưa đạt yêu cầu.',
    accountLocked: 'Quá nhiều lần thử. Vui lòng thử lại sau ít phút.',
    recoveryIssued: 'Mã khôi phục mới (chỉ hiển thị một lần):',
    recoveryDone: 'Đã đặt lại mật khẩu. Hãy đăng nhập bằng mật khẩu mới.',
    recoverHintBackup: 'Mã dự phòng bạn đã lưu khi tạo tài khoản.',
    recoverHintIssued: 'Mã do hệ thống tạo, chỉ dùng được một lần và hết hạn sau 30 phút.',
    resetCodeLabel: 'Mã đặt lại',
    sendResetCode: 'Gửi mã đặt lại',
    sendingResetCode: 'Đang gửi…',
    resetCodeRequested: 'Yêu cầu đã được ghi nhận. Nếu tài khoản tồn tại, mã đặt lại đã được tạo.',
    resetCodeLogHint: 'Bản triển khai này chưa có máy chủ thư, nên mã nằm trong nhật ký máy chủ (cửa sổ chạy API).',
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
    errorEndpointMissing: 'This feature is not on the server yet — that part is not built.',
    errorNotFound: 'Not found, or you do not have access to it.',
    errorRequestFailed: 'The server answered but refused the request.',
    readinessMissing: 'This server has no readiness endpoint yet.',
    brokerDown: 'The MQTT broker is not running — new data will not arrive.',
    databaseDown: 'The database is unreachable.',
    signIn: 'Sign in',
    signOut: 'Sign out',
    usernameOrEmail: 'Username or email',
    password: 'Password',
    signInPrompt: 'Sign in to see your terrariums.',
    signingIn: 'Signing in…',
    invalidCredentials: 'Wrong username or password.',
    sessionExpired: 'Your session has expired. Please sign in again.',
    signedInAs: 'Signed in as',
    forgotPassword: 'Forgot password?',
    backToSignIn: 'Back to sign in',
    recoverPrompt: 'Enter the recovery code you saved when you created the account.',
    recoveryCode: 'Recovery code',
    newPassword: 'New password',
    recoverButton: 'Reset password',
    recovering: 'Working…',
    recoveryFailed: 'That account and recovery code do not match.',
    passwordPolicyViolation: 'The new password was refused.',
    accountLocked: 'Too many attempts. Try again in a few minutes.',
    recoveryIssued: 'New recovery code (shown once):',
    recoveryDone: 'Password reset. Sign in with your new password.',
    recoverHintBackup: 'The backup code you saved when you created the account.',
    recoverHintIssued: 'Issued by the server, single use, and expires after 30 minutes.',
    resetCodeLabel: 'Reset code',
    sendResetCode: 'Send me a reset code',
    sendingResetCode: 'Requesting…',
    resetCodeRequested: 'The request was recorded. If the account exists, a reset code was issued.',
    resetCodeLogHint: 'This deployment has no mail server, so the code is written to the server log (the API window).',
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
