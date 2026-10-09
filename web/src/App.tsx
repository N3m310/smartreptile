import React from 'react';
import { BrowserRouter, Navigate, Outlet, Route, Routes } from 'react-router-dom';
import { Layout } from './components/Layout';
import { I18nProvider } from './i18n';
import { SessionProvider, useSession } from './state/session';
import { Login } from './pages/Login';
import { Dashboard } from './pages/Dashboard';
import { Terrariums } from './pages/Terrariums';
import { TerrariumDetail } from './pages/TerrariumDetail';
import { Devices } from './pages/Devices';
import { Alerts } from './pages/Alerts';
import { History } from './pages/History';
import { Settings } from './pages/Settings';
import { Wallboard } from './pages/Wallboard';
import { System } from './pages/System';

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
              <Route path="/devices" element={<Devices />} />
              <Route path="/alerts" element={<Alerts />} />
              <Route path="/history" element={<History />} />
              <Route path="/settings" element={<Settings />} />
              <Route path="/system" element={<System />} />
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

export default App;

