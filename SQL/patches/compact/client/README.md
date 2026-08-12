# Client-side compact.sqlite3 patches

Patches in this folder apply to the **client's** game database:

```
<ArcheAge client>\game\db\compact.sqlite3
```

Everything else under `SQL/patches/compact/` targets the copy the server loads
(`AAEmu.Game/Data/compact.sqlite3`). These are separate files, and a patch in here does **not**
belong in the server's copy.

## When a fix belongs here

Some rules the game enforces are read only by the client, never sent to it. Where the client
refuses an action before any packet reaches the server, no amount of server work changes the
outcome — the data the client itself reads has to change. Those fixes live here.

The tell is usually that the server-side handler never logs anything: the client declined locally.

## Applying

Any SQLite client works, for example:

```sh
sqlite3 "<ArcheAge client>/game/db/compact.sqlite3" < 2026-08-12-synthesis-grade-cap.sql
```

Back the database up first, and restart the client afterwards — these tables are read once at
startup.

## Caveats

- Both the client and the server keep their own `compact.sqlite3`, and both are gitignored, so
  every developer extracts their own. Nothing here is applied automatically.
- A patch here changes what one installed client permits. Every player connecting to a server that
  depends on the patched behaviour needs the same patch, or they will still be blocked locally.
- Each file states which dataset it was written against. Verify before applying to a different
  client build; the queries are written to be idempotent and to match nothing when already applied.
