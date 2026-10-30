import { randomBytes } from 'node:crypto';
import type { APIRequestContext } from '@playwright/test';

const apiBaseUrl = process.env.E2E_API_BASE_URL ?? 'http://127.0.0.1:8080';

export async function createTerrariumFixture(
  request: APIRequestContext,
  accessToken: string,
  name: string
): Promise<string> {
  const response = await request.post(`${apiBaseUrl}/api/v1/terrariums`, {
    headers: { authorization: `Bearer ${accessToken}` },
    data: {
      name,
      speciesProfileId: null,
      location: null,
      timeZoneId: null,
      deviceId: null,
    },
  });
  if (!response.ok()) {
    throw new Error(`Could not create an E2E terrarium fixture (HTTP ${response.status()}).`);
  }

  const terrarium: unknown = await response.json();
  if (
    typeof terrarium !== 'object' ||
    terrarium === null ||
    !('id' in terrarium) ||
    typeof terrarium.id !== 'string'
  ) {
    throw new Error('The terrarium fixture response did not contain an id.');
  }

  return terrarium.id;
}

export async function deleteTerrariumFixture(
  request: APIRequestContext,
  accessToken: string,
  terrariumId: string,
  allowUnboundDevice = false
): Promise<void> {
  const query = allowUnboundDevice ? '?allowUnboundDevice=true' : '';
  const response = await request.delete(
    `${apiBaseUrl}/api/v1/terrariums/${encodeURIComponent(terrariumId)}${query}`,
    { headers: { authorization: `Bearer ${accessToken}` } }
  );
  if (!response.ok() && response.status() !== 410) {
    throw new Error(`Could not soft-delete E2E terrarium fixture (HTTP ${response.status()}).`);
  }
}

export function createMacAddressFixture(): string {
  return ['02', ...randomBytes(5)].map((octet) => octet.toString(16).padStart(2, '0')).join(':');
}

export async function purgeDeviceFixture(
  request: APIRequestContext,
  accessToken: string,
  deviceId: string
): Promise<void> {
  const response = await request.delete(
    `${apiBaseUrl}/api/v1/devices/${encodeURIComponent(deviceId)}?purge=true`,
    { headers: { authorization: `Bearer ${accessToken}` } }
  );
  if (!response.ok() && response.status() !== 404) {
    throw new Error(`Could not purge E2E device fixture (HTTP ${response.status()}).`);
  }
}
