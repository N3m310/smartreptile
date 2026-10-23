/*
 * The MAC rules the API enforces, restated for the browser.
 *
 * A board's address is not merely "twelve hex digits": an ESP32 reports its station address through
 * `WiFi.macAddress()`, which is always 48 bits, always unicast, never the broadcast address and never all zeros.
 * Checking that here turns a round trip into an inline message — and stops a typo from reaching a route whose answer
 * would be the same `mac_invalid` anyway (`07-appendices/03` §2.4). The server stays the authority; this mirrors it.
 */

/** Why a value cannot be a board's own address. */
export type MacProblem = 'malformed' | 'broadcast' | 'all-zero' | 'multicast';

export interface MacInspection {
  /** `AA:BB:CC:DD:EE:FF` when the value is twelve hex digits, otherwise null. */
  normalised: string | null;
  /** Null when the value is usable. */
  problem: MacProblem | null;
}

const SEPARATORS = /[\s:.-]/g;

/** Inspects a typed value against the rules the API applies. */
export function inspectMac(value: string): MacInspection {
  const digits = value.replace(SEPARATORS, '');

  if (digits.length !== 12 || !/^[0-9a-fA-F]{12}$/.test(digits)) {
    return { normalised: null, problem: 'malformed' };
  }

  const normalised = (digits.toUpperCase().match(/.{2}/g) ?? []).join(':');

  if (normalised === 'FF:FF:FF:FF:FF:FF') {
    return { normalised, problem: 'broadcast' };
  }

  if (normalised === '00:00:00:00:00:00') {
    // What a chip reports before Wi-Fi starts; treating it as an identity would put every unstarted board in one row.
    return { normalised, problem: 'all-zero' };
  }

  const firstOctet = Number.parseInt(normalised.slice(0, 2), 16);

  // Bit 0 of the first octet is the I/G bit: set means a group address, which is delivered to many stations and
  // therefore identifies none.
  return { normalised, problem: (firstOctet & 0x01) !== 0 ? 'multicast' : null };
}

/** A sentence to show under the input. */
export function macProblemMessage(problem: MacProblem): string {
  switch (problem) {
    case 'malformed':
      return 'Địa chỉ MAC phải gồm 12 chữ số hex, có thể phân cách bằng ":" hoặc "-" (ví dụ AA:BB:CC:DD:EE:FF).';
    case 'broadcast':
      return 'FF:FF:FF:FF:FF:FF là địa chỉ broadcast, không phải của một bo mạch. Dùng địa chỉ ESP32 in ra khi khởi động.';
    case 'all-zero':
      return '00:00:00:00:00:00 là giá trị ESP32 trả về trước khi Wi-Fi khởi động, không phải địa chỉ của nó.';
    case 'multicast':
      return 'Đây là địa chỉ nhóm (bit thấp của octet đầu bằng 1), không phải MAC unicast của bo mạch.';
  }
}
