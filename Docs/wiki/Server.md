# Server Downloads

- Audience: Contributors, players, and testers
- Last verified against: `develop` on October 10, 2026
- Prerequisites: Rust (rustup) if building from source

While there are occasional prebuilt downloads, it is strongly recommended to
compile your own server from the latest `develop` branch.

See [Installation & Setup](Installation-&-Setup). In short:
`cd rust && cargo build --release`.

If that is not an option, you can use precompiled releases (Windows and Linux
archives created with `cargo xtask publish --archive`). They contain the
`aaemu-login` and `aaemu-game` binaries with their configuration files, the
tools and the `SQL/` scripts. You still need MySQL, `compact.sqlite3`, the
client `game_pak` and your own `Config.Local.json` files.

## ArcheAge 1.2

- [Release](https://github.com/AAEmu/AAEmu/releases)

## Related

- [Home](Home)
- [Installation & Setup](Installation-&-Setup)
- [Local Development Guide](Local-Development-Guide)
