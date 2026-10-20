import React, { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Bell, Save, Sliders, User } from 'lucide-react';
import * as api from '../api/endpoints';
import { ApiError } from '../api/client';
import type { TimeZoneList } from '../api/types';
import { useI18n } from '../i18n';
import { describeError } from '../lib/messages';
import { usePolled } from '../lib/usePolled';
import { useSession } from '../state/session';

interface ProfileDraft {
  preferredLanguage: string;
  timeZoneId: string;
  quietHoursStart: string;
  quietHoursEnd: string;
  minNotifySeverity: string;
  channelFcmEnabled: boolean;
  channelEmailEnabled: boolean;
}

const initialDraft = (profile: ReturnType<typeof useSession>['user']): ProfileDraft => ({
  preferredLanguage: profile?.preferredLanguage ?? 'vi',
  timeZoneId: profile?.timeZoneId ?? 'Asia/Ho_Chi_Minh',
  quietHoursStart: profile?.quietHoursStart ?? '',
  quietHoursEnd: profile?.quietHoursEnd ?? '',
  minNotifySeverity: profile?.minNotifySeverity ?? 'warning',
  channelFcmEnabled: profile?.channelFcmEnabled ?? false,
  channelEmailEnabled: profile?.channelEmailEnabled ?? false,
});

export const Settings: React.FC = () => {
  const { t, setLang } = useI18n();
  const navigate = useNavigate();
  const { user, refreshProfile } = useSession();
  const timeZones = usePolled<TimeZoneList>(() => api.listTimeZones(), null);
  const [draft, setDraft] = useState<ProfileDraft>(() => initialDraft(user));
  const [busy, setBusy] = useState(false);
  const [saved, setSaved] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const [validationError, setValidationError] = useState<string | null>(null);

  useEffect(() => {
    setDraft(initialDraft(user));
  }, [user]);

  const update = <K extends keyof ProfileDraft>(key: K, value: ProfileDraft[K]) =>
    setDraft((current) => ({ ...current, [key]: value }));

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    setSaved(false);
    setError(null);
    setValidationError(null);
    if (Boolean(draft.quietHoursStart) !== Boolean(draft.quietHoursEnd)) {
      setValidationError('Vui lòng nhập đủ giờ bắt đầu và giờ kết thúc yên tĩnh, hoặc để trống cả hai.');
      return;
    }

    setBusy(true);
    try {
      await api.updateMyProfile({
        preferredLanguage: draft.preferredLanguage,
        timeZoneId: draft.timeZoneId,
        quietHoursStart: draft.quietHoursStart,
        quietHoursEnd: draft.quietHoursEnd,
        minNotifySeverity: draft.minNotifySeverity,
        channelFcmEnabled: draft.channelFcmEnabled,
        channelEmailEnabled: draft.channelEmailEnabled,
      });
      await refreshProfile();
      if (draft.preferredLanguage === 'vi' || draft.preferredLanguage === 'en') {
        setLang(draft.preferredLanguage);
      }
      setSaved(true);
    } catch (cause) {
      setError(cause instanceof ApiError ? cause : new ApiError('unknown', 0, null));
    } finally {
      setBusy(false);
    }
  };

  const fieldClass = 'mt-1 w-full rounded-lg border border-hairline bg-canvas px-3 py-2 text-sm text-ink';

  return (
    <div className="max-w-4xl space-y-6">
      <header>
        <h1 className="font-heading text-2xl font-bold text-ink">Cài đặt cá nhân</h1>
        <p className="mt-1 text-sm text-muted">Cập nhật ngôn ngữ, múi giờ và cách nhận thông báo.</p>
      </header>

      {error && <p role="alert" className="rounded-lg border border-critical/30 bg-critical/5 p-3 text-sm text-critical">{describeError(t, error)}</p>}
      {validationError && <p role="alert" className="rounded-lg border border-critical/30 bg-critical/5 p-3 text-sm text-critical">{validationError}</p>}
      {saved && <p role="status" className="rounded-lg border border-accent/30 bg-accent/5 p-3 text-sm text-ink">Đã lưu cài đặt cá nhân.</p>}

      <form onSubmit={(event) => void submit(event)} className="space-y-6">
        <section className="rounded-2xl border border-hairline bg-surface p-5 sm:p-6">
          <div className="mb-5 flex items-center gap-3 border-b border-hairline pb-4">
            <User className="h-5 w-5 text-brand" aria-hidden="true" />
            <div><h2 className="font-heading font-semibold text-ink">Tài khoản</h2><p className="text-xs text-muted">Tên đăng nhập và email chỉ đọc; API hiện không hỗ trợ đổi hai thông tin này.</p></div>
          </div>
          <div className="grid gap-4 sm:grid-cols-2">
            <label className="text-xs font-medium text-muted">Tên đăng nhập<input readOnly value={user?.username ?? ''} className={`${fieldClass} opacity-70`} /></label>
            <label className="text-xs font-medium text-muted">Email<input readOnly value={user?.email ?? ''} className={`${fieldClass} opacity-70`} /></label>
            <label className="text-xs font-medium text-muted">Ngôn ngữ giao diện
              <select value={draft.preferredLanguage} onChange={(event) => update('preferredLanguage', event.target.value)} className={fieldClass}>
                <option value="vi">Tiếng Việt</option><option value="en">English</option>
              </select>
            </label>
            <label className="text-xs font-medium text-muted">Múi giờ
              <select required value={draft.timeZoneId} onChange={(event) => update('timeZoneId', event.target.value)} className={fieldClass}>
                {timeZones.data?.items.map((zone) => <option key={zone.ianaId} value={zone.ianaId}>{zone.displayName} ({zone.utcOffset})</option>)}
              </select>
            </label>
          </div>
          {timeZones.error && <p role="alert" className="mt-3 text-xs text-critical">{describeError(t, timeZones.error)}</p>}
        </section>

        <section className="rounded-2xl border border-hairline bg-surface p-5 sm:p-6">
          <div className="mb-5 flex items-center gap-3 border-b border-hairline pb-4">
            <Bell className="h-5 w-5 text-brand" aria-hidden="true" />
            <div><h2 className="font-heading font-semibold text-ink">Thông báo</h2><p className="text-xs text-muted">Cài đặt này được lưu trực tiếp vào hồ sơ tài khoản.</p></div>
          </div>
          <div className="grid gap-4 sm:grid-cols-2">
            <label className="text-xs font-medium text-muted">Mức cảnh báo tối thiểu
              <select value={draft.minNotifySeverity} onChange={(event) => update('minNotifySeverity', event.target.value)} className={fieldClass}>
                <option value="info">Thông tin trở lên</option><option value="warning">Cảnh báo trở lên</option><option value="critical">Chỉ mức nguy hiểm</option>
              </select>
            </label>
            <fieldset className="space-y-3 text-sm text-ink">
              <legend className="mb-2 text-xs font-medium text-muted">Kênh nhận thông báo</legend>
              <label className="flex items-center gap-2"><input type="checkbox" checked={draft.channelEmailEnabled} onChange={(event) => update('channelEmailEnabled', event.target.checked)} className="accent-brand" /> Email</label>
              <label className="flex items-center gap-2"><input type="checkbox" checked={draft.channelFcmEnabled} onChange={(event) => update('channelFcmEnabled', event.target.checked)} className="accent-brand" /> Thông báo đẩy{user?.hasFcmToken ? '' : ' (chưa đăng ký thiết bị nhận)'}</label>
            </fieldset>
            <label className="text-xs font-medium text-muted">Bắt đầu giờ yên tĩnh<input type="time" value={draft.quietHoursStart} onChange={(event) => update('quietHoursStart', event.target.value)} className={fieldClass} /></label>
            <label className="text-xs font-medium text-muted">Kết thúc giờ yên tĩnh<input type="time" value={draft.quietHoursEnd} onChange={(event) => update('quietHoursEnd', event.target.value)} className={fieldClass} /></label>
            <p className="text-xs text-muted sm:col-span-2">Để trống cả hai ô giờ yên tĩnh để tắt khoảng thời gian này.</p>
          </div>
        </section>

        <section className="rounded-2xl border border-hairline bg-surface p-5 sm:p-6">
          <div className="mb-3 flex items-center gap-3"><Sliders className="h-5 w-5 text-brand" aria-hidden="true" /><h2 className="font-heading font-semibold text-ink">Ngưỡng bể nuôi</h2></div>
          <p className="text-sm text-muted">Ngưỡng được cấu hình riêng theo từng bể nuôi.</p>
          <button type="button" onClick={() => navigate('/terrariums')} className="mt-3 inline-flex text-sm font-medium text-accent hover:underline">Mở danh sách bể nuôi</button>
        </section>

        <div className="flex justify-end">
          <button disabled={busy || timeZones.loading} className="inline-flex items-center gap-2 rounded-lg bg-brand px-4 py-2.5 text-sm font-semibold text-white disabled:opacity-50">
            <Save className="h-4 w-4" aria-hidden="true" />{busy ? 'Đang lưu…' : 'Lưu cài đặt'}
          </button>
        </div>
      </form>
    </div>
  );
};
