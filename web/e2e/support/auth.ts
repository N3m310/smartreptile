import { resolve } from 'node:path';
import { expect, type Page } from '@playwright/test';

export const authStatePath = (role: 'owner' | 'technician' | 'viewer' | 'admin'): string =>
  resolve(process.cwd(), 'test-results', '.auth', `${role}.json`);

export interface Credentials {
  username: string;
  password: string;
}

export const demoAccounts = {
  owner: {
    username: process.env.E2E_OWNER_USERNAME ?? 'demo.owner',
    password: process.env.E2E_OWNER_PASSWORD ?? 'Demo!Reptile1',
  },
  technician: {
    username: process.env.E2E_TECHNICIAN_USERNAME ?? 'demo.technician',
    password: process.env.E2E_TECHNICIAN_PASSWORD ?? 'Demo!Reptile1',
  },
  viewer: {
    username: process.env.E2E_VIEWER_USERNAME ?? 'demo.viewer',
    password: process.env.E2E_VIEWER_PASSWORD ?? 'Demo!Reptile1',
  },
};

export async function signIn(page: Page, credentials: Credentials): Promise<void> {
  await page.goto('/');
  await page.locator('input[autocomplete="username"]').fill(credentials.username);
  await page.locator('input[autocomplete="current-password"]').fill(credentials.password);
  await page.getByRole('button', { name: /sign in|đăng nhập/i }).click();
  await expect(page).toHaveURL(/\/dashboard$/);
  await expect(page.locator('main')).toBeVisible();
}

export async function openSignedInApp(page: Page): Promise<void> {
  await page.goto('/dashboard');
  await expect(page.locator('main')).toBeVisible();
}

export async function readAccessToken(page: Page): Promise<string> {
  const storedSession = await page.evaluate(() => localStorage.getItem('terraguard.session'));
  if (!storedSession) {
    throw new Error('The signed-in browser session is missing from local storage.');
  }

  const session: unknown = JSON.parse(storedSession);
  if (
    typeof session !== 'object' ||
    session === null ||
    !('accessToken' in session) ||
    typeof session.accessToken !== 'string'
  ) {
    throw new Error('The signed-in browser session does not contain an access token.');
  }

  return session.accessToken;
}
