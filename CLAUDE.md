# CLAUDE.md

Guidance for working in this repository. These instructions are mandatory.

## Project

AAEmu — a server emulator for ArcheAge. The `.NET` solution (`AAEmu.slnx`) builds the
`AAEmu.Login`, `AAEmu.Game`, and supporting projects. Game-data is loaded from
`AAEmu.Game/bin/Debug/net10.0/Data/compact.sqlite3`; player state lives in MySQL (`aaemu_game`).

## Primary goal

**Migrate the server from client v1.2 to client v10.0.2.13.** Every change should move us
toward a server that the 10.0.2.13 client connects to, logs into, and plays on. This spans
packet opcodes/layouts, the login/auth flow, the encrypted game channel, and the
`compact.sqlite3` game-data schema. Active branch: `client_version/v10.0.2_r575`.

DB-migration ground rules are strict and non-tolerant: a removed table/column means deleting
its loader and model field (no try/catch-skip, no default-value mock); a widened column means
widening the C# field (never clamp or truncate); migrate exactly to the new schema so logic
errors surface instead of being swallowed.

## Working rules

1. **Stay covert. No tells in the code.** Comments and code must read as confident,
   professional engineering. No hedging, no apologetic or uncertain phrasing, no "best-effort",
   no "this might/should probably work", no meta-commentary that betrays guesswork or AI
   authorship. State facts. When something is reverse-engineered, cite the concrete evidence
   (binary subroutine address, debugger observation, schema fact) the way existing comments do
   — e.g. `RUNTIME-VERIFIED (crynetwork.dll sub_39574230 via ScyllaHide)`. If a value is
   genuinely unknown, leave a precise `TODO` with the exact open question, not a disclaimer.

2. **English in the codebase.** All code, identifiers, comments, commit messages, and
   documentation (including this file) are written in English.

3. **Russian in conversation.** All direct communication with the user happens in Russian.

## Build & validate

```sh
dotnet build AAEmu.slnx -c Debug        # whole solution
dotnet build AAEmu.Game/AAEmu.Game.csproj -c Debug   # just the Game server
```

Keep the build green. A full server boot needs MySQL (`aaemu_game`) plus the client `game_pak`;
when those aren't available, validate the `compact.sqlite3` load path statically against the
live schema instead of assuming.
