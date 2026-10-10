# ![AAEmu](https://i.imgur.com/NFDY376.png)

[![Rust](https://github.com/AAEmu/AAEmu/actions/workflows/rust.yml/badge.svg?branch=develop)](https://github.com/AAEmu/AAEmu/actions/workflows/rust.yml)
[![Coverage Status](https://coveralls.io/repos/github/AAEmu/AAEmu/badge.svg?branch=develop)](https://coveralls.io/github/AAEmu/AAEmu?branch=develop)
![Discord](https://img.shields.io/discord/479677351618281472?color=%235865F2&label=Discord&logo=Discord&logoColor=%23FFFFFF")
[![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/AAEmu/AAEmu)

__Open source server software for ArcheAge (1.2) written in Rust__

> AAEmu was originally written in C# (.NET). The login server, game server and tools have been
> ported to Rust; the Cargo workspace lives in [`rust/`](rust/).

## Getting started

### Prerequisites

- **Rust** (stable) via [rustup](https://rustup.rs/), which provides `cargo`. No .NET SDK is needed.
- **MySQL 8**, either installed locally or run with Docker (`docker compose up -d db`).
- The **ArcheAge 1.2 client** files (`game_pak`) and a matching **`compact.sqlite3`** game database
  (not distributed with this repository, see the [wiki](https://github.com/AAEmu/AAEmu/wiki/Dependencies-and-Downloads)).

### Quick start

```sh
git clone https://github.com/AAEmu/AAEmu.git
cd AAEmu/rust
cargo build --release

# MySQL + login + game in one command (needs Docker, creates .env from .env.example)
cargo xtask dev --release
```

Without Docker, import `SQL/aaemu_login.sql` and `SQL/aaemu_game.sql` into your MySQL server, set
the database credentials (see below) and start the servers yourself, login first:

```sh
cd rust
cargo run --release -p aaemu-login
cargo run --release -p aaemu-game     # in a second terminal
```

The `Scripts/start_login.sh` / `Scripts/start_game.sh` (Linux/macOS) and
`Scripts/start AAEmu.Login.bat` / `Scripts/start AAEmu.Game.bat` (Windows) scripts do the same.
Database updates in `SQL/updates` are applied automatically when `Connections:AutoApplyUpdates` is
`true`.

Default ports: login 1237 (clients) and 1234 (game servers), game 1239, stream 1250, web API 1280.

### Configuration and data files

Each server reads its files from its *application directory*; in a source checkout these are:

| Server | Directory | Contents |
| --- | --- | --- |
| Login | `rust/crates/aaemu-login/app/` | `Config.json`, `ExampleConfig.Local.json` |
| Game | `rust/crates/aaemu-game/app/` | `Config.json`, `ExampleConfig.Local.json`, `Configurations/*.json`, `Data/`, `ClientData/`, `Scripts/Commands/kits.json` |

- Put `compact.sqlite3` in `rust/crates/aaemu-game/app/Data/` and the client `game_pak` in
  `rust/crates/aaemu-game/app/ClientData/` (or list its location in `Configurations/ClientData.json`).
- Put local overrides (database password, hosts, ...) in a `Config.Local.json` next to `Config.json`
  (gitignored), or use environment variables (`Connections__MySQLProvider__Password=...`) or
  command-line arguments (`--Section:Key=value`).
- Set `AAEMU_APP_PATH` to use another application directory (the game server also accepts it as its
  first argument).
- Logging goes to stdout and is controlled by `RUST_LOG` (default `info`, e.g. `RUST_LOG=debug`).

See the [login](rust/crates/aaemu-login/README.md) and [game](rust/crates/aaemu-game/README.md)
server READMEs and the [Config.json wiki page](https://github.com/AAEmu/AAEmu/wiki/Working-with-the-Config.json-files-and-server-listings)
for details.

### Building, testing and publishing

```sh
cd rust
cargo build --release            # build everything
cargo test --workspace           # unit and integration tests
cargo xtask ci                   # fmt --check, clippy -D warnings and tests, like CI
cargo xtask publish --archive    # self-contained server folders in publish/
```

`cargo xtask publish [--out DIR] [--target TRIPLE] [--archive] [--no-compact-db]` (or
`Scripts/publish.sh`, `Scripts/start_AAEmu_publish.bat`) creates `publish/aaemu-<version>-<target>/`
with `AAEmu.Login/`, `AAEmu.Game/` (binary plus its config and data), `Tools/` and `SQL/`. Start the
binaries from their folders.

### Docker

`docker-compose.yaml` runs MySQL (`db`), Adminer (`adminer`, http://localhost:8080), `login` and
`game`, built from `docker/Dockerfile`. Copy `.env.example` to `.env`, then use
`Scripts/docker-install-local.sh` (or `.ps1`) and `docker compose up -d`. See the
[Docker Installation Guide](https://github.com/AAEmu/AAEmu/wiki/Docker-Installation-Guide).

### Live testing

[`rust/LIVE-TESTING.md`](rust/LIVE-TESTING.md) describes running the servers against a real MySQL
and 1.2 client data with the headless `aaemu-testclient` bot (smoke tests, parity checks, load tests).

### Setup, help & support, FAQs
Head over to the [wiki](https://github.com/AAEmu/AAEmu/wiki).
If you don't find what you're looking for there, [talk to a human](#discussion). Please do not use our issue tracker for support requests.

### Discussion
- [Community Discord](https://discord.gg/vn8E8E6)
- [FAQ](https://github.com/AAEmu/AAEmu/wiki/FAQ)
- [Mini troubleshooting guide](https://github.com/AAEmu/AAEmu/wiki/Mini-troubleshoot-guide)

### Can I contribute?
Yes you can! Contributions are welcomed provided that they comply with our [Contributing Guidelines](CONTRIBUTING.md). Please ensure you read the relevant sections of the guidelines carefully before making a Pull Request or opening an Issue.

## Contributing

See [Contributing](CONTRIBUTING.md) for details. Thanks to all the people who already contributed!

<a href="https://github.com/AAEmu/AAEmu/graphs/contributors">
  <img src="https://contrib.rocks/image?repo=AAEmu/AAEmu&max=750" />
</a>

## Star History

[![Star History Chart](https://star-history.dera.page/svg?repos=AAEmu/AAEmu&type=timeline)](https://star-history.dera.page/#AAEmu/AAEmu&type=timeline)

## Licensing information

	This program is free software: you can redistribute it and/or modify
	it under the terms of the GNU Lesser General Public License as published by
	the Free Software Foundation, either version 3 of the License, or
	(at your option) any later version.

	This program is distributed in the hope that it will be useful,
	but WITHOUT ANY WARRANTY; without even the implied warranty of
	MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
	GNU Lesser General Public License for more details.

	You should have received a copy of the GNU Lesser General Public License
	along with this program.  If not, see <http://www.gnu.org/licenses/>.

AAEmu/AAEmu are not affiliated with XLGames. All brands and trademarks belong to their respective owners. AAEmu is not a XLGames-approved software, nor is it associated with XLGames.
