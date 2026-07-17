# Changelog

## Unreleased

### Added

- `AAEmu.Login`: new `CARequestAuth_004_Packet` and its handler — the 3.5.0.3 client authentication variant.
- Login server port documentation: `Docs/LoginServer3503Port.md` (English) and `Docs/LoginServer3503Port_ru.md` (Russian) — what was ported from 3.5.0.3 and why.

### Changed

- Ported the login server (`AAEmu.Login`) to the 3.5.0.3 client protocol based on the 1.2 → 3.5 comparison materials (`diff-3503`):
  - `LoginClient` — new `ACJoinResponsePacket` signatures (character slots 6→2, `AdditionalData=22`) and `ACAuthResponsePacket`;
  - `LoginSession` — reworked login state machine for the 3.5 packet sequence;
  - `CARequestAuthTencentPacketHandler` — working authentication via `KoreaAuthFlowFactory` instead of a no-op stub;
  - C2L/L2C packet structures and opcodes (`CLOffsets`, `LCOffsets`, `GLOffsets`, `LGOffsets`) synced with the 3.5.0.3 client.
