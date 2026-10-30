import { expect, test } from '@playwright/test';
import { demoAccounts, readAccessToken, signIn } from './support/auth';

test.describe('authentication', () => {
  test('shows a useful error for invalid credentials without leaving the sign-in page', async ({ page }) => {
    await page.goto('/');
    await page.locator('input[autocomplete="username"]').fill(`e2e-invalid-${Date.now()}@example.invalid`);
    await page.locator('input[autocomplete="current-password"]').fill('not-the-password');
    await page.getByRole('button', { name: /sign in|đăng nhập/i }).click();

    await expect(page.getByRole('alert')).toBeVisible();
    await expect(page).toHaveURL(/\/$/);
  });

  test('signs in and signs out through the application UI', async ({ page }) => {
    await signIn(page, demoAccounts.owner);

    await page.getByRole('button', { name: /sign out|đăng xuất/i }).click();

    await expect(page).toHaveURL(/\/$/);
    await expect(page.locator('input[autocomplete="username"]')).toBeVisible();
  });

  test('changes the sign-in language from the language selector', async ({ page }) => {
    await page.goto('/');
    await page.getByRole('button', { name: /english|tiếng anh/i }).click();

    await expect(page.getByRole('heading', { name: 'Sign in' })).toBeVisible();
    await expect(page.locator('input[autocomplete="username"]')).toBeVisible();
  });

  test('redirects an unauthenticated visit to a protected page to sign-in', async ({ page }) => {
    await page.goto('/devices');

    await expect(page).toHaveURL(/\/$/);
    await expect(page.locator('input[autocomplete="username"]')).toBeVisible();
  });

  test('registers a disposable account, signs in, and removes its access', async ({ page, request }) => {
    const suffix = `${Date.now()}${Math.floor(Math.random() * 1000)}`;
    const credentials = {
      username: `e2e${suffix}`,
      email: `e2e${suffix}@example.invalid`,
      password: 'E2e!Reptile1',
    };
    const recoveredPassword = 'E2e!Recovered1';
    const apiBaseUrl = process.env.E2E_API_BASE_URL ?? 'http://127.0.0.1:8080';
    let registered = false;
    let accessToken: string | undefined;
    let activePassword = credentials.password;

    try {
      await page.goto('/');
      await page.getByRole('button', { name: /create account|tạo tài khoản/i }).click();
      await page.locator('input[autocomplete="username"]').fill(credentials.username);
      await page.locator('input[autocomplete="email"]').fill(credentials.email);
      await page.locator('input[autocomplete="new-password"]').fill(credentials.password);

      const registrationResponse = page.waitForResponse((response) =>
        response.request().method() === 'POST' &&
        new URL(response.url()).pathname.endsWith('/api/v1/auth/register')
      );
      await page.locator('form button[type="submit"]').click();
      const response = await registrationResponse;
      expect(response.status(), `Registration returned HTTP ${response.status()}`).toBe(202);
      registered = true;

      await expect(page.locator('code')).toBeVisible();
      const recoveryCode = (await page.locator('code').innerText()).trim();
      expect(recoveryCode).not.toBe('');
      await page.getByRole('button', { name: /forgot password|quên mật khẩu/i }).click();
      await page.locator('input[autocomplete="username"]').fill(credentials.username);
      await page.locator('input[autocomplete="one-time-code"]').fill(recoveryCode);
      await page.locator('input[autocomplete="new-password"]').fill(recoveredPassword);

      const recoveryResponse = page.waitForResponse((currentResponse) =>
        currentResponse.request().method() === 'POST' &&
        new URL(currentResponse.url()).pathname.endsWith('/api/v1/auth/recover')
      );
      await page.locator('form button[type="submit"]').click();
      const recovery = await recoveryResponse;
      expect(recovery.ok(), `Account recovery returned HTTP ${recovery.status()}`).toBeTruthy();
      activePassword = recoveredPassword;

      await signIn(page, { username: credentials.username, password: activePassword });
      accessToken = await readAccessToken(page);
      await expect(page.locator('main')).toBeVisible();
    } finally {
      if (registered) {
        if (!accessToken) {
          const loginResponse = await request.post(`${apiBaseUrl}/api/v1/auth/login`, {
            data: {
              usernameOrEmail: credentials.username,
              password: activePassword,
            },
          });
          expect(loginResponse.ok(), 'Could not sign in to clean up the disposable E2E account').toBeTruthy();
          const loginSession: unknown = await loginResponse.json();
          if (
            typeof loginSession !== 'object' ||
            loginSession === null ||
            !('accessToken' in loginSession) ||
            typeof loginSession.accessToken !== 'string'
          ) {
            throw new Error('The cleanup login response did not contain an access token.');
          }
          accessToken = loginSession.accessToken;
        }

        const deletion = await request.delete(`${apiBaseUrl}/api/v1/auth/me`, {
          headers: { authorization: `Bearer ${accessToken}` },
        });
        expect(deletion.status(), `Could not disable the disposable E2E account (HTTP ${deletion.status()})`).toBe(204);
      }
    }
  });
});
