import React, { useState } from 'react';
import { AlertTriangle, Cpu, Pencil, Plus, RefreshCw, Search, Trash2, Wifi, WifiOff } from 'lucide-react';
import * as api from '../api/endpoints';
import { ApiError } from '../api/client';
import type { DeviceItem, DeviceList, TerrariumList } from '../api/types';
import { useI18n } from '../i18n';
import { describeError } from '../lib/messages';
import { usePolled } from '../lib/usePolled';
import { useSession } from '../state/session';

const POLL_MS = 30_000;

export const Devices: React.FC = () => {
  const { t, lang } = useI18n();
  const { hasPermission } = useSession();
  const devices = usePolled<DeviceList>(() => api.listDevices(), POLL_MS);
  const terrariums = usePolled<TerrariumList>(() => api.listTerrariums(), null);
  const [searchTerm, setSearchTerm] = useState('');
  const [statusFilter, setStatusFilter] = useState('all');
  const [selected, setSelected] = useState<DeviceItem | null>(null);
  const [editing, setEditing] = useState(false);
  const [deviceName, setDeviceName] = useState('');
  const [samplingIntervalSec, setSamplingIntervalSec] = useState(60);
  const [publishIntervalSec, setPublishIntervalSec] = useState(60);
  const [claimCode, setClaimCode] = useState('');
  const [claimMode, setClaimMode] = useState<'code' | 'mac'>('code');
  const [macAddress, setMacAddress] = useState('');
  const [terrariumId, setTerrariumId] = useState('');
  const [moveTerrariumId, setMoveTerrariumId] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const [secret, setSecret] = useState<string | null>(null);
  const items = devices.data?.items ?? [];
  const canRead = hasPermission('device.read');
  const canWrite = hasPermission('device.write');
  const canProvision = hasPermission('device.provision');

  const filtered = items.filter((device) => {
    const query = searchTerm.trim().toLowerCase();
    const matchesQuery =
      !query ||
      [device.deviceId, device.deviceName, device.macAddress, device.terrariumName ?? '']
        .some((value) => value.toLowerCase().includes(query));
    const runtime = device.runtime.toLowerCase();
    const matchesStatus = statusFilter === 'all' || runtime === statusFilter;
    return matchesQuery && matchesStatus;
  });
  const offline = items.filter((device) => device.runtime.toLowerCase() === 'offline');

  const runAction = async (action: () => Promise<unknown>, reload = true) => {
    setBusy(true);
    setError(null);
    setSecret(null);
    try {
      await action();
      if (reload) {
        devices.reload();
      }
    } catch (cause) {
      setError(cause instanceof ApiError ? cause : new ApiError('unknown', 0, null));
    } finally {
      setBusy(false);
    }
  };

  const startEdit = (device: DeviceItem) => {
    setSelected(device);
    setDeviceName(device.deviceName);
    setSamplingIntervalSec(device.samplingIntervalSec);
    setPublishIntervalSec(device.publishIntervalSec);
    setMoveTerrariumId(device.terrariumId ?? '');
    setEditing(true);
    setError(null);
    // The picker is fed from the same read the claim form uses; refreshed on open so an enclosure created in
    // another tab is bindable without a page reload.
    terrariums.reload();
  };

  const bindToTerrarium = async (device: DeviceItem) => {
    if (!moveTerrariumId) return;
    await runAction(async () => {
      const result = await api.moveDevice(device.deviceId, moveTerrariumId);
      setSelected(result.device);
      setMoveTerrariumId(result.device.terrariumId ?? '');
      terrariums.reload();
    });
  };

  const submitEdit = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!selected) return;
    await runAction(async () => {
      const updated = await api.patchDevice(selected.deviceId, {
        deviceName,
        samplingIntervalSec,
        publishIntervalSec,
      });
      setSelected(updated);
      setEditing(false);
    });
  };

  const claim = async (event: React.FormEvent) => {
    event.preventDefault();
    const byMac = claimMode === 'mac';
    if (!(byMac ? macAddress.trim() : claimCode.trim()) || !terrariumId) return;
    await runAction(async () => {
      const result = byMac
        ? await api.claimDeviceByMac({ macAddress: macAddress.trim(), terrariumId })
        : await api.claimDevice({ claimCode: claimCode.trim(), terrariumId });
      setSecret(result.secret);
      setClaimCode('');
      setMacAddress('');
      devices.reload();
    }, false);
  };

  const removeDevice = (device: DeviceItem) => {
    if (!window.confirm(`Gỡ thiết bị ${device.deviceName} khỏi tài khoản?`)) return;
    void runAction(async () => {
      await api.deleteDevice(device.deviceId);
      if (selected?.deviceId === device.deviceId) setSelected(null);
    });
  };

  const showDevice = async (device: DeviceItem) => {
    setSelected(device);
    setEditing(false);
    setError(null);
    setBusy(true);
    try {
      setSelected(await api.getDevice(device.deviceId));
    } catch (cause) {
      setError(cause instanceof ApiError ? cause : new ApiError('unknown', 0, null));
    } finally {
      setBusy(false);
    }
  };

  const formatDate = (value: string | null) =>
    value ? new Intl.DateTimeFormat(lang === 'vi' ? 'vi-VN' : 'en-US', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value)) : '—';

  if (!canRead) {
    return <p className="rounded-xl border border-hairline bg-surface p-5 text-sm text-muted">{t('errorForbidden')}</p>;
  }

  return (
    <div className="space-y-6">
      <header className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="font-heading text-2xl font-bold text-ink">Thiết bị IoT</h1>
          <p className="mt-1 text-sm text-muted">Trạng thái và cấu hình thiết bị thuộc các bể nuôi của bạn.</p>
        </div>
        <div className="flex items-center gap-2">
          {canProvision && (
            <button type="button" onClick={() => document.getElementById('device-claim-form')?.scrollIntoView({ behavior: 'smooth', block: 'center' })} className="inline-flex items-center gap-1.5 rounded-lg bg-brand px-3 py-2 text-sm font-semibold text-white">
              <Plus className="h-4 w-4" aria-hidden="true" />Thêm thiết bị
            </button>
          )}
          <span className="rounded-lg border border-hairline bg-surface px-3 py-2 text-xs text-muted">
            Tổng thiết bị: <strong className="text-ink">{items.length}</strong>
          </span>
          <button type="button" onClick={devices.reload} className="rounded-lg border border-hairline bg-surface p-2 text-muted hover:text-ink" aria-label="Tải lại">
            <RefreshCw className="h-4 w-4" aria-hidden="true" />
          </button>
        </div>
      </header>

      {offline.length > 0 && (
        <div className="flex items-start gap-3 rounded-xl border border-warning/30 bg-warning/5 p-4">
          <AlertTriangle className="mt-0.5 h-5 w-5 shrink-0 text-warning" aria-hidden="true" />
          <div>
            <p className="text-sm font-semibold text-ink">{offline.length} thiết bị đang ngoại tuyến</p>
            <p className="mt-1 text-xs text-muted">{offline.map((device) => device.deviceName).join(', ')}</p>
          </div>
        </div>
      )}

      {canProvision && (
        <form id="device-claim-form" onSubmit={(event) => void claim(event)} className="grid gap-3 rounded-xl border border-hairline bg-surface p-4 sm:grid-cols-[1fr_1fr_auto]">
          <div className="flex gap-2 sm:col-span-3" role="tablist" aria-label="Cách nhận thiết bị">
            {(['code', 'mac'] as const).map((mode) => (
              <button key={mode} type="button" role="tab" aria-selected={claimMode === mode} onClick={() => setClaimMode(mode)} className={`rounded-lg border px-3 py-1.5 text-xs font-semibold ${claimMode === mode ? 'border-brand bg-brand/10 text-brand' : 'border-hairline text-muted hover:text-ink'}`}>
                {mode === 'code' ? 'Bằng mã nhận' : 'Bằng địa chỉ MAC'}
              </button>
            ))}
          </div>
          {claimMode === 'code' ? (
            <label className="text-xs font-medium text-muted">
              Mã nhận thiết bị
              <input required value={claimCode} onChange={(event) => setClaimCode(event.target.value)} className="mt-1 block w-full rounded-lg border border-hairline bg-canvas px-3 py-2 text-sm text-ink" placeholder="Nhập mã trên thiết bị" />
            </label>
          ) : (
            <label className="text-xs font-medium text-muted">
              Địa chỉ MAC
              <input required value={macAddress} onChange={(event) => setMacAddress(event.target.value)} className="mt-1 block w-full rounded-lg border border-hairline bg-canvas px-3 py-2 font-mono text-sm text-ink" placeholder="AA:BB:CC:DD:EE:FF" />
            </label>
          )}
          <label className="text-xs font-medium text-muted">
            Bể nuôi
            <select required value={terrariumId} onChange={(event) => setTerrariumId(event.target.value)} className="mt-1 block w-full rounded-lg border border-hairline bg-canvas px-3 py-2 text-sm text-ink">
              <option value="">Chọn bể nuôi</option>
              {(terrariums.data?.items ?? []).map((terrarium) => <option key={terrarium.id} value={terrarium.id}>{terrarium.name}</option>)}
            </select>
          </label>
          <button disabled={busy || !terrariumId} className="self-end rounded-lg bg-brand px-4 py-2 text-sm font-semibold text-white disabled:opacity-50">Nhận thiết bị</button>
          {terrariums.error && <p className="text-xs text-critical sm:col-span-3">{describeError(t, terrariums.error)}</p>}
        </form>
      )}

      {secret && (
        <div role="status" className="rounded-xl border border-warning/40 bg-warning/5 p-4 text-sm text-ink">
          <strong>Khóa thiết bị chỉ hiển thị một lần:</strong>
          <code className="ml-2 break-all font-mono">{secret}</code>
          <button type="button" className="ml-3 text-xs text-accent underline" onClick={() => setSecret(null)}>Đã lưu</button>
        </div>
      )}
      {error && <p role="alert" className="rounded-lg border border-critical/30 bg-critical/5 p-3 text-sm text-critical">{describeError(t, error)}</p>}
      {devices.error && <p role="alert" className="rounded-lg border border-critical/30 bg-critical/5 p-3 text-sm text-critical">{describeError(t, devices.error)}</p>}

      <div className="flex flex-col gap-3 rounded-xl border border-hairline bg-surface p-4 md:flex-row">
        <label className="relative flex-1">
          <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted" aria-hidden="true" />
          <input value={searchTerm} onChange={(event) => setSearchTerm(event.target.value)} placeholder="Tìm mã thiết bị, tên, MAC hoặc bể nuôi" className="w-full rounded-lg border border-hairline bg-canvas py-2 pl-9 pr-3 text-sm text-ink" />
        </label>
        <select value={statusFilter} onChange={(event) => setStatusFilter(event.target.value)} className="rounded-lg border border-hairline bg-canvas px-3 py-2 text-sm text-ink">
          <option value="all">Tất cả trạng thái</option>
          <option value="online">Trực tuyến</option>
          <option value="offline">Ngoại tuyến</option>
        </select>
      </div>

      {devices.loading && !devices.data ? <p className="p-6 text-center text-sm text-muted">Đang tải thiết bị…</p> : null}
      {!devices.loading && !devices.error && items.length === 0 ? <p className="rounded-xl border border-hairline bg-surface p-8 text-center text-sm text-muted">Chưa có thiết bị nào được kết nối.</p> : null}

      <div className="overflow-x-auto rounded-xl border border-hairline bg-surface">
        <table className="w-full min-w-[900px] text-left text-sm">
          <thead className="border-b border-hairline bg-raised text-xs uppercase text-muted">
            <tr>
              <th className="px-4 py-3">Thiết bị</th><th className="px-4 py-3">Bể nuôi</th><th className="px-4 py-3">Trạng thái</th><th className="px-4 py-3">Firmware</th><th className="px-4 py-3">Chu kỳ lấy mẫu</th><th className="px-4 py-3">Lần cuối kết nối</th><th className="px-4 py-3">Thao tác</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-hairline">
            {filtered.map((device) => (
              <tr key={device.deviceId} className="hover:bg-raised/50">
                <td className="px-4 py-3">
                  <button type="button" onClick={() => void showDevice(device)} className="flex items-center gap-3 text-left">
                    <Cpu className="h-4 w-4 text-brand" aria-hidden="true" />
                    <span><strong className="block text-ink">{device.deviceName}</strong><small className="font-mono text-muted">{device.deviceId}</small></span>
                  </button>
                </td>
                <td className="px-4 py-3 text-muted">{device.terrariumName ?? 'Chưa gán'}</td>
                <td className="px-4 py-3">
                  <span className="inline-flex items-center gap-1.5 text-xs text-ink">
                    {device.runtime.toLowerCase() === 'online' ? <Wifi className="h-4 w-4 text-accent" aria-hidden="true" /> : <WifiOff className="h-4 w-4 text-warning" aria-hidden="true" />}
                    {device.runtime === 'online' ? 'Trực tuyến' : device.runtime === 'offline' ? 'Ngoại tuyến' : device.status}
                  </span>
                </td>
                <td className="px-4 py-3 font-mono text-muted">{device.firmwareVersion ?? '—'}</td>
                <td className="px-4 py-3 text-muted">{device.samplingIntervalSec}s</td>
                <td className="px-4 py-3 text-muted">{formatDate(device.lastSeenAt)}</td>
                <td className="px-4 py-3">
                  <div className="flex flex-wrap gap-2">
                    {canWrite && <button type="button" onClick={() => startEdit(device)} className="inline-flex items-center gap-1 rounded-lg border border-hairline px-2.5 py-1.5 text-xs text-ink hover:bg-raised"><Pencil className="h-3.5 w-3.5" aria-hidden="true" />Sửa</button>}
                    {canWrite && <button type="button" disabled={busy} onClick={() => removeDevice(device)} className="inline-flex items-center gap-1 rounded-lg border border-critical/40 px-2.5 py-1.5 text-xs text-critical hover:bg-critical/5 disabled:opacity-50"><Trash2 className="h-3.5 w-3.5" aria-hidden="true" />Xóa</button>}
                    {canProvision && <button type="button" disabled={busy} onClick={() => void runAction(async () => { const result = await api.rotateDeviceSecret(device.deviceId); setSecret(result.secret); })} className="rounded-lg border border-hairline px-2.5 py-1.5 text-xs text-accent hover:bg-raised disabled:opacity-50">Đổi khóa</button>}
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
        {filtered.length === 0 && items.length > 0 ? <p className="p-8 text-center text-sm text-muted">Không tìm thấy thiết bị phù hợp.</p> : null}
      </div>

      {selected && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4" role="presentation" onMouseDown={(event) => { if (event.target === event.currentTarget) setSelected(null); }}>
          <section role="dialog" aria-modal="true" aria-labelledby="device-dialog-title" className="max-h-[90vh] w-full max-w-xl overflow-y-auto rounded-2xl border border-hairline bg-surface p-5 shadow-xl">
            <div className="mb-4 flex items-start justify-between gap-3">
              <div><h2 id="device-dialog-title" className="font-heading text-lg font-bold text-ink">{editing ? 'Cấu hình thiết bị' : selected.deviceName}</h2><p className="font-mono text-xs text-muted">{selected.deviceId}</p></div>
              <button type="button" onClick={() => { setSelected(null); setEditing(false); }} className="text-sm text-muted hover:text-ink">Đóng</button>
            </div>
            {editing ? (
              <form onSubmit={(event) => void submitEdit(event)} className="space-y-3">
                <label className="block text-xs text-muted">Tên thiết bị<input maxLength={60} value={deviceName} onChange={(event) => setDeviceName(event.target.value)} className="mt-1 w-full rounded-lg border border-hairline bg-canvas px-3 py-2 text-sm text-ink" /></label>
                <label className="block text-xs text-muted">Chu kỳ lấy mẫu (10–300 giây)<input required type="number" min={10} max={300} value={samplingIntervalSec} onChange={(event) => setSamplingIntervalSec(Number(event.target.value))} className="mt-1 w-full rounded-lg border border-hairline bg-canvas px-3 py-2 text-sm text-ink" /></label>
                <label className="block text-xs text-muted">Chu kỳ gửi dữ liệu (10–300 giây)<input required type="number" min={10} max={300} value={publishIntervalSec} onChange={(event) => setPublishIntervalSec(Number(event.target.value))} className="mt-1 w-full rounded-lg border border-hairline bg-canvas px-3 py-2 text-sm text-ink" /></label>
                {canProvision && (
                  <div className="rounded-lg border border-hairline bg-raised/40 p-3">
                    <p className="text-xs text-muted">
                      Bể nuôi hiện tại: <strong className="text-ink">{selected.terrariumName ?? 'Chưa gán'}</strong>
                    </p>
                    <div className="mt-2 flex flex-wrap items-end gap-2">
                      <label className="flex-1 text-xs text-muted">
                        Thêm vào bể nuôi
                        <select value={moveTerrariumId} onChange={(event) => setMoveTerrariumId(event.target.value)} className="mt-1 block w-full rounded-lg border border-hairline bg-canvas px-3 py-2 text-sm text-ink">
                          <option value="">Chọn bể nuôi</option>
                          {(terrariums.data?.items ?? []).map((terrarium) => (
                            <option key={terrarium.id} value={terrarium.id}>{terrarium.name}</option>
                          ))}
                        </select>
                      </label>
                      <button type="button" disabled={busy || !moveTerrariumId} onClick={() => void bindToTerrarium(selected)} className="rounded-lg border border-brand/40 px-3 py-2 text-xs font-semibold text-brand disabled:opacity-50">
                        Thêm vào bể nuôi
                      </button>
                    </div>
                    <p className="mt-2 text-[11px] text-muted">
                      Mỗi bể nuôi chỉ chứa một thiết bị: thiết bị đang ở bể đó (nếu có) sẽ được gỡ ra và có thể gắn lại sau.
                    </p>
                    {terrariums.error && <p className="mt-2 text-xs text-critical">{describeError(t, terrariums.error)}</p>}
                  </div>
                )}
                <div className="flex justify-end gap-2"><button type="button" onClick={() => setEditing(false)} className="rounded-lg border border-hairline px-3 py-2 text-sm text-muted">Huỷ</button><button disabled={busy} className="rounded-lg bg-brand px-3 py-2 text-sm font-semibold text-white disabled:opacity-50">Lưu</button></div>
              </form>
            ) : (
              <dl className="grid grid-cols-2 gap-3 text-sm">
                <Detail label="Địa chỉ MAC" value={selected.macAddress} /><Detail label="Chip ID" value={selected.chipId} /><Detail label="Giao thức" value={selected.protocol} /><Detail label="Trạng thái thiết bị" value={selected.status} /><Detail label="Firmware" value={selected.firmwareVersion ?? '—'} /><Detail label="Chu kỳ gửi" value={`${selected.publishIntervalSec}s`} /><Detail label="Tín hiệu Wi-Fi" value={selected.signalStrengthDbm === null ? '—' : `${selected.signalStrengthDbm} dBm`} /><Detail label="Pin" value={selected.batteryPct === null ? '—' : `${selected.batteryPct}%`} />
                <div className="col-span-2 flex flex-wrap justify-end gap-2 border-t border-hairline pt-4">
                  {canProvision && <button type="button" disabled={busy} onClick={() => { if (window.confirm(`Thu hồi khóa của thiết bị ${selected.deviceName}? Thiết bị sẽ ngừng gửi dữ liệu.`)) void runAction(async () => { await api.revokeDevice(selected.deviceId); setSelected(await api.getDevice(selected.deviceId)); }); }} className="rounded-lg border border-warning/40 px-3 py-2 text-xs text-warning">Thu hồi khóa</button>}
                  {canWrite && <button type="button" disabled={busy} onClick={() => { if (window.confirm(`Gỡ thiết bị ${selected.deviceName} khỏi tài khoản?`)) void runAction(async () => { await api.deleteDevice(selected.deviceId); setSelected(null); }); }} className="rounded-lg border border-critical/40 px-3 py-2 text-xs text-critical">Gỡ thiết bị</button>}
                </div>
              </dl>
            )}
            {error && <p role="alert" className="mt-3 text-sm text-critical">{describeError(t, error)}</p>}
          </section>
        </div>
      )}
    </div>
  );
};

const Detail: React.FC<{ label: string; value: string }> = ({ label, value }) => (
  <div className="rounded-lg border border-hairline p-3"><dt className="text-xs text-muted">{label}</dt><dd className="mt-1 break-all text-ink">{value}</dd></div>
);
