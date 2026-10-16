import React from 'react';
import { BrowserRouter, Navigate, Outlet, Route, Routes } from 'react-router-dom';
import { Layout } from './components/Layout';
import { I18nProvider } from './i18n';
import { SessionProvider, useSession } from './state/session';
import { PERMISSION_CODES } from './api/types';
import type { PermissionCode } from './api/types';
import { Login } from './pages/Login';
import { Dashboard } from './pages/Dashboard';
import { Terrariums } from './pages/Terrariums';
import { TerrariumDetail } from './pages/TerrariumDetail';
import { SpeciesProfiles } from './pages/SpeciesProfiles';
import { Devices } from './pages/Devices';
import { Alerts } from './pages/Alerts';
import { History } from './pages/History';
import { Settings } from './pages/Settings';
import { Wallboard } from './pages/Wallboard';
import { System } from './pages/System';
import { AdminOverview } from './pages/AdminOverview';
import { AdminAccounts } from './pages/AdminAccounts';
import { AdminRoles } from './pages/AdminRoles';

/**
 * Routes, providers and the guard.
 *
 * The guard is new, and it is the difference between a prototype and a client: the prototype's shell rendered for
 * anybody who typed `/dashboard`, and its pages were mock data so nothing leaked. Now every value behind that
 * route comes from the API, so an unauthenticated visit has to be answered with the sign-in form rather than with
 * a screen full of 401s.
 *
 * The wallboard is mounted *outside* `Layout`: it is a kiosk page (W1) with no navigation chrome, which is why it
 * is a sibling of the shell rather than a child of it.
 *
 * The administrator's three screens are the second guard, and it is a guard on a **permission** rather than on a
 * role: the API's `/users` routes name `user.read` and `/admin/*` names `data.read.any`, so the route refuses
 * exactly what the API would refuse, and a deployment that grants the permission to a role of its own gets a
 * working screen without a code change.
 */
export const App: React.FC = () => (
  <I18nProvider>
    <SessionProvider>
      <BrowserRouter>
        <Routes>
          {/* Public: the sign-in / register / recovery page. */}
          <Route path="/" element={<Login />} />

          {/* Everything else needs a session. */}
          <Route element={<RequireSession />}>
            <Route path="/wallboard" element={<Wallboard />} />
            <Route element={<Layout />}>
              <Route path="/dashboard" element={<Dashboard />} />
              <Route path="/terrariums" element={<Terrariums />} />
              <Route path="/terrariums/:id" element={<TerrariumDetail />} />
              <Route path="/species-profiles" element={<SpeciesProfiles />} />
              <Route path="/devices" element={<Devices />} />
              <Route path="/alerts" element={<Alerts />} />
              <Route path="/history" element={<History />} />
              <Route path="/settings" element={<Settings />} />
              <Route path="/system" element={<System />} />

              <Route element={<RequirePermission code={PERMISSION_CODES.dataReadAny} />}>
                <Route path="/admin" element={<AdminOverview />} />
              </Route>
              <Route element={<RequirePermission code={PERMISSION_CODES.userRead} />}>
                <Route path="/admin/accounts" element={<AdminAccounts />} />
                <Route path="/admin/roles" element={<AdminRoles />} />
              </Route>
            </Route>
          </Route>

          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </BrowserRouter>
    </SessionProvider>
  </I18nProvider>
);

/** Sends an unauthenticated visitor to the sign-in form. */
const RequireSession: React.FC = () => {
  const { session } = useSession();
  return session ? <Outlet /> : <Navigate to="/" replace />;
};

/**
 * Sends an account without `code` to the dashboard rather than rendering a screen whose every call would be
 * refused. The permission comes from the session, which takes it from `GET /auth/me` — never from `localStorage`.
 */
const RequirePermission: React.FC<{ code: PermissionCode }> = ({ code }) => {
  const { hasPermission } = useSession();
  return hasPermission(code) ? <Outlet /> : <Navigate to="/dashboard" replace />;
};

export default App;

