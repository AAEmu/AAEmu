# Client Downloads

- Audience: Contributors, players, and testers
- Last verified against: `develop` on October 10, 2026
- Prerequisites: Internet access and enough disk space for client files

Our current `develop` branch requires client version 1.2 (revision `208022`).
Download links are listed below.

Besides connecting to the server, the client data is also needed for server
configuration.

## For AAEmu version 1.2

### Client v1.2 (`r208022`)

1. [MEGA](https://mega.nz/folder/GnwjQCrZ#WNWzX_lDvkzCqoTtt7I42Q) -
   `ArcheAge 1.2.4.13 (r208022) for AAEmu.7z`
1. [MEGA][client-download-mega-patched] -
   `Trion_1.2_(r208022)_BROC_FULL_PATCHED_CLIENT.zip`
1. [Google][client-download-google] - `aaemu client.zip` (multiple files)

### Launcher (`aaemu-launcher`)

The 1.2 client does not ask for a user name or password itself: a launcher
hands it a login ticket. AAEmu ships its own launcher in the Cargo workspace
(`rust/crates/aaemu-launcher`), so nothing else has to be downloaded. It runs
on Windows (where the client runs).

```sh
cd rust
cargo build --release -p aaemu-launcher
cargo run --release -p aaemu-launcher -- --client "C:\ArcheAge\bin32\archeage.exe" --login 127.0.0.1:1237 --user myname
```

- `--client`: the client executable `bin32\archeage.exe` inside the extracted
  client folder (or set the `AAEMU_CLIENT` environment variable). The client
  folder is only read; nothing in it has to be changed.
- `--login`: the login server's public address and port (default
  `127.0.0.1:1237`).
- `--user`: the account name. With `AutoAccount` (on by default) the login
  server creates the account on the first login.
- Password: the launcher asks for it on the console, or use
  `--password PW`, `--password-file FILE` (first line of the file) or the
  `AAEMU_PASSWORD` environment variable.
- `--lang` selects the client locale (default `en_us`); `--nosplash`,
  `--arg X` (extra client argument), `--wait` (stay until the client exits)
  and `--dry-run` (print the command line only) are optional; `--help` lists
  everything.

The built binary is `rust/target/release/aaemu-launcher.exe` (or in your Cargo
target directory); a published build has it in `Tools/`. The ticket protocol is
described in
[`rust/LIVE-TESTING.md`](https://github.com/AAEmu/AAEmu/blob/develop/rust/LIVE-TESTING.md#6-the-real-12-client-aaemu-launcher).

The public AAEmu Launcher (Trion 1.2 login type) also works as an alternative,
but it is not needed.

### Extra client archives

- [MEGA client directory (multiple versions)](https://mega.nz/folder/C3Q0WQjT#vRUethZLPiYSo2B4nE_etg)

## Related

- [Home](Home)
- [Installation & Setup](Installation-&-Setup)
- [Local Development Guide](Local-Development-Guide)

[client-download-google]: https://drive.google.com/drive/folders/1_pIBVHIm1YFal-nteGaVuXjTv3Yrsv4Q
[client-download-mega-patched]: https://mega.nz/folder/C3Q0WQjT#vRUethZLPiYSo2B4nE_etg/file/qyAVQY4I
