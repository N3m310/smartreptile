import {
  apiBase, clearSession, failureKind, forgotPassword, getJson, getReadiness, hasSession, login, recover,
  resetPassword,
} from '../api.js?v=0.1.5';
import { createStore, freshnessOf, formatAge, formatValue } from '../store.js';
import { metricName, statusClass, statusLabel, t } from '../i18n.js?v=0.1.5';

// Live dashboard page (W2 in docs/02-design/04 §1.2).
//
// M2 scope: sign-in, password recovery, terrarium selection, latest values with honest staleness, readiness
// banner. The history page, alert table and threshold editor arrive with their milestones.

const store = createStore();
let activeTerrariumId = null;
let refreshTimer = null;

// A server that answers is not an unreachable server. The M1 snapshot pass caught this: `/api/v1/terrariums`
// returns 404 because those routes are M2 work, and calling that "cannot reach the server" told the keeper
// something false about the state of the system (docs/06-report/snapshots/README.md).
function bannerFor(error) {
  // A missing session is not a failed request: say which one it is. An expired token is answered transparently by
  // api.js (one rotation), so a 401 that reaches here means the refresh token is spent too.
  if (error?.status === 401) {
    return { className: 'banner warn', text: t(hasSession() ? 'sessionExpired' : 'signInPrompt') };
  }

  switch (failureKind(error)) {
    case 'unreachable':
      return { className: 'banner error', text: t('errorBackendUnreachable') };
    case 'missing-endpoint':
      return { className: 'banner warn', text: `${t('errorEndpointMissing')} (${error.code})` };
    case 'not-found':
      return { className: 'banner warn', text: `${t('errorNotFound')} (${error.code})` };
    default:
      return { className: 'banner error', text: `${t('errorRequestFailed')} (${error.code})` };
  }
}

function renderBanner(state) {
  const banner = document.getElementById('banner');
  const readiness = state.readiness;

  if (state.error) {
    const { className, text } = bannerFor(state.error);
    banner.className = className;
    banner.textContent = text;
    banner.hidden = false;
    return;
  }

  if (readiness && readiness.status !== 'Healthy') {
    // A missing readiness endpoint is not a failing database: say which one it is.
    if (readiness.status === 'MissingEndpoint') {
      banner.className = 'banner warn';
      banner.textContent = t('readinessMissing');
      banner.hidden = false;
      return;
    }

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

    // Signed out: readiness is still worth showing, but there is no token to read a terrarium with.
    if (!hasSession() || !activeTerrariumId) {
      return;
    }

    const payload = await getJson(`/api/v1/terrariums/${activeTerrariumId}/readings/latest`);
    store.setLatest(activeTerrariumId, payload);
  } catch (error) {
    // Cached values stay on screen: a monitoring dashboard that blanks itself during a blip is worse than useless.
    handleFailure(error);
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
    handleFailure(error);
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

// ---- session ------------------------------------------------------------------------------------------
//
// Every terrarium route is authenticated and scoped to the caller (BR-02.2), so the page cannot show a value
// before it holds a token. The sign-in form belongs to the Live page; the wallboard has no controls and explains
// what is missing instead.

const authPanel = document.getElementById('auth');
const signInForm = document.getElementById('sign-in');
const recoverForm = document.getElementById('recover');
const controls = document.getElementById('controls');
const signOutButton = document.getElementById('sign-out');
const authMessage = document.getElementById('auth-message');
const recoverMessage = document.getElementById('recover-message');
const recoverResult = document.getElementById('recover-result');
const recoverCode = document.getElementById('recover-code');
const recoverHint = document.getElementById('recover-hint');
const sendResetCodeButton = document.getElementById('send-reset-code');

/**
 * Which of the two kinds of code the form is asking for. Only the keeper knows whether they still hold the backup
 * code from registration, so the mode is switched by the button that requests a reset code and never guessed from
 * what was typed: the label above the field is the contract, and the submit handler follows it.
 */
let recoveryCodeKind = 'backup';

/** Shows or hides the whole auth panel; a session means there is nothing left to sign in to. */
function showSignIn(visible) {
  if (!authPanel) {
    return;
  }

  authPanel.hidden = !visible;
  controls.hidden = visible;
  signOutButton.hidden = visible;

  if (visible) {
    showForm('sign-in');
  }
}

/** Switches between the two forms inside the panel. */
function showForm(name) {
  if (!authPanel) {
    return;
  }

  authPanel.hidden = false;
  signInForm.hidden = name !== 'sign-in';
  recoverForm.hidden = name !== 'recover';

  if (name === 'recover') {
    setRecoveryCodeKind('backup');
  }
}

/** Relabels the code field for the kind of code being asked for, so the two are never confused. */
function setRecoveryCodeKind(kind) {
  recoveryCodeKind = kind;
  document.getElementById('recover-code-label').textContent =
    t(kind === 'issued' ? 'resetCodeLabel' : 'recoveryCode');
  recoverHint.textContent = t(kind === 'issued' ? 'recoverHintIssued' : 'recoverHintBackup');
  recoverCode.value = '';
}

/** The sentence a failed recovery deserves, decided in one place so the codes stay out of the markup. */
function recoverMessageFor(error) {
  switch (error?.code) {
    case 'invalid_recovery_code':
      return t('recoveryFailed');
    case 'password_policy_violation':
      return t('passwordPolicyViolation');
    case 'account_locked':
    case 'ip_blocked':
      return t('accountLocked');
    default:
      return `${t('errorRequestFailed')} (${error?.code ?? 'unknown'})`;
  }
}

/**
 * Reports a failed call. A 401 gets a second treatment on purpose: api.js has already tried to rotate the session,
 * so a 401 that reaches here means the token is gone — show the form rather than leaving the keeper with a banner
 * and nothing to act on.
 */
function handleFailure(error) {
  store.setError(error);

  if (error?.status === 401) {
    showSignIn(true);
  }
}

/** Loads everything a signed-in page needs, in the order the data depends on. */
async function start() {
  showSignIn(false);
  activeTerrariumId = null;
  await loadTerrariums();
  await refresh();
}

if (authPanel) {
  document.getElementById('auth-prompt').textContent = t('signInPrompt');
  document.getElementById('auth-username-label').textContent = t('usernameOrEmail');
  document.getElementById('auth-password-label').textContent = t('password');
  document.getElementById('sign-in-button').textContent = t('signIn');
  document.getElementById('show-recover').textContent = t('forgotPassword');
  signOutButton.textContent = t('signOut');

  document.getElementById('recover-prompt').textContent = t('recoverPrompt');
  document.getElementById('recover-identifier-label').textContent = t('usernameOrEmail');
  document.getElementById('recover-password-label').textContent = t('newPassword');
  document.getElementById('recover-button').textContent = t('recoverButton');
  document.getElementById('show-sign-in').textContent = t('backToSignIn');
  sendResetCodeButton.textContent = t('sendResetCode');
  setRecoveryCodeKind('backup');

  signInForm.addEventListener('submit', async (event) => {
    event.preventDefault();
    authMessage.textContent = t('signingIn');

    try {
      const user = await login(
        document.getElementById('username').value.trim(),
        document.getElementById('password').value);

      document.getElementById('password').value = '';
      authMessage.textContent = `${t('signedInAs')} ${user.username}`;
      await start();
    } catch (error) {
      authMessage.textContent = error.code === 'invalid_credentials'
        ? t('invalidCredentials')
        : `${t('errorRequestFailed')} (${error.code})`;
    }
  });

  document.getElementById('show-recover').addEventListener('click', () => {
    // Carry the identifier across: someone who has just failed to sign in has already typed it.
    document.getElementById('recover-identifier').value = document.getElementById('username').value.trim();
    recoverMessage.textContent = '';
    recoverResult.hidden = true;
    showForm('recover');
    document.getElementById('recover-code').focus();
  });

  document.getElementById('show-sign-in').addEventListener('click', () => {
    showForm('sign-in');
    document.getElementById('username').focus();
  });

  sendResetCodeButton.addEventListener('click', async () => {
    // Only the identifier matters here — the keeper has no code yet, which is why they are pressing this.
    if (!document.getElementById('recover-identifier').reportValidity()) {
      return;
    }

    recoverMessage.textContent = t('sendingResetCode');
    recoverResult.hidden = true;
    sendResetCodeButton.disabled = true;

    try {
      await forgotPassword(document.getElementById('recover-identifier').value.trim());

      // The server answers the same way whether or not the account exists, so this message has to as well. Where
      // the code actually went is the one difference, and this deployment states that once, here, rather than
      // implying anything about the account.
      setRecoveryCodeKind('issued');
      recoverMessage.textContent = `${t('resetCodeRequested')} ${t('resetCodeLogHint')}`;
      recoverCode.focus();
    } catch (error) {
      recoverMessage.textContent = recoverMessageFor(error);
    } finally {
      sendResetCodeButton.disabled = false;
    }
  });

  recoverForm.addEventListener('submit', async (event) => {
    event.preventDefault();
    recoverMessage.textContent = t('recovering');
    recoverResult.hidden = true;

    const identifier = document.getElementById('recover-identifier').value.trim();
    const code = recoverCode.value;
    const password = document.getElementById('recover-password').value;

    try {
      // The two kinds of code are different credentials with different endpoints, so which one was typed decides
      // which call is made. A backup code presented as a reset code is simply refused, which is the honest answer.
      const replacement = recoveryCodeKind === 'issued'
        ? await resetPassword(identifier, code, password)
        : await recover(identifier, code, password);

      // The consumed code is spent and the replacement is shown exactly once, so it stays on screen instead of
      // flashing past: losing it would leave the account with no way back, which is worse than an ugly page.
      recoverMessage.textContent = t('recoveryDone');
      recoverResult.textContent = `${t('recoveryIssued')} ${replacement}`;
      recoverResult.hidden = false;

      recoverCode.value = '';
      document.getElementById('recover-password').value = '';
      document.getElementById('username').value = identifier;
      document.getElementById('password').value = '';
      setRecoveryCodeKind('backup');
    } catch (error) {
      recoverMessage.textContent = recoverMessageFor(error);
    }
  });

  signOutButton.addEventListener('click', () => {
    clearSession();
    activeTerrariumId = null;
    store.setTerrariums([]);
    document.getElementById('terrarium').innerHTML = '';
    document.getElementById('device-header').textContent = '';
    document.getElementById('cards').innerHTML = '';
    authMessage.textContent = '';
    showSignIn(true);
  });
}

// 30 s polling is the documented degraded mode (UC-02 A1); the SignalR push path lands in M2.
if (hasSession()) {
  start();
} else {
  document.getElementById('cards').innerHTML = '';
  showSignIn(true);
}

refreshTimer = setInterval(refresh, 30000);
window.addEventListener('beforeunload', () => clearInterval(refreshTimer));
