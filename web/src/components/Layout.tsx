import React, { useState } from 'react';
import { NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom';
import {
  AlertTriangle,
  Box,
  Cpu,
  History,
  KeyRound,
  LayoutDashboard,
  Loader2,
  LogOut,
  Menu,
  MonitorPlay,
  ServerCog,
  Settings,
  ShieldCheck,
  User,
  X,
} from 'lucide-react';
import { apiBaseUrl, ApiError } from '../api/client';
import * as auth from '../api/endpoints';
import { useI18n } from '../i18n';
import { LANGUAGES } from '../i18n/strings';
import { useSession } from '../state/session';
import { describeError, fieldMessages } from '../lib/messages';
import { MockDataNotice } from './MockDataNotice';

/**
 * The shell. Everything in it is either the session's or the API's — the prototype's hard-coded
 * "Admin Quản Lý / admin@terraguard.vn" and its invented alert badge are gone, because a role the page invents is
 * a permission decision the page has no business making (`02-design/06` §3).
 *
 * A route guard lives one level up in `App.tsx`; this component can assume a session exists and still reads it
 * defensively, because a session can be dropped from inside a request (a rotation failure) while the shell is
 * mounted.
 *
 * The three screens still on `data/mockData.ts` — Devices, Alerts, Settings — keep the mock-data notice. They
 * cannot be wired yet (roadmap 4.20: their endpoints do not exist), and the honest thing to do with a screen that
 * renders invented values is to say so on that screen. The notice therefore follows the route instead of being
 * rendered globally, so a wired screen never apologises for data it did not invent.
 */

const MOCK_ROUTES = ['/devices', '/alerts', '/settings'];

export const Layout: React.FC = () => {
  const { t, lang, setLang } = useI18n();
  const { user, signOut } = useSession();
  const navigate = useNavigate();
  const location = useLocation();
  const [sidebarOpen, setSidebarOpen] = useState(false);
  const [accountOpen, setAccountOpen] = useState(false);
  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  const online = navigator.onLine;
  const showsMockNotice = MOCK_ROUTES.some((route) => location.pathname.startsWith(route));

  const navItems = [
    { name: t('navDashboard'), path: '/dashboard', icon: LayoutDashboard },
    { name: t('navTerrariums'), path: '/terrariums', icon: Box },
    { name: t('navDevices'), path: '/devices', icon: Cpu },
    { name: t('navAlerts'), path: '/alerts', icon: AlertTriangle },
    { name: t('navHistory'), path: '/history', icon: History },
    { name: t('navSettings'), path: '/settings', icon: Settings },
    { name: t('navSystem'), path: '/system', icon: ServerCog },
    { name: t('navWallboard'), path: '/wallboard', icon: MonitorPlay },
  ];

  const handleSignOut = async () => {
    await signOut();
    navigate('/', { replace: true });
  };

  const submitPasswordChange = async (event: React.FormEvent) => {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await auth.changePassword({ currentPassword, newPassword });
      // The server revoked every session including this one, so staying on a page that 401s on the next click
      // would be a lie. The confirmation is handed to the sign-in screen through router state.
      await signOut();
      navigate('/', { replace: true, state: { notice: t('passwordChanged') } });
    } catch (cause) {
      setError(cause instanceof ApiError ? cause : new ApiError('unknown', 0, null));
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="flex min-h-screen flex-col bg-canvas text-ink md:flex-row">
      {sidebarOpen ? (
        <div
          className="fixed inset-0 z-40 bg-black/60 backdrop-blur-xs md:hidden"
          onClick={() => setSidebarOpen(false)}
        />
      ) : null}

      <aside
        className={`fixed top-0 z-50 flex h-screen w-64 flex-col border-r border-hairline bg-surface transition-transform duration-300 ease-in-out md:sticky ${
          sidebarOpen ? 'translate-x-0' : '-translate-x-full md:translate-x-0'
        }`}
      >
        <div className="flex h-18 items-center justify-between border-b border-hairline px-6">
          <NavLink to="/dashboard" className="flex items-center gap-3">
            <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-gradient-to-br from-brand to-[#204930] shadow-lg shadow-brand/20">
              <ShieldCheck className="h-6 w-6 text-ink" aria-hidden="true" />
            </div>
            <div>
              <span className="block font-heading text-xl font-extrabold tracking-wider text-ink">
                VIVARIUM<span className="text-brand">GUARD</span>
              </span>
              <span className="-mt-1 block text-[10px] uppercase tracking-widest text-muted">
                {t('appTitle')}
              </span>
            </div>
          </NavLink>
          <button
            onClick={() => setSidebarOpen(false)}
            className="p-1 text-muted hover:text-ink md:hidden"
            aria-label={t('cancel')}
          >
            <X className="h-5 w-5" />
          </button>
        </div>

        <nav className="flex-1 space-y-1.5 overflow-y-auto px-4 py-6">
          {navItems.map((item) => {
            const Icon = item.icon;
            return (
              <NavLink
                key={item.path}
                to={item.path}
                onClick={() => setSidebarOpen(false)}
                className={({ isActive }) =>
                  `flex items-center gap-3 rounded-xl px-3.5 py-3 text-sm font-medium transition-all ${
                    isActive
                      ? 'bg-brand font-semibold text-white shadow-md shadow-brand/20'
                      : 'text-muted hover:bg-raised hover:text-ink'
                  }`
                }
              >
                <Icon className="h-5 w-5 shrink-0" aria-hidden="true" />
                <span>{item.name}</span>
              </NavLink>
            );
          })}
        </nav>

        <div className="border-t border-hairline bg-field/50 p-4">
          {/* The account block. The name and role are the server's (`GET /auth/me`), never a literal. */}
          <div className="mb-2 flex items-center gap-3 rounded-xl bg-raised/60 p-2">
            <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full border border-brand/40 bg-brand/20 text-brand">
              <User className="h-5 w-5" aria-hidden="true" />
            </div>
            <div className="min-w-0">
              <p className="truncate text-xs font-semibold text-ink">{user?.username ?? '—'}</p>
              <p className="truncate text-[11px] text-muted">
                {user?.email ?? '—'}
                {user?.role ? ` · ${user.role}` : ''}
              </p>
            </div>
          </div>

          <button
            type="button"
            onClick={() => setAccountOpen((open) => !open)}
            aria-expanded={accountOpen}
            className="flex w-full items-center justify-center gap-2 rounded-lg px-3 py-2 text-xs font-medium text-ink transition-colors hover:bg-raised"
          >
            <KeyRound className="h-4 w-4" aria-hidden="true" />
            <span>{t('changePassword')}</span>
          </button>

          {accountOpen ? (
            <form onSubmit={submitPasswordChange} className="mt-3 space-y-2">
              <p className="text-[11px] text-muted">{t('changePasswordPrompt')}</p>
              <input
                type="password"
                value={currentPassword}
                onChange={(event) => setCurrentPassword(event.target.value)}
                placeholder={t('currentPassword')}
                autoComplete="current-password"
                required
                className="w-full rounded-lg border border-hairline bg-field px-3 py-2 text-xs text-ink outline-none focus:border-brand"
              />
              <input
                type="password"
                value={newPassword}
                onChange={(event) => setNewPassword(event.target.value)}
                placeholder={t('newPassword')}
                autoComplete="new-password"
                required
                className="w-full rounded-lg border border-hairline bg-field px-3 py-2 text-xs text-ink outline-none focus:border-brand"
              />
              <p className="text-[11px] text-muted">{t('passwordHint')}</p>
              {error ? (
                <div className="rounded-lg border border-critical/40 bg-critical/10 px-2.5 py-2 text-[11px] text-ink">
                  <p>{describeError(t, error)}</p>
                  {fieldMessages(t, error.errors).map((message) => (
                    <p key={message}>{message}</p>
                  ))}
                </div>
              ) : null}
              <button
                type="submit"
                disabled={busy}
                className="flex w-full items-center justify-center gap-2 rounded-lg bg-brand px-3 py-2 text-xs font-medium text-white transition-colors hover:bg-brand-dark disabled:opacity-70"
              >
                {busy ? (
                  <>
                    <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />
                    <span>{t('changingPassword')}</span>
                  </>
                ) : (
                  <span>{t('changePasswordSubmit')}</span>
                )}
              </button>
            </form>
          ) : null}

          <button
            type="button"
            onClick={() => void handleSignOut()}
            className="mt-2 flex w-full items-center justify-center gap-2 rounded-lg border border-transparent px-3 py-2 text-xs font-medium text-critical transition-colors hover:border-critical/20 hover:bg-critical/10"
          >
            <LogOut className="h-4 w-4" aria-hidden="true" />
            <span>{t('signOut')}</span>
          </button>
        </div>
      </aside>

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="sticky top-0 z-30 flex h-18 items-center justify-between border-b border-hairline bg-surface/80 px-4 backdrop-blur-md sm:px-8">
          <button
            onClick={() => setSidebarOpen(true)}
            className="rounded-lg bg-raised p-2 text-ink hover:bg-hairline md:hidden"
            aria-label={t('navDashboard')}
          >
            <Menu className="h-5 w-5" />
          </button>

          <div className="flex items-center gap-3 sm:gap-5">
            {/* Browser connectivity, which is not the same claim as "the API answered" — the pages say that. */}
            <div className="hidden items-center gap-2 rounded-full border border-hairline bg-canvas px-3 py-1.5 text-xs sm:flex">
              <span
                className={`h-2 w-2 rounded-full ${online ? 'bg-inrange' : 'bg-unknown'}`}
                aria-hidden="true"
              />
              <span className="text-muted">{t('navLive')}</span>
              <span className="font-mono text-[11px] text-muted">{apiBaseUrl}</span>
            </div>

            <div className="flex items-center gap-1">
              {LANGUAGES.map((option) => (
                <button
                  key={option}
                  type="button"
                  onClick={() => setLang(option)}
                  aria-pressed={lang === option}
                  className={`rounded-lg px-2.5 py-1 text-xs transition-colors ${
                    lang === option ? 'bg-raised text-ink' : 'text-muted hover:text-ink'
                  }`}
                >
                  {option.toUpperCase()}
                </button>
              ))}
            </div>
          </div>
        </header>

        <main className="mx-auto w-full max-w-7xl flex-1 p-4 sm:p-8">
          {showsMockNotice ? (
            <div className="mb-6">
              <MockDataNotice />
            </div>
          ) : null}
          <Outlet />
        </main>
      </div>
    </div>
  );
};
