import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import type { FullConfig } from '@playwright/test';

export default async function globalSetup(config: FullConfig): Promise<void> {
  const apiBaseUrl = process.env.E2E_API_BASE_URL ?? 'http://127.0.0.1:8080';
  const healthUrl =
    process.env.E2E_API_HEALTH_URL ?? `${apiBaseUrl}/health/ready`;

  if (process.env.E2E_SKIP_API_HEALTH_CHECK !== '1') {
    try {
      const response = await fetch(healthUrl, { signal: AbortSignal.timeout(5_000) });
      if (!response.ok) {
        throw new Error(`HTTP ${response.status}`);
      }
    } catch (error) {
      const detail = error instanceof Error ? error.message : String(error);
      throw new Error(
        `The SmartReptile API is not ready at ${healthUrl} (${detail}). ` +
          'Start the backend and database before running the browser tests, or set ' +
          'E2E_API_HEALTH_URL to the correct readiness URL. ' +
          'Set E2E_SKIP_API_HEALTH_CHECK=1 only when deliberately testing a remote environment.'
      );
    }
  }

  const baseURL = config.projects[0]?.use.baseURL;
  if (typeof baseURL !== 'string') {
    throw new Error('Playwright baseURL must be configured to create authenticated test sessions.');
  }
  const browserOrigin = new URL(baseURL).origin;
  const authDirectory = resolve(process.cwd(), 'test-results', '.auth');
  await mkdir(authDirectory, { recursive: true });

  const accounts = [
    {
      name: 'owner',
      username: process.env.E2E_OWNER_USERNAME ?? 'demo.owner',
      password: process.env.E2E_OWNER_PASSWORD ?? 'Demo!Reptile1',
    },
    {
      name: 'technician',
      username: process.env.E2E_TECHNICIAN_USERNAME ?? 'demo.technician',
      password: process.env.E2E_TECHNICIAN_PASSWORD ?? 'Demo!Reptile1',
    },
    {
      name: 'viewer',
      username: process.env.E2E_VIEWER_USERNAME ?? 'demo.viewer',
      password: process.env.E2E_VIEWER_PASSWORD ?? 'Demo!Reptile1',
    },
    ...(process.env.E2E_ADMIN_USERNAME && process.env.E2E_ADMIN_PASSWORD
      ? [
          {
            name: 'admin',
            username: process.env.E2E_ADMIN_USERNAME,
            password: process.env.E2E_ADMIN_PASSWORD,
          },
        ]
      : []),
  ];

  for (const account of accounts) {
    const response = await fetch(`${apiBaseUrl}/api/v1/auth/login`, {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({
        usernameOrEmail: account.username,
        password: account.password,
      }),
      signal: AbortSignal.timeout(10_000),
    });

    if (!response.ok) {
      throw new Error(
        `Could not prepare the ${account.name} E2E session (HTTP ${response.status}). ` +
          'Check the E2E credentials, demo-data seed, and auth rate limit.'
      );
    }

    const session = (await response.json()) as Record<string, unknown>;
    if (
      typeof session.accessToken !== 'string' ||
      typeof session.refreshToken !== 'string' ||
      typeof session.user !== 'object' ||
      session.user === null
    ) {
      throw new Error(`The API returned an invalid session while preparing the ${account.name} E2E session.`);
    }

    const storageState = {
      cookies: [],
      origins: [
        {
          origin: browserOrigin,
          localStorage: [
            {
              name: 'terraguard.session',
              value: JSON.stringify(session),
            },
          ],
        },
      ],
    };

    await writeFile(
      resolve(authDirectory, `${account.name}.json`),
      JSON.stringify(storageState, null, 2),
      'utf8'
    );
  }
}
