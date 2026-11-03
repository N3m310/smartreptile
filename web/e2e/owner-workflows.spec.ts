import { expect, test } from '@playwright/test';
import { authStatePath, openSignedInApp, readAccessToken } from './support/auth';

test.describe('owner workflows', () => {
  test.use({ storageState: authStatePath('owner') });

  test.beforeEach(async ({ page }) => {
    await openSignedInApp(page);
  });

  test('navigates through dashboard, terrariums, detail and linked history', async ({ page }) => {
    await expect(page.locator('main h1')).toBeVisible();

    await page.locator('nav a[href="/terrariums"]').click();
    await expect(page).toHaveURL(/\/terrariums$/);
    await expect(page.locator('main h1')).toBeVisible();

    const detailLink = page.locator('main a[href^="/terrariums/"]').first();
    if (await detailLink.count()) {
      await detailLink.click();
      await expect(page).toHaveURL(/\/terrariums\/[^/]+$/);
      await expect(page.locator('main')).toBeVisible();

      const historyLink = page.locator('a[href^="/history?terrarium="]').first();
      if (await historyLink.count()) {
        await historyLink.click();
      } else {
        await page.locator('a[href="/history"]').click();
      }
    } else {
      await page.locator('a[href="/history"]').click();
    }

    await expect(page).toHaveURL(/\/history(?:\?.*)?$/);
    await expect(page.locator('main h1')).toBeVisible();
  });

  test('opens devices, alerts and personal settings from the application navigation', async ({ page }) => {
    await page.locator('a[href="/devices"]').click();
    await expect(page).toHaveURL(/\/devices$/);
    await expect(page.locator('main h1')).toBeVisible();
    await expect(page.locator('#device-claim-form')).toBeVisible();

    await page.locator('a[href="/alerts"]').click();
    await expect(page).toHaveURL(/\/alerts$/);
    await expect(page.locator('main h1')).toBeVisible();

    await page.locator('a[href="/settings"]').click();
    await expect(page).toHaveURL(/\/settings$/);
    await expect(page.locator('main h1')).toBeVisible();
  });

  test('shows owner-only management actions without changing demo data', async ({ page }) => {
    await page.locator('nav a[href="/terrariums"]').click();
    await expect(page.getByRole('button', { name: /new terrarium|thêm bể nuôi|create terrarium/i })).toBeVisible();

    await page.locator('a[href="/devices"]').click();
    await expect(page.locator('#device-claim-form')).toBeVisible();
  });

  test('creates a terrarium, updates its species, validates thresholds, and opens its history', async ({
    page,
    request,
  }) => {
    const accessToken = await readAccessToken(page);
    const name = `E2E terrarium ${Date.now()}`;
    let terrariumId: string | undefined;

    try {
      await page.locator('nav a[href="/terrariums"]').click();
      await page.getByRole('button', { name: /new terrarium|thêm bể nuôi|create terrarium/i }).click();
      await expect(page.locator('#terrarium-name')).toBeVisible();

      const species = page.locator('#terrarium-species');
      await expect(species.locator('option').nth(1)).toBeAttached();
      await page.locator('#terrarium-name').fill(name);
      await page.locator('#terrarium-location').fill('Playwright E2E cleanup');

      const createdResponse = page.waitForResponse((response) =>
        response.request().method() === 'POST' &&
        new URL(response.url()).pathname.endsWith('/api/v1/terrariums')
      );
      await page.locator('#terrarium-name').locator('xpath=ancestor::form').getByRole('button', {
        name: /create|tạo|save|lưu/i,
      }).click();
      const response = await createdResponse;
      expect(response.ok(), `Create terrarium returned HTTP ${response.status()}`).toBeTruthy();

      const created: unknown = await response.json();
      if (
        typeof created !== 'object' ||
        created === null ||
        !('id' in created) ||
        typeof created.id !== 'string'
      ) {
        throw new Error('Create response did not contain a terrarium id.');
      }
      terrariumId = created.id;

      const terrariumLink = page.locator('main a[href^="/terrariums/"]').filter({ hasText: name });
      await expect(terrariumLink).toBeVisible();
      await terrariumLink.click();
      await expect(page).toHaveURL(new RegExp(`/terrariums/${terrariumId}$`));

      const speciesEditor = page.locator('#terrarium-species-edit');
      await expect(speciesEditor.locator('option').nth(1)).toBeAttached();
      await speciesEditor.selectOption({ index: 1 });
      await page.locator('form').filter({ has: speciesEditor }).getByRole('button', {
        name: /save|lưu/i,
      }).click();
      await expect(page.getByRole('status')).toBeVisible();

      const thresholdsSection = page
        .getByRole('heading', { name: /effective thresholds|ngưỡng hiệu lực/i })
        .locator('xpath=..');
      const thresholdEdit = thresholdsSection.locator('button[aria-expanded]').first();
      await expect(thresholdEdit).toBeVisible();
      await thresholdEdit.click();
      await expect(thresholdEdit).toHaveAttribute('aria-expanded', 'true');
      const targetMin = page.locator('input[id$="-target-min"]').first();
      const targetMax = page.locator('input[id$="-target-max"]').first();
      await expect(targetMin).toBeVisible();
      await targetMin.fill(await targetMax.inputValue());
      const thresholdForm = page.locator('form').filter({ has: targetMin });
      await thresholdForm.getByRole('button', { name: /save|lưu/i }).click();
      await expect(thresholdsSection.getByRole('alert')).toBeVisible();

      const historyLink = page.locator(`a[href="/history?terrarium=${terrariumId}"]`).first();
      await expect(historyLink).toBeVisible();
      await historyLink.click();
      await expect(page).toHaveURL(new RegExp(`/history\\?.*terrarium=${terrariumId}`));
      await expect(page.locator('main h1')).toBeVisible();
    } finally {
      if (terrariumId) {
        const cleanup = await request.delete(
          `${process.env.E2E_API_BASE_URL ?? 'http://127.0.0.1:8080'}/api/v1/terrariums/${terrariumId}`,
          { headers: { authorization: `Bearer ${accessToken}` } }
        );
        expect(cleanup.ok(), `Could not soft-delete E2E terrarium (HTTP ${cleanup.status()})`).toBeTruthy();
      }
    }
  });
});
