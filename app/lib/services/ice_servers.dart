import '../core/env.dart';

/// ICE configuration for the live camera, shared by the publishing phone and the viewer.
///
/// STUN alone is not enough for two peers that cannot dial each other. The Android emulator is the case that bit this
/// project: its only IPv4 candidate is the host's *public* address, so the phone would have to hairpin off its own NAT
/// to reach it, and ICE never completes even though signalling succeeds and `onTrack` fires. A TURN server both sides
/// can reach (the build-time `TURN_URL`) adds a relay candidate that always pairs.
///
/// Kept in one place so the two peer roles cannot drift into different ICE configurations — they must agree, or the
/// candidates one side gathers are unusable to the other.
Map<String, dynamic> iceConfiguration() {
  final servers = <Map<String, dynamic>>[
    {'urls': 'stun:stun.l.google.com:19302'},
  ];

  if (Env.turnUrl.isNotEmpty) {
    servers.add({
      'urls': Env.turnUrl,
      'username': Env.turnUsername,
      'credential': Env.turnPassword,
    });
  }

  return {'iceServers': servers};
}
