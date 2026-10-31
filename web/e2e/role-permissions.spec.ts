import { expect, test } from '@playwright/test';
import { authStatePath, openSignedInApp, readAccessToken, signIn } from './support/auth';

for (const [role, credentials] of [
  ['technician', 'technician'],
  ['viewer', 'viewer'],
] as const) {
  test.describe(role, () => {
    test.use({ storageState: authStatePath(credentials) });

    test('can read their surfaces but cannot open admin or provision devices', async ({ page }) => {
      await page.goto('/devices');
      await expect(page.locator('main')).toBeVisible();
      await expect(page.locator('#device-claim-form')).toHaveCount(0);
      await expect(page.getByRole('button', { name: /configure|cấu hình|edit|sửa/i })).toHaveCount(0);

      await page.goto('/admin');
      await expect(page).toHaveURL(/\/dashboard$/);
    });
  });
}

test.describe('viewer species-profile permissions', () => {
  test.use({ storageState: authStatePath('viewer') });

  test('cannot open species-profile authoring controls', async ({ page }) => {
    await page.goto('/species-profiles');

    await expect(page.locator('main')).toBeVisible();
    await expect(page.getByRole('button', { name: /new profile|tạo hồ sơ|thêm hồ sơ/i })).toHaveCount(0);
  });
});

test.describe('administrator', () => {
  test.skip(
    !process.env.E2E_ADMIN_USERNAME || !process.env.E2E_ADMIN_PASSWORD,
    'Set E2E_ADMIN_USERNAME and E2E_ADMIN_PASSWORD to run administrator scenarios.'
  );
  test.use({ storageState: authStatePath('admin') });

  test('can open the overview, account and role management screens', async ({ page }) => {
    for (const route of ['/admin', '/admin/accounts', '/admin/roles']) {
      await page.goto(route);
      await expect(page).toHaveURL(new RegExp(`${route.replaceAll('/', '\\/')}$`));
      await expect(page.locator('main h1')).toBeVisible();
    }
  });

  test('creates, reads, updates and deletes a custom species profile', async ({ page, request }) => {
    test.setTimeout(60_000);
    await openSignedInApp(page);
    const accessToken = await readAccessToken(page);
    const suffix = Date.now();
    const profileName = `E2E profile ${suffix}`;
    const updatedProfileName = `${profileName} updated`;
    const apiBaseUrl = process.env.E2E_API_BASE_URL ?? 'http://127.0.0.1:8080';
    let profileId: string | undefined;
    let profileDeleted = false;

    try {
      await page.goto('/species-profiles');
      await page.getByRole('button', { name: /new profile|tạo hồ sơ|thêm hồ sơ/i }).click();
      await page.locator('#profile-create-name').fill(profileName);
      await page.locator('#profile-create-scientificName').fill(`Testus ${suffix}`);
      await page.locator('#profile-create-band-0-target-min').fill('20');
      await page.locator('#profile-create-band-0-target-max').fill('30');

      const createResponse = page.waitForResponse((response) =>
        response.request().method() === 'POST' &&
        new URL(response.url()).pathname === '/api/v1/species-profiles',
      { timeout: 15_000 });
      await page.locator('#profile-create-name').locator('xpath=ancestor::form')
        .getByRole('button', { name: /create|tạo|save|lưu/i }).click();
      const created = await createResponse;
      expect(created.ok(), `Profile creation returned HTTP ${created.status()}`).toBeTruthy();

      const profile: unknown = await created.json();
      if (
        typeof profile !== 'object' ||
        profile === null ||
        !('id' in profile) ||
        typeof profile.id !== 'string'
      ) {
        throw new Error('Species-profile create response did not contain an id.');
      }
      profileId = profile.id;

      await expect(page).toHaveURL(new RegExp(`/species-profiles/${profileId}$`));
      await expect(page.locator('main')).toContainText(profileName);

      await page.goto('/species-profiles');
      const search = page.getByRole('searchbox');
      await search.fill(profileName);
      const card = page.locator('article').filter({ hasText: profileName });
      await expect(card).toBeVisible();
      await card.getByRole('button', { name: /edit|sửa/i }).click();

      const nameInput = page.locator(`#profile-edit-${profileId}-name`);
      await nameInput.fill(updatedProfileName);
      const updateResponse = page.waitForResponse((response) =>
        response.request().method() === 'PATCH' &&
        new URL(response.url()).pathname === `/api/v1/species-profiles/${profileId}`,
      { timeout: 15_000 });
      await nameInput.locator('xpath=ancestor::form')
        .getByRole('button', { name: /save|lưu/i }).click();
      const updated = await updateResponse;
      expect(updated.ok(), `Profile update returned HTTP ${updated.status()}`).toBeTruthy();
      const updatedCard = page.locator('article').filter({ hasText: updatedProfileName });
      await expect(updatedCard).toContainText(updatedProfileName);

      await updatedCard.getByRole('button', { name: /delete|xóa|xoá/i }).first().click();
      const deleteResponse = page.waitForResponse((response) =>
        response.request().method() === 'DELETE' &&
        new URL(response.url()).pathname === `/api/v1/species-profiles/${profileId}`,
      { timeout: 15_000 });
      await page.getByRole('button', { name: /delete|xóa|xoá/i }).last().click();
      const deleted = await deleteResponse;
      expect(deleted.status(), `Profile deletion returned HTTP ${deleted.status()}`).toBe(204);
      profileDeleted = true;
      await expect(updatedCard).toHaveCount(0);
    } finally {
      if (profileId && !profileDeleted) {
        const cleanup = await request.delete(
          `${apiBaseUrl}/api/v1/species-profiles/${encodeURIComponent(profileId)}`,
          { headers: { authorization: ['Bearer', accessToken].join(' ') } }
        );
        if (cleanup.status() !== 204 && cleanup.status() !== 404) {
          throw new Error(`Could not delete the E2E species profile (HTTP ${cleanup.status()}).`);
        }
      }
    }
  });
});
