import 'package:flutter/foundation.dart';

/// The MAC rules the API enforces (`SmartReptile.Domain.Devices.MacAddress`), restated for the app.
///
/// A board's address is not merely "twelve hex digits": an ESP32 reports its station address through
/// `WiFi.macAddress()`, which is always 48 bits, always unicast, never the broadcast address and never all zeros.
/// Checking that here turns a round trip into an inline message — and stops a typo from reaching a route whose answer
/// would be the same `mac_invalid` anyway (`07-appendices/03` §2.4). The server stays the authority; this mirrors it.
enum MacProblem {
  /// Twelve hex digits once the separators are gone, and usable as a station address.
  none,

  /// Not twelve hex digits.
  malformed,

  /// `FF:FF:FF:FF:FF:FF` — every station, so no station.
  broadcast,

  /// `00:00:00:00:00:00` — what a chip reports before Wi-Fi is up.
  allZero,

  /// A group address: the I/G bit of the first octet is set.
  multicast,
}

@immutable
class MacInspection {
  const MacInspection(this.normalised, this.problem);

  /// `AA:BB:CC:DD:EE:FF` when the value has twelve hex digits, otherwise null.
  final String? normalised;

  /// [MacProblem.none] when the value is usable.
  final MacProblem problem;

  bool get isUsable => problem == MacProblem.none;
}

const _separators = {':', '-', '.', ' '};
const _hexDigits = '0123456789abcdefABCDEF';

/// Inspects a typed value against the rules the API applies.
MacInspection inspectMac(String? value) {
  final input = value ?? '';

  final digits = input.split('').where((char) => !_separators.contains(char)).join();

  // Exactly twelve characters, every one of them a hex digit. A truncated value is refused rather than accepted: a
  // shorter address could match a different board instead of none.
  if (digits.length != 12 || digits.split('').any((char) => !_hexDigits.contains(char))) {
    return const MacInspection(null, MacProblem.malformed);
  }

  final upper = digits.toUpperCase();
  final normalised = [
    for (var index = 0; index < 12; index += 2) upper.substring(index, index + 2),
  ].join(':');

  if (normalised == 'FF:FF:FF:FF:FF:FF') {
    return MacInspection(normalised, MacProblem.broadcast);
  }

  if (normalised == '00:00:00:00:00:00') {
    // What a chip reports before Wi-Fi starts; treating it as an identity would put every unstarted board in one row.
    return MacInspection(normalised, MacProblem.allZero);
  }

  final firstOctet = int.parse(normalised.substring(0, 2), radix: 16);

  // Bit 0 of the first octet is the I/G bit: set means a group address, which is delivered to many stations and
  // therefore identifies none.
  return MacInspection(
    normalised,
    (firstOctet & 0x01) != 0 ? MacProblem.multicast : MacProblem.none,
  );
}

/// A sentence to show under the input, or null when the value is usable.
String? macProblemMessage(MacProblem problem) => switch (problem) {
  MacProblem.none => null,
  MacProblem.malformed =>
    'Địa chỉ MAC phải gồm 12 chữ số hex, có thể phân cách bằng ":" hoặc "-" (ví dụ AA:BB:CC:DD:EE:FF).',
  MacProblem.broadcast =>
    'FF:FF:FF:FF:FF:FF là địa chỉ broadcast, không phải của một bo mạch. Dùng địa chỉ ESP32 in ra khi khởi động.',
  MacProblem.allZero =>
    '00:00:00:00:00:00 là giá trị ESP32 trả về trước khi Wi-Fi khởi động, không phải địa chỉ của nó.',
  MacProblem.multicast =>
    'Đây là địa chỉ nhóm (bit thấp của octet đầu bằng 1), không phải MAC unicast của bo mạch.',
};
