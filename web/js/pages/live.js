import { apiBase, getJson, getReadiness } from '../api.js';
import { createStore, freshnessOf, formatAge, formatValue } from '../store.js';
import { metricName, statusClass, statusLabel, t } from '../i18n.js';

// Live dashboard page (W2 in docs/02-design/04 §1.2).
//
// M1 scope: terrarium selection, latest values with honest staleness, readiness banner. The history page,
// alert table and threshold editor arrive with the corresponding milestones.

const store = createStore();
let activeTerrariumId = null;
let refreshTimer = null;

function renderBanner(state) {
  const banner = document.getElementById('banner');
  const readiness = state.readiness;

  if (state.error) {
    banner.className = 'banner error';
    banner.textContent = state.error.isNetworkFailure
      ? t('errorBackendUnreachable')
      : `${t('errorBackendUnreachable')} (${state.error.code})`;
    banner.hidden = false;
    return;
  }

  if (readiness && readiness.status !== 'Healthy') {
    const failing = (readiness.checks ?? []).filter((check) => check.status !== 'Healthy').map((check) => check.name);
    banner.className = 'banner warn';
    banner.textContent = failing.includes('mqtt-broker') ? t('brokerDown') : t('databaseDown');
    banner.hidden = false;
    return;
  }

  banner.hidden = true;
}

function renderCards(state) {
  const container = document.getElementById('cards');

  if (!activeTerrariumId) {
    container.innerHTML = `<div class="banner warn">${t('emptyStateTitle')} — ${t('emptyStateBody')}</div>`;
    return;
  }

  const payload = state.latest[activeTerrariumId];

  if (!payload) {
    container.innerHTML = `<div class="banner warn">${t('statusNoData')}</div>`;
    return;
  }

  const interval = payload.device?.samplingIntervalSec ?? 60;

  container.innerHTML = (payload.metrics ?? [])
    .map((metric) => {
      const freshness = freshnessOf(metric.capturedAt, interval, metric.status);
      const stale = freshness.stale ? 'stale' : '';
      const band = metric.target
        ? `<div class="band">${t('band')} ${formatValue(metric.target.min, metric.code)}–${formatValue(metric.target.max, metric.code)} ${metric.unit}</div>`
        : '';

      return `
        <article class="card ${statusClass(metric.status)} ${stale}">
          <div class="metric-name">${metricName(metric.code)}</div>
          <div><span class="value">${formatValue(metric.value, metric.code)}</span><span class="unit">${metric.unit}</span></div>
          <div class="badge ${statusClass(metric.status)}">${statusLabel(metric.status)}</div>
          ${band}
          <div class="age">${t('lastUpdated')} ${formatAge(freshness.ageMs)}</div>
        </article>`;
    })
    .join('');
}

function renderHeader(state) {
  const header = document.getElementById('device-header');
  if (!activeTerrariumId) {
    header.textContent = '';
    return;
  }

  const device = state.latest[activeTerrariumId]?.device;
  if (!device) {
    header.textContent = '';
    return;
  }

  const online = device.status === 'online';
  header.textContent = `${device.deviceId} · ${online ? t('deviceOnline') : t('deviceOffline')}` +
    (device.firmwareVersion ? ` · fw ${device.firmwareVersion}` : '');
}

async function refresh() {
  try {
    const readiness = await getReadiness();
    store.setReadiness(readiness);

    if (!activeTerrariumId) {
      return;
    }

    const payload = await getJson(`/api/v1/terrariums/${activeTerrariumId}/readings/latest`);
    store.setLatest(activeTerrariumId, payload);
  } catch (error) {
    // Cached values stay on screen: a monitoring dashboard that blanks itself during a blip is worse than useless.
    store.setError(error);
  }
}

async function loadTerrariums() {
  try {
    const payload = await getJson('/api/v1/terrariums');
    const list = payload.items ?? payload ?? [];
    store.setTerrariums(list);

    const select = document.getElementById('terrarium');
    select.innerHTML = list.map((item) => `<option value="${item.id}">${item.name}</option>`).join('');

    if (list.length > 0) {
      activeTerrariumId = list[0].id;
      select.value = activeTerrariumId;
    }
  } catch (error) {
    store.setError(error);
  }
}

store.subscribe((state) => {
  renderBanner(state);
  renderHeader(state);
  renderCards(state);
});

document.getElementById('terrarium').addEventListener('change', (event) => {
  activeTerrariumId = event.target.value;
  refresh();
});

document.getElementById('api-base-label').textContent = apiBase;

// 30 s polling is the documented degraded mode (UC-02 A1); the SignalR push path lands in M2.
loadTerrariums().then(refresh);
refreshTimer = setInterval(refresh, 30000);
window.addEventListener('beforeunload', () => clearInterval(refreshTimer));
