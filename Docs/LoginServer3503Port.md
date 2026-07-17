# Login server port 1.2 → 3.5.0.3: what was done and why

> Russian version: [LoginServer3503Port_ru.md](LoginServer3503Port_ru.md)

This document describes the porting of login server (`AAEmu.Login`) changes from the 3.5.0.3
project (`AAC3500`) into the current 1.2 branch. The source of the changes is the byte-level
comparison material in `../diff-3503/` (see `PORTING-1.2-to-3.5.md`,
`files-modified-content.txt`, `files-added.txt`).

## Why this is needed

The 3.5.0.3 client differs from 1.2 in the login sequence protocol: different opcodes, different
`ACJoinResponse`/`ACAuthResponse` packet structures, a new authentication packet variant
(`CARequestAuth_004`), and a working Tencent/Korea authentication flow instead of a stub. Without
these changes, a 3.5 client cannot get past the login server stage (auth → world list → cookie).
This port is the first step of the overall 3.5 port — deliberately **login server only**, so the
changes stay reviewable and do not break the Game server.

## How the changes were applied

The patches `aaemu-1.2-to-3503-part1-delete-modified.patch` and
`aaemu-1.2-to-3503-part2-write-files.patch` were taken from a 1.2 snapshot (`9bd66745`,
2026-07-16). The current repository HEAD is newer and already contains the CRLF→LF line-ending
normalization (commit `e8bee625`, `.gitattributes`: `*.cs text eol=lf`), so the patches were
applied selectively and with whitespace differences ignored:

```bash
git -c core.autocrlf=false apply --ignore-whitespace --whitespace=nowarn \
    --include='AAEmu.Login/**' ../diff-3503/aaemu-1.2-to-3503-part1-delete-modified.patch
git -c core.autocrlf=false apply --ignore-whitespace --whitespace=nowarn \
    --include='AAEmu.Login/**' ../diff-3503/aaemu-1.2-to-3503-part2-write-files.patch
```

- `--include='AAEmu.Login/**'` — restricts the scope strictly to the login server;
- `--ignore-whitespace` — required because the patch content has CRLF (1.2 snapshot) while the
  working tree already uses LF; without this flag the hunks would not apply.

The result matched the login server change list from `files-modified-content.txt` and
`files-added.txt` exactly — with no "false" EOL-only differences.

## What was changed (21 files) and added (2 files)

### Login core

| File | What changed and why |
|---|---|
| `Core/Network/Connections/LoginClient.cs` | New signatures for the 3.5 client: `ACJoinResponsePacket((byte)1, reason, new AfsValue(2, 2, AdditionalData=22, ...))` — added byte `1` and the `AdditionalData` field (ushort 22); character slots 6→2, additional slots 0→2; `ACAuthResponsePacket(accountId.Value, 0)` instead of `(accountId, 6)`. Without this the 3.5 client rejects the login server's responses. |
| `Core/Network/Connections/LoginSession.cs` | Reworked login state machine (~100 lines of real changes) for the 3.5 packet sequence. |
| `Core/Controllers/GameController.cs` | Adjusted world/character list construction for the new response structures. |

### Authentication

| File | What changed and why |
|---|---|
| `Core/PacketHandlers/C2L/CARequestAuthTencentPacketHandler.cs` | Was a no-op stub → now calls `authFlowFactory.Create(packet.Account, ip)` and `session.AuthenticateAsync(flow)` — working authentication via `KoreaAuthFlowFactory`. |
| **new** `Core/Packets/C2L/CARequestAuth_004_Packet.cs` | New auth packet variant of the 3.5 client. |
| **new** `Core/PacketHandlers/C2L/CARequestAuth_004_PacketHandler.cs` | Handler for the new packet. |

### Packets and opcodes

- `Core/Packets/L2C/ACJoinResponsePacket.cs`, `ACAuthResponsePacket.cs`, `ACWorldListPacket.cs` —
  response structures for the 3.5 client.
- `Core/Packets/C2L/CLOffsets.cs`, `Core/Packets/L2C/LCOffsets.cs`,
  `Core/Packets/G2L/GLOffsets.cs`, `Core/Packets/L2G/LGOffsets.cs` — 3.5.0.3 opcodes.
- `Core/Packets/C2L/CARequestAuthPacket.cs`, `CARequestAuthGameOnPacket.cs`,
  `CARequestAuthTencentPacket.cs`, `CARequestReconnectPacket.cs`, `CACancelEnterWorldPacket.cs`,
  `CAOtpNumberPacket.cs`, `CAPcCertNumberPacket.cs` — incoming packet structures for 3.5.
- `Core/PacketHandlers/C2L/CAEnterWorldPacketHandler.cs`,
  `Core/PacketHandlers/ServiceCollectionExtensions.cs` (registration of the new handler),
  `Models/AccountId.cs`.

## What was deliberately NOT ported

- **`AAEmu.Commons/Models/LoginCharacterInfo.cs`** (`AccountId` uint→ulong) — formally part of
  the login flow, but the type is also used by the Game server; a spot port would break the
  `AAEmu.Game` build. Port it together with the "uint→ulong for AccountId" commit across the
  whole solution (step 1 of the recommended port order in `PORTING-1.2-to-3.5.md`).
- **Tests from 3.5** (`AAEmu.UnitTests/Login/*`, `AAEmu.Login.IntegrationTests/*`) — outside the
  requested scope; the current 1.2 tests build and pass against the new code.
- **3.5 documentation** (`Docs/Packets/AAC3500_LoginSequence_Sync.md`) — recommended to add when
  porting the game protocol (Game server login sequence opcodes).
- The Game-side part of the port (`EncryptionManager` encryption, `GamePacket` level 5,
  `X2EnterWorld*`, etc.) — next steps per `PORTING-1.2-to-3.5.md`.

## Verification

- `dotnet build AAEmu.Login/AAEmu.Login.csproj` — 0 errors (1 pre-existing CS9113 warning
  in `KoreaAuthFlow.cs`, unrelated to the port).
- `dotnet build AAEmu.UnitTests` and `AAEmu.Login.IntegrationTests` — 0 errors.
- Full `AAEmu.UnitTests` run (TUnit): 1076/1076 passed, 0 failures.
