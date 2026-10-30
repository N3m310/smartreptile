import React, { useEffect, useMemo, useState } from 'react';
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { AlertTriangle, Check, Clock, Droplets, ShieldAlert, Sun, Thermometer, WifiOff } from 'lucide-react';
import { apiBaseUrl, ApiError, readSession } from '../api/client';
import * as api from '../api/endpoints';
import type { AlertCounts, AlertDetail, AlertPage, AlertTimelineEntry, MetricSilence, TerrariumList } from '../api/types';
import { useI18n } from '../i18n';
import { describeError } from '../lib/messages';
import { usePolled } from '../lib/usePolled';
import { useSession } from '../state/session';

const POLL_MS = 30_000;
type AlertStateFilter = 'all' | 'in_progress' | 'Resolved';
type ResolveReason = 'Recovered' | 'FalsePositive' | 'SensorFault' | 'Accepted';

// The status bar, in the order the lifecycle moves: what still needs somebody, then what is done with, and the whole
// list as the reference point. "Chưa xử lý" is open **and** acknowledged — taking ownership of an alert is not
// closing it, so picking one up must not make it vanish from the tab that lists what needs attention.
const STATUS_TABS: { value: AlertStateFilter; label: string; count: (counts: AlertCounts) => number }[] = [
  { value: 'all', label: 'Tất cả', count: (counts) => counts.total },
  { value: 'in_progress', label: 'Chưa xử lý', count: (counts) => counts.open + counts.acknowledged },
  { value: 'Resolved', label: 'Đã xử lý', count: (counts) => counts.resolved },
];

const EMPTY_COUNTS: AlertCounts = { open: 0, acknowledged: 0, resolved: 0, total: 0 };

export const Alerts: React.FC = () => {
  const { t, lang } = useI18n();
  const { hasPermission } = useSession();
  const [stateFilter, setStateFilter] = useState<AlertStateFilter>('all');
  const [severityFilter, setSeverityFilter] = useState('');
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const [cursor, setCursor] = useState<string | undefined>();
  const [extraItems, setExtraItems] = useState<AlertPage['items']>([]);
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const [detail, setDetail] = useState<AlertDetail | null>(null);
  const [timeline, setTimeline] = useState<AlertTimelineEntry[]>([]);
  const [silences, setSilences] = useState<MetricSilence[]>([]);
  const [panelError, setPanelError] = useState<ApiError | null>(null);
  const [busy, setBusy] = useState(false);
  const [resolveOpen, setResolveOpen] = useState(false);
  const [resolveReason, setResolveReason] = useState<ResolveReason>('Recovered');
  const [resolveNote, setResolveNote] = useState('');
  const [silenceReason, setSilenceReason] = useState('');
  const [silenceMetric, setSilenceMetric] = useState('');
  const [silenceHours, setSilenceHours] = useState(1);
  const [liveState, setLiveState] = useState<'connecting' | 'connected' | 'offline'>('connecting');
  const [liveError, setLiveError] = useState<string | null>(null);
  const terrariums = usePolled<TerrariumList>(() => api.listTerrariums(), POLL_MS);
  const list = usePolled<AlertPage>(
    () => api.listAlerts({
      ...(stateFilter === 'all' ? {} : { state: stateFilter }),
      ...(severityFilter ? { severity: severityFilter } : {}),
      ...(from ? { from: new Date(from).toISOString() } : {}),
      ...(to ? { to: new Date(to).toISOString() } : {}),
      pageSize: 50,
    }),
    POLL_MS,
    [stateFilter, severityFilter, from, to]
  );
  const canWrite = hasPermission('alert.write');
  const canSilence = hasPermission('silence.write');
  const canRead = hasPermission('alert.read');
  const items = useMemo(() => {
    // Danger first, then warning, then the informational rows: an open critical excursion must not be pushed off the
    // screen by the resolved noise a flapping signal leaves behind. Newest first inside one severity.
    // De-duplicated by id first: a reload after an action returns rows the "older alerts" pages already hold, and a
    // repeated key makes React drop or double one of them.
    const rank: Record<string, number> = { Critical: 0, Warning: 1, Info: 2 };
    const byId = new Map<number, AlertPage['items'][number]>();
    for (const item of [...(list.data?.items ?? []), ...extraItems]) {
      if (!byId.has(item.id)) byId.set(item.id, item);
    }
    return [...byId.values()].sort((left, right) => {
      const bySeverity = (rank[left.severity] ?? 3) - (rank[right.severity] ?? 3);
      return bySeverity !== 0 ? bySeverity : right.triggeredAt.localeCompare(left.triggeredAt);
    });
  }, [list.data?.items, extraItems]);
  const names = useMemo(() => new Map((terrariums.data?.items ?? []).map((item) => [item.id, item.name])), [terrariums.data?.items]);
  const counts = list.data?.counts ?? EMPTY_COUNTS;
  const openCount = counts.open + counts.acknowledged;

  useEffect(() => {
    setExtraItems([]);
    setCursor(undefined);
  }, [stateFilter, severityFilter, from, to]);

  useEffect(() => {
    setCursor(list.data?.nextCursor ?? undefined);
  }, [list.data]);

  useEffect(() => {
    if (!canRead || !terrariums.data) return;
    const terrariumIds = terrariums.data.items.map((item) => item.id);
    if (terrariumIds.length === 0) {
      setLiveState('offline');
      return;
    }
    let cancelled = false;
    const connection = new HubConnectionBuilder()
      .withUrl(`${apiBaseUrl}/hubs/telemetry`, {
        accessTokenFactory: () => readSession()?.accessToken ?? '',
      })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Error)
      .build();
    const joinGroups = async () => {
      await Promise.all(terrariumIds.map((id) => connection.invoke('JoinTerrarium', id)));
    };

    connection.on('alertChanged', () => list.reload());
    connection.onreconnected(async () => {
      try {
        await joinGroups();
        if (!cancelled) {
          setLiveState('connected');
          setLiveError(null);
        }
      } catch (cause) {
        if (!cancelled) {
          setLiveState('offline');
          setLiveError(cause instanceof Error ? cause.message : 'Không thể đăng ký nhận cập nhật trực tiếp.');
        }
      }
    });
    void connection.start().then(async () => {
      await joinGroups();
      if (!cancelled) {
        setLiveState('connected');
        setLiveError(null);
      }
    }).catch((cause: unknown) => {
      if (!cancelled) {
        setLiveState('offline');
        setLiveError(cause instanceof Error ? cause.message : 'Không thể kết nối cập nhật trực tiếp.');
      }
    });
    return () => {
      cancelled = true;
      void connection.stop();
    };
  }, [canRead, terrariums.data?.items.map((item) => item.id).join('|')]);

  useEffect(() => {
    if (selectedId === null) {
      setDetail(null);
      setTimeline([]);
      setSilences([]);
      return;
    }
    let cancelled = false;
    setPanelError(null);
    void Promise.all([
      api.getAlert(selectedId),
      api.getAlertTimeline(selectedId),
    ]).then(([nextDetail, nextTimeline]) => {
      if (!cancelled) {
        setDetail(nextDetail);
        setTimeline(nextTimeline.items);
      }
    }).catch((cause) => {
      if (!cancelled) {
        setPanelError(cause instanceof ApiError ? cause : new ApiError('unknown', 0, null));
      }
    });
    return () => { cancelled = true; };
  }, [selectedId]);

  useEffect(() => {
    if (!detail || !canSilence) return;
    let cancelled = false;
    void api.listTerrariumSilences(detail.alert.terrariumId).then((result) => {
      if (!cancelled) setSilences(result.items);
    }).catch((cause) => {
      if (!cancelled) setPanelError(cause instanceof ApiError ? cause : new ApiError('unknown', 0, null));
    });
    return () => { cancelled = true; };
  }, [detail?.alert.terrariumId, canSilence]);

  const formatDate = (value: string) =>
    new Intl.DateTimeFormat(lang === 'vi' ? 'vi-VN' : 'en-US', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value));
  const metricName = (metric: string | null) => ({
    tempC: 'Nhiệt độ',
    humidityPct: 'Độ ẩm',
    lightLux: 'Ánh sáng',
    uvIndex: 'UV',
    surfaceTempC: 'Nhiệt độ bề mặt',
  }[metric ?? ''] ?? (metric ?? 'Sự cố thiết bị'));

  const refreshSelected = async () => {
    list.reload();
    if (selectedId !== null) {
      const [nextDetail, nextTimeline] = await Promise.all([
        api.getAlert(selectedId),
        api.getAlertTimeline(selectedId),
      ]);
      setDetail(nextDetail);
      setTimeline(nextTimeline.items);
    }
  };

  const acknowledge = async (alertId: number) => {
    setBusy(true);
    setPanelError(null);
    try {
      await api.acknowledgeAlert(alertId);
      await refreshSelected();
    } catch (cause) {
      setPanelError(cause instanceof ApiError ? cause : new ApiError('unknown', 0, null));
    } finally {
      setBusy(false);
    }
  };

  const resolve = async (event: React.FormEvent, alertId: number) => {
    event.preventDefault();
    setBusy(true);
    setPanelError(null);
    try {
      await api.resolveAlert(alertId, { reason: resolveReason, ...(resolveNote.trim() ? { note: resolveNote.trim() } : {}) });
      setResolveOpen(false);
      setResolveNote('');
      await refreshSelected();
    } catch (cause) {
      setPanelError(cause instanceof ApiError ? cause : new ApiError('unknown', 0, null));
    } finally {
      setBusy(false);
    }
  };

  const createSilence = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!detail || !silenceReason.trim()) return;
    setBusy(true);
    setPanelError(null);
    try {
      await api.createTerrariumSilence(detail.alert.terrariumId, {
        ...(silenceMetric ? { metric: silenceMetric } : {}),
        untilUtc: new Date(Date.now() + silenceHours * 60 * 60 * 1000).toISOString(),
        reason: silenceReason.trim(),
      });
      setSilenceReason('');
      const result = await api.listTerrariumSilences(detail.alert.terrariumId);
      setSilences(result.items);
    } catch (cause) {
      setPanelError(cause instanceof ApiError ? cause : new ApiError('unknown', 0, null));
    } finally {
      setBusy(false);
    }
  };

  const cancelSilence = async (silence: MetricSilence) => {
    if (!detail) return;
    setBusy(true);
    setPanelError(null);
    try {
      await api.cancelTerrariumSilence(detail.alert.terrariumId, silence.id);
      setSilences((current) => current.filter((item) => item.id !== silence.id));
    } catch (cause) {
      setPanelError(cause instanceof ApiError ? cause : new ApiError('unknown', 0, null));
    } finally {
      setBusy(false);
    }
  };

  const loadMore = async () => {
    if (!list.data?.nextCursor) return;
    setBusy(true);
    try {
      const page = await api.listAlerts({
        ...(stateFilter === 'all' ? {} : { state: stateFilter }),
        ...(severityFilter ? { severity: severityFilter } : {}),
        ...(from ? { from: new Date(from).toISOString() } : {}),
        ...(to ? { to: new Date(to).toISOString() } : {}),
        cursor,
        pageSize: 50,
      });
      setExtraItems((current) => [...current, ...page.items]);
      setCursor(page.nextCursor ?? undefined);
    } catch (cause) {
      setPanelError(cause instanceof ApiError ? cause : new ApiError('unknown', 0, null));
    } finally {
      setBusy(false);
    }
  };

  // The confirmation belongs to the row it was asked from, not to the detail panel: on a viewport narrower than
  // `xl` that panel stacks below the whole list, so a form opened there was thousands of pixels off screen and the
  // button looked dead.
  const resolveFormFor = (alertId: number) => (
    <form onSubmit={(event) => void resolve(event, alertId)} className="mt-3 space-y-3 rounded-lg border border-hairline bg-canvas p-3">
      <h3 className="text-sm font-semibold text-ink">Xác nhận xử lý</h3>
      <label className="block text-xs text-muted">Lý do<select required value={resolveReason} onChange={(event) => setResolveReason(event.target.value as ResolveReason)} className="mt-1 w-full rounded-lg border border-hairline bg-surface px-3 py-2 text-sm text-ink"><option value="Recovered">Đã phục hồi</option><option value="FalsePositive">Cảnh báo không chính xác</option><option value="SensorFault">Lỗi cảm biến</option><option value="Accepted">Chấp nhận rủi ro</option></select></label>
      <label className="block text-xs text-muted">Ghi chú (không bắt buộc)<textarea maxLength={500} value={resolveNote} onChange={(event) => setResolveNote(event.target.value)} className="mt-1 w-full rounded-lg border border-hairline bg-surface px-3 py-2 text-sm text-ink" /></label>
      <div className="flex justify-end gap-2"><button type="button" onClick={() => setResolveOpen(false)} className="rounded-lg border border-hairline px-3 py-2 text-xs text-muted">Huỷ</button><button disabled={busy} className="rounded-lg bg-brand px-3 py-2 text-xs font-semibold text-white disabled:opacity-50">Xác nhận xử lý</button></div>
    </form>
  );

  if (!canRead) {
    return <p className="rounded-xl border border-hairline bg-surface p-5 text-sm text-muted">{t('errorForbidden')}</p>;
  }

  return (
    <div className="space-y-6">
      <header className="flex flex-wrap items-end justify-between gap-3">
        <div><h1 className="font-heading text-2xl font-bold text-ink">Cảnh báo</h1><p className="mt-1 text-sm text-muted">Theo dõi và xử lý cảnh báo từ các bể nuôi.</p></div>
        <div className="flex items-center gap-3">
          <span className="inline-flex items-center gap-1.5 rounded-lg border border-hairline bg-surface px-3 py-2 text-xs text-muted"><ShieldAlert className="h-4 w-4 text-warning" aria-hidden="true" />{openCount} cần xử lý</span>
          <span className={`text-xs ${liveState === 'connected' ? 'text-accent' : 'text-muted'}`}>{liveState === 'connected' ? 'Cập nhật trực tiếp' : 'Đang đồng bộ định kỳ'}</span>
        </div>
      </header>
      {liveError && <p role="status" className="rounded-lg border border-warning/30 bg-warning/5 p-3 text-xs text-muted">Cập nhật trực tiếp tạm thời không khả dụng; danh sách vẫn tự làm mới mỗi 30 giây.</p>}
      {list.error && <p role="alert" className="rounded-lg border border-critical/30 bg-critical/5 p-3 text-sm text-critical">{describeError(t, list.error)}</p>}
      {terrariums.error && <p role="alert" className="rounded-lg border border-critical/30 bg-critical/5 p-3 text-sm text-critical">{describeError(t, terrariums.error)}</p>}

      <nav aria-label="Trạng thái cảnh báo" className="flex flex-wrap items-center gap-2 rounded-xl border border-hairline bg-surface p-2">
        {STATUS_TABS.map((tab) => {
          const active = stateFilter === tab.value;
          return (
            <button
              key={tab.value}
              type="button"
              aria-pressed={active}
              onClick={() => setStateFilter(tab.value)}
              className={`rounded-lg px-3 py-2 text-sm transition ${active ? 'bg-accent font-semibold text-canvas' : 'text-muted hover:bg-raised hover:text-ink'}`}
            >
              {tab.label} ({tab.count(counts)})
            </button>
          );
        })}
      </nav>

      <section className="grid gap-3 rounded-xl border border-hairline bg-surface p-4 md:grid-cols-3">
        <label className="text-xs text-muted">Mức độ<select value={severityFilter} onChange={(event) => setSeverityFilter(event.target.value)} className="mt-1 w-full rounded-lg border border-hairline bg-canvas px-3 py-2 text-sm text-ink"><option value="">Tất cả mức độ</option><option value="Info">Thông tin</option><option value="Warning">Cảnh báo</option><option value="Critical">Nguy hiểm</option></select></label>
        <label className="text-xs text-muted">Từ ngày<input type="datetime-local" value={from} onChange={(event) => setFrom(event.target.value)} className="mt-1 w-full rounded-lg border border-hairline bg-canvas px-3 py-2 text-sm text-ink" /></label>
        <label className="text-xs text-muted">Đến ngày<input type="datetime-local" value={to} onChange={(event) => setTo(event.target.value)} className="mt-1 w-full rounded-lg border border-hairline bg-canvas px-3 py-2 text-sm text-ink" /></label>
      </section>

      {list.loading && !list.data ? <p className="py-8 text-center text-sm text-muted">Đang tải cảnh báo…</p> : null}
      {!list.loading && !list.error && items.length === 0 ? <p className="rounded-xl border border-hairline bg-surface p-8 text-center text-sm text-muted">Không có cảnh báo phù hợp với bộ lọc.</p> : null}

      <div className="grid gap-5 xl:grid-cols-[minmax(0,1fr)_minmax(360px,0.85fr)]">
        <div className="space-y-3">
          {items.map((alert) => {
            const active = alert.state !== 'Resolved';
            return (
              <article key={alert.id} className={`rounded-xl border bg-surface p-4 ${selectedId === alert.id ? 'border-brand' : 'border-hairline'}`}>
                <button type="button" onClick={() => { setSelectedId(alert.id); setResolveOpen(false); }} className="w-full text-left">
                  <div className="flex items-start gap-3">
                    <span className={`mt-0.5 rounded-lg p-2 ${alert.severity === 'Critical' ? 'bg-critical/10 text-critical' : alert.severity === 'Warning' ? 'bg-warning/10 text-warning' : 'bg-raised text-muted'}`}>
                      {alert.metric === 'tempC' || alert.metric === 'surfaceTempC' ? <Thermometer className="h-4 w-4" /> : alert.metric === 'humidityPct' ? <Droplets className="h-4 w-4" /> : alert.metric === 'lightLux' ? <Sun className="h-4 w-4" /> : alert.metric === null ? <WifiOff className="h-4 w-4" /> : <AlertTriangle className="h-4 w-4" />}
                    </span>
                    <span className="min-w-0 flex-1">
                      <span className="flex flex-wrap items-center gap-2"><strong className="text-sm text-ink">{metricName(alert.metric)}</strong><span className="rounded-full bg-raised px-2 py-0.5 text-[10px] text-muted">{alert.severity}</span><span className="rounded-full bg-raised px-2 py-0.5 text-[10px] text-muted">{alert.state}</span></span>
                      <span className="mt-1 block text-xs text-muted">{names.get(alert.terrariumId) ?? alert.terrariumId} · {alert.deviceId}</span>
                      <span className="mt-1 block text-xs text-muted">{alert.peakValue ?? alert.triggeringValue ?? '—'}{alert.bandMin !== null || alert.bandMax !== null ? ` · Ngưỡng ${alert.bandMin ?? '—'}–${alert.bandMax ?? '—'}` : ''}</span>
                      <span className="mt-2 flex items-center gap-1 text-[11px] text-muted"><Clock className="h-3.5 w-3.5" aria-hidden="true" />{formatDate(alert.triggeredAt)}</span>
                    </span>
                  </div>
                </button>
                <div className="mt-3 flex justify-end gap-2">
                  {active && canWrite && alert.state === 'Open' && <button type="button" disabled={busy} onClick={() => void acknowledge(alert.id)} className="rounded-lg border border-hairline px-3 py-1.5 text-xs text-ink disabled:opacity-50">Tiếp nhận</button>}
                  {active && canWrite && <button type="button" disabled={busy} onClick={() => { setSelectedId(alert.id); setResolveOpen(selectedId === alert.id ? !resolveOpen : true); }} className="inline-flex items-center gap-1 rounded-lg bg-brand px-3 py-1.5 text-xs font-semibold text-white disabled:opacity-50"><Check className="h-3.5 w-3.5" aria-hidden="true" />Xử lý</button>}
                </div>
                {canWrite && resolveOpen && selectedId === alert.id && resolveFormFor(alert.id)}
              </article>
            );
          })}
          {cursor && <button type="button" disabled={busy} onClick={() => void loadMore()} className="w-full rounded-lg border border-hairline bg-surface px-4 py-2 text-sm text-ink disabled:opacity-50">Tải cảnh báo cũ hơn</button>}
        </div>

        {selectedId !== null && (
          <aside className="h-fit space-y-4 rounded-xl border border-hairline bg-surface p-4 xl:sticky xl:top-4">
            <div className="flex items-start justify-between gap-3"><div><h2 className="font-heading font-semibold text-ink">Chi tiết cảnh báo #{selectedId}</h2><p className="mt-1 text-xs text-muted">{detail ? `${names.get(detail.alert.terrariumId) ?? detail.alert.terrariumId} · ${metricName(detail.alert.metric)}` : 'Đang tải…'}</p></div><button type="button" onClick={() => { setSelectedId(null); setResolveOpen(false); }} className="text-xs text-muted hover:text-ink">Đóng</button></div>
            {panelError && <p role="alert" className="text-sm text-critical">{describeError(t, panelError)}</p>}
            {detail && <>
              <dl className="grid grid-cols-2 gap-2 text-xs">
                <Info label="Giá trị kích hoạt" value={detail.alert.triggeringValue === null ? '—' : String(detail.alert.triggeringValue)} />
                <Info label="Giá trị cao nhất" value={detail.alert.peakValue === null ? '—' : String(detail.alert.peakValue)} />
                <Info label="Pha đánh giá" value={detail.alert.phase} />
                <Info label="Nguồn" value={detail.alert.source} />
              </dl>
              {detail.series.length > 0 && <div><h3 className="mb-2 text-xs font-semibold text-ink">Diễn biến ({detail.series.length} điểm)</h3><ul className="max-h-36 space-y-1 overflow-auto text-xs text-muted">{detail.series.map((point) => <li key={point.at} className="flex justify-between gap-2"><time>{formatDate(point.at)}</time><span>{point.value}</span></li>)}</ul></div>}
              {detail.seriesTruncated && <p className="text-[11px] text-muted">Chỉ hiển thị một phần dữ liệu của sự cố.</p>}
              {canSilence && <section className="space-y-3 border-t border-hairline pt-3"><h3 className="text-sm font-semibold text-ink">Tạm tắt thông báo</h3><form onSubmit={(event) => void createSilence(event)} className="space-y-2"><label className="block text-xs text-muted">Phạm vi<select value={silenceMetric} onChange={(event) => setSilenceMetric(event.target.value)} className="mt-1 w-full rounded-lg border border-hairline bg-canvas px-3 py-2 text-sm text-ink"><option value="">Tất cả chỉ số</option><option value="tempC">Nhiệt độ</option><option value="humidityPct">Độ ẩm</option><option value="lightLux">Ánh sáng</option><option value="uvIndex">UV</option><option value="surfaceTempC">Nhiệt độ bề mặt</option></select></label><label className="block text-xs text-muted">Thời lượng<select value={silenceHours} onChange={(event) => setSilenceHours(Number(event.target.value))} className="mt-1 w-full rounded-lg border border-hairline bg-canvas px-3 py-2 text-sm text-ink">{[1, 2, 4, 8, 12, 24].map((hour) => <option key={hour} value={hour}>{hour} giờ</option>)}</select></label><label className="block text-xs text-muted">Lý do<input required maxLength={200} value={silenceReason} onChange={(event) => setSilenceReason(event.target.value)} className="mt-1 w-full rounded-lg border border-hairline bg-canvas px-3 py-2 text-sm text-ink" /></label><button disabled={busy} className="rounded-lg border border-hairline px-3 py-2 text-xs text-ink disabled:opacity-50">Tạo khoảng tắt</button></form>
                {silences.map((silence) => <div key={silence.id} className="flex items-center justify-between gap-2 text-xs"><span className="text-muted">{silence.metric ?? 'Tất cả'} đến {formatDate(silence.untilUtc)}</span><button type="button" disabled={busy} onClick={() => void cancelSilence(silence)} className="text-accent hover:underline">Huỷ</button></div>)}
              </section>}
              <section className="border-t border-hairline pt-3"><h3 className="mb-2 text-sm font-semibold text-ink">Lịch sử xử lý</h3>{timeline.length ? <ol className="space-y-2 text-xs">{timeline.map((entry, index) => <li key={`${entry.event}-${entry.at}-${index}`} className="flex justify-between gap-3 text-muted"><span>{entry.event}{entry.reason ? ` · ${entry.reason}` : ''}{entry.note ? ` — ${entry.note}` : ''}</span><time className="shrink-0">{formatDate(entry.at)}</time></li>)}</ol> : <p className="text-xs text-muted">Chưa có sự kiện.</p>}</section>
            </>}
          </aside>
        )}
      </div>
    </div>
  );
};

const Info: React.FC<{ label: string; value: string }> = ({ label, value }) => (
  <div className="rounded-lg border border-hairline p-2"><dt className="text-muted">{label}</dt><dd className="mt-1 font-medium text-ink">{value}</dd></div>
);
