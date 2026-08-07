# AAEmu.Web

A small ASP.NET Core Razor Pages front-end for account administration, intended for local and
community server use. It talks to the two databases directly and does not communicate with the
login or game server processes, so it can run whether or not they are up.

## Pages

| Page | Purpose |
|------|---------|
| `/` | Landing page with the registered account count |
| `/Accounts` | Paginated, searchable list of accounts |
| `/Accounts/Details/{id}` | One account: login details, editable gameplay values, its characters |
| `/Accounts/Create` | Registers a new account |
| `/Characters` | Paginated, searchable list of every character |
| `/health/ready` | Health check covering both database connections |

## Configuration

Copy `Config.Local.json.example` to `Config.Local.json` and fill in your credentials. That file is
gitignored; `Config.json` holds only placeholders and is committed.

Two connections are required, matching the split between the servers:

- `Connections:MySQLProvider` — the login database (`aaemu_login`): credentials, bans
- `Connections:GameMySQLProvider` — the game database (`aaemu_game`): access level, labor, credits,
  loyalty, characters

The two are joined on `users.id` == `accounts.account_id`. They are queried over separate
connections rather than with a SQL join, so they may live on different servers.

### Launching the client

With `ClientLauncher:Enabled` set and `ExecutablePath` pointing at `archeage.exe`, a **Run** button
appears on the account list and on each account's detail page. It starts the client with the
launcher passport arguments for that account:

```
archeage.exe -devmode {DevMode} -StrUserName={account} -strUserToken={UserToken}
             -sIp={AuthIp} -sPort={AuthPort} -gameId={GameId} +locale {Locale}
```

`DevMode`, `Locale`, `AuthIp`, `AuthPort`, `GameId` and `UserToken` are all configurable; the
defaults match a stock local setup. `-serverId` / `-selectedServerId` are deliberately not passed,
so the client stops on world select and can outlive a world restart.

The client is started **by the web server process**, so it opens on the machine hosting this app —
not on the machine viewing the page. Launch requests from anything other than loopback are
refused for that reason. `Enabled` is off by default.

Run it with:

```
dotnet run --project AAEmu.Web
```

## Notes and limitations

- **There is no authentication.** Anyone who can reach the site can list accounts and edit gameplay
  values. Do not expose it to the internet as-is.
- **Passwords are stored in the legacy format** (`Base64(SHA256(password))`). `AAEmu.Login`'s
  `PasswordService` recognises it, verifies it, and upgrades the row to PBKDF2 on the account's
  first login. Moving the shared hashing code out of `AAEmu.Login` into a library both projects can
  reference would let accounts be created with PBKDF2 up front.
- **`korea_challenge_hash` is not populated** on registration, so accounts created here will not
  work with the V2 challenge flow until their first plaintext password login backfills it.
- **Labor is capped at 32767.** `AccountManager` reads that column with `GetInt16`, so a larger
  value would overflow on the account's next load even though the column is an `INT`.
- **Characters are read-only.** The game server holds a character in memory while it is online and
  writes the whole row back on save, so web-side edits to an online character would be lost.
- **Usernames are not unique in the database.** The `users` table has no unique index on
  `username`, so registration guards against duplicates in application code only.
- `AccessLevels.json` is linked from `AAEmu.Game/Configurations/` at build time rather than copied,
  so the access level hints cannot drift from the game's own command table.
