import { expect, test } from '@playwright/test';
import { authStatePath, openSignedInApp, readAccessToken } from './support/auth';
import {
  createTerrariumFixture,
  createMacAddressFixture,
  deleteTerrariumFixture,
  purgeDeviceFixture,
} from './support/fixtures';

test.describe('owner feature surfaces', () => {
  test.describe.configure({ mode: 'serial' });
  test.use({ storageState: authStatePath('owner') });

  test.beforeEach(async ({ page }) => {
    await openSignedInApp(page);
  });

  test('persists personal notification settings across reload and restores the original value', async ({ page }) => {
    await page.goto('/settings');

    const severity = page.getByLabel('Mức cảnh báo tối thiểu');
    await expect(severity).toBeVisible();
    const originalValue = await severity.inputValue();
    const updatedValue = originalValue === 'critical' ? 'warning' : 'critical';
    let restoreRequired = false;

    try {
      await severity.selectOption(updatedValue);
      restoreRequired = true;
      await page.getByRole('button', { name: 'Lưu cài đặt' }).click();
      await expect(page.getByRole('status')).toContainText('Đã lưu cài đặt cá nhân');

      await page.reload();
      await expect(page.getByLabel('Mức cảnh báo tối thiểu')).toHaveValue(updatedValue);
    } finally {
      if (restoreRequired) {
        await page.goto('/settings');
        await page.getByLabel('Mức cảnh báo tối thiểu').selectOption(originalValue);
        await page.getByRole('button', { name: 'Lưu cài đặt' }).click();
        await expect(page.getByRole('status')).toContainText('Đã lưu cài đặt cá nhân');
      }
    }
  });

  test('opens the system diagnosis and wallboard routes', async ({ page }) => {
    await page.goto('/system');
    await expect(page).toHaveURL(/\/system$/);
    await expect(page.locator('main h1')).toBeVisible();

    await page.goto('/wallboard');
    await expect(page).toHaveURL(/\/wallboard(?:\?.*)?$/);
    await expect(page.locator('h1').first()).toBeVisible();
  });

  test('validates an incomplete quiet-hours pair without saving profile changes', async ({ page }) => {
    await page.goto('/settings');

    const patchRequests: string[] = [];
    page.on('request', (request) => {
      if (request.method() === 'PATCH' && new URL(request.url()).pathname.endsWith('/api/v1/auth/me')) {
        patchRequests.push(request.url());
      }
    });

    await page.getByLabel('Bắt đầu giờ yên tĩnh').fill('22:00');
    await page.getByLabel('Kết thúc giờ yên tĩnh').fill('');
    await page.getByRole('button', { name: 'Lưu cài đặt' }).click();

    await expect(page.getByRole('alert')).toContainText(/nhập đủ giờ bắt đầu và giờ kết thúc/i);
    expect(patchRequests).toHaveLength(0);
  });

  test('rejects an invalid MAC before attempting to claim a device', async ({ page, request }) => {
    const accessToken = await readAccessToken(page);
    const terrariumId = await createTerrariumFixture(request, accessToken, `E2E MAC ${Date.now()}`);
    try {
      await page.goto('/devices');
      const form = page.locator('#device-claim-form');
      await expect(form).toBeVisible();
      await form.locator('select').first().selectOption(terrariumId);
      await page.getByRole('tab', { name: /địa chỉ mac/i }).click();
      const macInput = page.getByLabel('Địa chỉ MAC');
      await macInput.fill('not-a-mac');

      const claimRequests: string[] = [];
      page.on('request', (currentRequest) => {
        if (
          currentRequest.method() === 'POST' &&
          new URL(currentRequest.url()).pathname.endsWith('/api/v1/devices/claim-by-mac')
        ) {
          claimRequests.push(currentRequest.url());
        }
      });
      await form.getByRole('button', { name: 'Nhận thiết bị' }).click();

      await expect(macInput).toHaveAttribute('aria-invalid', 'true');
      expect(claimRequests).toHaveLength(0);
    } finally {
      await deleteTerrariumFixture(request, accessToken, terrariumId);
    }
  });

  test('filters alerts and history through their real UI controls', async ({ page, request }) => {
    await page.goto('/alerts');
    const alertTabs = page.getByRole('navigation', { name: 'Trạng thái cảnh báo' }).getByRole('button');
    await expect(alertTabs).toHaveCount(3);
    await alertTabs.nth(1).click();
    await expect(alertTabs.nth(1)).toHaveAttribute('aria-pressed', 'true');
    await page.getByLabel('Mức độ').selectOption('Critical');
    await expect(page.getByLabel('Mức độ')).toHaveValue('Critical');

    const accessToken = await readAccessToken(page);
    const terrariumId = await createTerrariumFixture(request, accessToken, `E2E history ${Date.now()}`);
    try {
      await page.goto(`/history?terrarium=${terrariumId}`);
      const historyHeader = page.locator('main header');
      const ranges = historyHeader.locator('button[aria-pressed]');
      await expect(ranges).toHaveCount(4);
      await ranges.nth(1).click();
      await expect(ranges.nth(1)).toHaveAttribute('aria-pressed', 'true');

      const selectors = historyHeader.locator('select');
      await expect(selectors).toHaveCount(2);
      const initialMetric = await selectors.nth(1).inputValue();
      await selectors.nth(1).selectOption({ index: 1 });
      await expect(selectors.nth(1)).not.toHaveValue(initialMetric);
      await expect(page).toHaveURL(new RegExp(`/history\\?terrarium=${terrariumId}`));
    } finally {
      await deleteTerrariumFixture(request, accessToken, terrariumId);
    }
  });

  test('opens a species profile from the searchable profile catalogue', async ({ page }) => {
    await page.goto('/species-profiles');
    await expect(page.locator('main h1')).toBeVisible();

    const firstProfile = page.locator('main a[href^="/species-profiles/"]').first();
    await expect(firstProfile).toBeVisible();
    const profileName = (await firstProfile.innerText()).split('\n')[0];
    await firstProfile.click();

    await expect(page).toHaveURL(/\/species-profiles\/[^/]+$/);
    await expect(page.locator('main')).toContainText(profileName);
  });

  test('registers, claims, configures, and purges a disposable IoT device', async ({ page, request }) => {
    const accessToken = await readAccessToken(page);
    const suffix = Date.now();
    const terrariumId = await createTerrariumFixture(request, accessToken, `E2E device ${suffix}`);
    const initialName = `E2E node ${suffix}`;
    const macAddress = createMacAddressFixture();
    let deviceClaimed = false;
    let deviceId: string | undefined;
    try {
      await page.goto('/devices');
      const claimForm = page.locator('#device-claim-form');
      await claimForm.locator('select').first().selectOption(terrariumId);
      await page.getByRole('tab', { name: /địa chỉ mac/i }).click();
      await page.getByLabel('Địa chỉ MAC').fill(macAddress);
      await page.getByLabel('Tên thiết bị (không bắt buộc)').fill(initialName);
      const claimResponse = page.waitForResponse((response) =>
        response.request().method() === 'POST' &&
        new URL(response.url()).pathname.endsWith('/api/v1/devices/claim-by-mac')
      );
      await claimForm.getByRole('button', { name: 'Nhận thiết bị' }).click();
      const response = await claimResponse;
      expect(response.ok(), `Device claim returned HTTP ${response.status()}`).toBeTruthy();
      const claim: unknown = await response.json();
      if (
        typeof claim !== 'object' ||
        claim === null ||
        !('deviceId' in claim) ||
        typeof claim.deviceId !== 'string'
      ) {
        throw new Error('The device claim response did not contain a device id.');
      }
      deviceId = claim.deviceId;
      deviceClaimed = true;
      await expect(page.getByRole('status')).toContainText(/khóa thiết bị chỉ hiển thị một lần/i);

      const row = page.locator('tbody tr').filter({ hasText: initialName });
      await expect(row).toBeVisible();
      await row.getByRole('button', { name: 'Sửa' }).click();
      const dialog = page.getByRole('dialog');
      const nameInput = dialog.getByLabel('Tên thiết bị');
      const samplingInput = dialog.getByLabel(/Chu kỳ lấy mẫu/);
      const publishInput = dialog.getByLabel(/Chu kỳ gửi dữ liệu/);
      const updatedName = `${initialName} updated`;
      await nameInput.fill(updatedName);
      await samplingInput.fill('61');
      await publishInput.fill('61');
      await dialog.getByRole('button', { name: 'Lưu' }).click();
      await expect(dialog.getByRole('heading', { name: updatedName })).toBeVisible();
      await dialog.getByRole('button', { name: 'Đóng' }).click();
      await expect(row).toContainText(updatedName);

      await row.getByRole('button').first().click();
      const details = page.getByRole('dialog');
      page.once('dialog', (confirmation) => confirmation.accept());
      await details.getByRole('button', { name: 'Xoá vĩnh viễn' }).click();
      await expect(details).toHaveCount(0);
      await page.getByRole('button', { name: 'Tải lại' }).click();
      await expect(page.locator('tbody tr').filter({ hasText: updatedName })).toHaveCount(0);
      deviceClaimed = false;
    } finally {
      if (deviceId) {
        await purgeDeviceFixture(request, accessToken, deviceId);
      }
      await deleteTerrariumFixture(request, accessToken, terrariumId, deviceClaimed);
    }
  });
});
