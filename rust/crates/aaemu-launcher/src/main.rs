//! `aaemu-launcher` — starts the ArcheAge 1.2 client against an AAEmu login server.
//!
//! ```text
//! aaemu-launcher --client <dir>\bin32\archeage.exe --login 127.0.0.1:1237 --user NAME
//!                [--password PW | --password-file F]   (else $AAEMU_PASSWORD, else prompt)
//!                [--lang en_us] [--nosplash] [--sound] [--arg X]... [--no-hshield-arg] [--cwd DIR]
//!                [--ticket-timeout SECS] [--wait] [--dry-run]
//! ```
//!
//! See the crate documentation (`src/lib.rs`) for the ticket protocol.

use std::io::Write as _;
use std::path::PathBuf;

use aaemu_launcher::{
    client_args, command_line, mapping_bytes, password_hash, ticket_plain, ticket_xml,
    LaunchOptions,
};
use anyhow::{bail, Context};

const USAGE: &str = "\
aaemu-launcher: start the ArcheAge 1.2 (Trion) client with a launcher ticket

  --client PATH          client executable (<client>\\bin32\\archeage.exe); default $AAEMU_CLIENT
  --login HOST:PORT      login server (default 127.0.0.1:1237)
  --user NAME            account name (AutoAccount on the login server creates it)
  --password PW          password; else --password-file F, else $AAEMU_PASSWORD, else prompt
  --password-file F      read the password from the first line of F
  --lang LANG            client locale, e.g. en_us, de, fr, ru, ko, ja, zh_cn (default en_us;
                         empty string = do not pass -lang)
  --nosplash             pass -nosplash
  --sound                enable client sound and music (silent by default)
  --no-sound             disable client sound and music
  --fast-start           skip the timed login logo stages (enabled by default)
  --fast-pak             use 64 KB pak read-ahead (enabled by default)
  --smooth-start         normal-priority CPU clock calibration (enabled by default)
  --no-fast-start        use the original timed login logo stages
  --no-fast-pak          use the original 1 MB pak read-ahead
  --no-smooth-start      use the original CPU clock calibration priorities
  --hidden               render in a hidden 1280x720 DX10 window (needs the driver)
  --multi-client         accept this client's built-in confirmation for another instance
  --driver-dll PATH      x86 aadriver.dll (required for startup audio policy)
  --pak-manifest PATH    lossless compressed pak manifest; else <client>/game.aacm if present
  --no-compressed-pak    disable automatic compressed pak discovery
  --arg X                extra client argument (repeatable)
  --no-hshield-arg       do not append +acpxmk
  --cwd DIR              working directory of the client (default: the executable's folder)
  --ticket-timeout SECS  how long to wait for the client to signal the ticket event (default 60)
  --wait                 keep running until the client exits and report its exit code
  --dry-run              print the command line and ticket XML (password hash masked), start nothing
";

struct Cli {
    client: Option<PathBuf>,
    login: String,
    user: Option<String>,
    password: Option<String>,
    password_file: Option<PathBuf>,
    lang: String,
    extra: Vec<String>,
    hshield_arg: bool,
    sound: bool,
    cwd: Option<PathBuf>,
    ticket_timeout: u32,
    wait: bool,
    dry_run: bool,
    driver_dll: Option<PathBuf>,
    fast_start: bool,
    fast_pak: bool,
    smooth_start: bool,
    hidden: bool,
    multi_client: bool,
    pak_manifest: Option<PathBuf>,
    compressed_pak: bool,
}

fn parse_cli() -> anyhow::Result<Cli> {
    parse_args(std::env::args().skip(1))
}

fn parse_args(mut it: impl Iterator<Item = String>) -> anyhow::Result<Cli> {
    let mut cli = Cli {
        client: std::env::var_os("AAEMU_CLIENT").map(PathBuf::from),
        login: "127.0.0.1:1237".into(),
        user: None,
        password: None,
        password_file: None,
        lang: "en_us".into(),
        extra: vec![],
        hshield_arg: true,
        sound: false,
        cwd: None,
        ticket_timeout: 60,
        wait: false,
        dry_run: false,
        driver_dll: std::env::var_os("AADRIVER_DLL").map(PathBuf::from),
        fast_start: true,
        fast_pak: true,
        smooth_start: true,
        hidden: false,
        multi_client: false,
        pak_manifest: std::env::var_os("AAEMU_PAK_MANIFEST").map(PathBuf::from),
        compressed_pak: true,
    };
    while let Some(a) = it.next() {
        let mut val = || it.next().with_context(|| format!("{a} needs a value"));
        match a.as_str() {
            "--client" => cli.client = Some(val()?.into()),
            "--login" => cli.login = val()?,
            "--user" => cli.user = Some(val()?),
            "--password" => cli.password = Some(val()?),
            "--password-file" => cli.password_file = Some(val()?.into()),
            "--lang" => cli.lang = val()?,
            "--nosplash" => cli.extra.push("-nosplash".into()),
            "--sound" => cli.sound = true,
            "--no-sound" => cli.sound = false,
            "--fast-start" => cli.fast_start = true,
            "--fast-pak" => cli.fast_pak = true,
            "--smooth-start" => cli.smooth_start = true,
            "--no-fast-start" => cli.fast_start = false,
            "--no-fast-pak" => cli.fast_pak = false,
            "--no-smooth-start" => cli.smooth_start = false,
            "--hidden" => cli.hidden = true,
            "--multi-client" => cli.multi_client = true,
            "--pak-manifest" => {
                cli.pak_manifest = Some(val()?.into());
                cli.compressed_pak = true;
            }
            "--no-compressed-pak" => {
                cli.pak_manifest = None;
                cli.compressed_pak = false;
            }
            "--driver-dll" => cli.driver_dll = Some(val()?.into()),
            "--arg" => cli.extra.push(val()?),
            "--no-hshield-arg" => cli.hshield_arg = false,
            "--cwd" => cli.cwd = Some(val()?.into()),
            "--ticket-timeout" => cli.ticket_timeout = val()?.parse()?,
            "--wait" => cli.wait = true,
            "--dry-run" => cli.dry_run = true,
            "-h" | "--help" => {
                print!("{USAGE}");
                std::process::exit(0);
            }
            _ => bail!("unknown argument '{a}'\n\n{USAGE}"),
        }
    }
    Ok(cli)
}

fn read_password(cli: &Cli) -> anyhow::Result<String> {
    if let Some(p) = &cli.password {
        return Ok(p.clone());
    }
    if let Some(f) = &cli.password_file {
        let s = std::fs::read_to_string(f).with_context(|| format!("reading {}", f.display()))?;
        return Ok(s.lines().next().unwrap_or_default().to_string());
    }
    if let Ok(p) = std::env::var("AAEMU_PASSWORD") {
        return Ok(p);
    }
    eprint!("password (input is visible): ");
    std::io::stderr().flush()?;
    let mut s = String::new();
    std::io::stdin().read_line(&mut s)?;
    Ok(s.trim_end_matches(['\r', '\n']).to_string())
}

fn split_login(login: &str) -> anyhow::Result<(String, u16)> {
    match login.rsplit_once(':') {
        Some((h, p)) => Ok((h.to_string(), p.parse().context("invalid login port")?)),
        None => Ok((login.to_string(), 1237)),
    }
}

fn driver_candidates(exe: &std::path::Path) -> Vec<PathBuf> {
    let Some(dir) = exe.parent() else {
        return Vec::new();
    };
    let mut paths = vec![dir.join("aadriver.dll"), dir.join("aadriver/aadriver.dll")];
    // Cargo controllers live in target/debug or target/release; native/build.bat
    // places the shared x86 driver in target/aadriver.
    if let Some(target) = dir.parent() {
        paths.push(target.join("aadriver/aadriver.dll"));
    }
    // native/build.bat's default output for a source checkout, including when
    // Cargo uses a custom target directory on another drive.
    paths.push(
        PathBuf::from(env!("CARGO_MANIFEST_DIR"))
            .join("../aaemu-clientdriver/native/out/aadriver.dll"),
    );
    paths
}

fn compressed_pak_path(cli: &Cli, client: &std::path::Path) -> Option<PathBuf> {
    if !cli.compressed_pak {
        return None;
    }
    cli.pak_manifest.clone().or_else(|| {
        let candidate = client.parent()?.parent()?.join("game.aacm");
        candidate.is_file().then_some(candidate)
    })
}

fn main() -> anyhow::Result<()> {
    let mut cli = parse_cli()?;
    if cli.driver_dll.is_none() && !cli.dry_run {
        cli.driver_dll = driver_candidates(&std::env::current_exe()?)
            .into_iter()
            .find(|path| path.is_file());
        if cli.driver_dll.is_none() {
            bail!("startup audio policy needs aadriver.dll even with optimizations disabled: build aaemu-clientdriver/native/build.bat, use --driver-dll PATH or AADRIVER_DLL");
        }
    }
    if cli.fast_start || cli.fast_pak || cli.smooth_start || cli.hidden || cli.multi_client {
        cli.extra.push("-nosplash".into());
    }
    if cli.fast_start {
        cli.extra.push("-aaemu-fast-start".into());
    }
    if cli.fast_pak {
        cli.extra.push("-aaemu-fast-pak".into());
    }
    if cli.smooth_start {
        cli.extra.push("-aaemu-smooth-start".into());
    }
    if cli.multi_client {
        cli.extra.push("-aaemu-multi-client".into());
    }
    if cli.hidden {
        cli.extra.extend(
            [
                "-aaemu-hidden",
                "+r_driver",
                "DX10",
                "+r_fullscreen",
                "0",
                "+r_width",
                "1280",
                "+r_height",
                "720",
            ]
            .map(str::to_owned),
        );
    }
    let client = cli
        .client
        .clone()
        .context("--client (or AAEMU_CLIENT) is required")?;
    if let Some(path) = compressed_pak_path(&cli, &client) {
        if !path.is_file() {
            bail!("compressed pak manifest {} not found", path.display());
        }
        if !cli.dry_run {
            let bridge = cli
                .driver_dll
                .as_ref()
                .and_then(|dll| dll.parent())
                .context("compressed pak requires an early client driver")?
                .join("aaemu_pak.dll");
            let client_bridge = client
                .parent()
                .and_then(std::path::Path::parent)
                .map(|root| root.join("aaemu_pak.dll"));
            if !bridge.is_file() && !client_bridge.is_some_and(|path| path.is_file()) {
                bail!("compressed pak bridge {} not found; build the i686 aaemu-pak library next to aadriver.dll", bridge.display());
            }
        }
        cli.extra.push("-aaemu-pak-manifest".into());
        cli.extra
            .push(path.canonicalize()?.to_string_lossy().into_owned());
    }
    let user = cli.user.clone().context("--user is required")?;
    if user.trim().is_empty() {
        bail!("--user must not be empty");
    }
    let (login_host, login_port) = split_login(&cli.login)?;
    let opts = LaunchOptions {
        login_host,
        login_port,
        lang: cli.lang.clone(),
        extra_args: cli.extra.clone(),
        sound: cli.sound,
        hshield_arg: cli.hshield_arg,
    };
    let exe = client.to_string_lossy().to_string();

    if cli.dry_run {
        println!("{}", command_line(&exe, &client_args(&opts, 0, 0)));
        println!(
            "{}",
            ticket_plain(&ticket_xml(&user, "<sha256-hex of the password>"))
        );
        return Ok(());
    }

    if let Some(dll) = &cli.driver_dll {
        if !dll.is_file() {
            bail!("driver DLL {} not found", dll.display());
        }
    }

    let password = read_password(&cli)?;
    if password.is_empty() {
        bail!("the password must not be empty");
    }
    let plain = ticket_plain(&ticket_xml(&user, &password_hash(&password)));
    let key: [u8; 8] = rand::random();
    run(
        &cli,
        &client,
        &exe,
        &opts,
        &mapping_bytes(&key, plain.as_bytes()),
    )
}

#[cfg(windows)]
fn run(
    cli: &Cli,
    client: &std::path::Path,
    exe: &str,
    opts: &LaunchOptions,
    mapping: &[u8],
) -> anyhow::Result<()> {
    use aaemu_launcher::win::{Process, Ticket};

    let ticket = Ticket::create(mapping)?;
    if ticket.already_existed {
        eprintln!(
            "warning: the ticket objects already existed (is another launcher or client running?)"
        );
    }
    let cmd = command_line(
        exe,
        &client_args(opts, ticket.map.value(), ticket.event.value()),
    );
    let cwd = match &cli.cwd {
        Some(d) => d.clone(),
        None => client
            .parent()
            .map(PathBuf::from)
            .unwrap_or_else(|| PathBuf::from(".")),
    };
    println!("starting: {cmd}");
    let mut proc = Process::spawn(client, &cmd, &cwd, &ticket, cli.hidden, true)?;
    println!("client started (pid {})", proc.pid);
    if let Some(dll) = &cli.driver_dll {
        aaemu_clientdriver::win::inject(proc.pid, dll).context("early client driver injection")?;
        proc.wait_for_audio_ready(20_000)
            .context("client audio policy readiness (rebuild aadriver.dll if outdated)")?;
        proc.resume()?;
        println!("client driver injected");
    }

    // Keep the mapping alive until the client has taken the ticket (it signals the event), it
    // exits, or the timeout runs out. The client holds its own inherited handles, so closing
    // ours afterwards does not remove the ticket.
    let deadline =
        std::time::Instant::now() + std::time::Duration::from_secs(u64::from(cli.ticket_timeout));
    loop {
        if ticket.wait_signaled(250)? {
            println!("client signalled the ticket event");
            break;
        }
        if let Some(code) = proc.wait(Some(0))? {
            bail!("client exited with code {code} before taking the ticket");
        }
        if std::time::Instant::now() >= deadline {
            println!(
                "no ticket event after {} s (the client may not signal it); leaving the client running",
                cli.ticket_timeout
            );
            break;
        }
    }
    if cli.wait {
        let code = proc.wait(None)?.unwrap_or_default();
        println!("client exited with code {code}");
    }
    Ok(())
}

#[cfg(not(windows))]
fn run(
    _cli: &Cli,
    _client: &std::path::Path,
    _exe: &str,
    _opts: &LaunchOptions,
    _mapping: &[u8],
) -> anyhow::Result<()> {
    bail!("starting the client needs Windows (use --dry-run to inspect the arguments)")
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn normal_launch_enables_optimizations_without_hiding_the_window() {
        let cli = parse_args(std::iter::empty()).unwrap();
        assert!(cli.fast_start && cli.smooth_start && cli.fast_pak);
        assert!(!cli.hidden);
        assert!(!cli.multi_client);
        assert!(!cli.sound);
    }

    #[test]
    fn sound_is_explicit_and_independent_of_rendering_and_startup_profiles() {
        let args = [
            "--sound",
            "--hidden",
            "--no-fast-start",
            "--no-fast-pak",
            "--no-smooth-start",
        ];
        let cli = parse_args(args.into_iter().map(str::to_owned)).unwrap();
        assert!(cli.sound && cli.hidden);
        assert!(!cli.fast_start && !cli.fast_pak && !cli.smooth_start);
        let cli = parse_args(["--sound", "--no-sound"].into_iter().map(str::to_owned)).unwrap();
        assert!(!cli.sound);
        assert!(USAGE.contains("required for startup audio policy"));
    }

    #[test]
    fn additional_instance_confirmation_is_explicit() {
        let cli = parse_args(["--multi-client".to_owned()].into_iter()).unwrap();
        assert!(cli.multi_client);
    }

    #[test]
    fn compressed_pak_selection_is_explicit_and_can_be_disabled() {
        let client = PathBuf::from("Client").join("bin32").join("archeage.exe");
        let cli = parse_args(
            ["--pak-manifest", "custom.aacm"]
                .into_iter()
                .map(str::to_owned),
        )
        .unwrap();
        assert_eq!(
            compressed_pak_path(&cli, &client),
            Some("custom.aacm".into())
        );
        let cli = parse_args(
            ["--pak-manifest", "custom.aacm", "--no-compressed-pak"]
                .into_iter()
                .map(str::to_owned),
        )
        .unwrap();
        assert_eq!(compressed_pak_path(&cli, &client), None);
    }

    #[test]
    fn original_profiles_can_be_selected_and_reenabled() {
        let args = ["--no-fast-start", "--no-fast-pak", "--no-smooth-start"];
        let cli = parse_args(args.into_iter().map(str::to_owned)).unwrap();
        assert!(!cli.fast_start && !cli.fast_pak && !cli.smooth_start);
        let args = args
            .into_iter()
            .chain(["--fast-start", "--fast-pak", "--smooth-start"]);
        let cli = parse_args(args.map(str::to_owned)).unwrap();
        assert!(cli.fast_start && cli.fast_pak && cli.smooth_start);
    }

    #[test]
    fn driver_search_supports_published_and_cargo_layouts() {
        let dir = PathBuf::from("target").join("release");
        assert_eq!(
            driver_candidates(&dir.join("aaemu-launcher.exe")),
            vec![
                dir.join("aadriver.dll"),
                dir.join("aadriver/aadriver.dll"),
                PathBuf::from("target").join("aadriver/aadriver.dll"),
                PathBuf::from(env!("CARGO_MANIFEST_DIR"))
                    .join("../aaemu-clientdriver/native/out/aadriver.dll")
            ]
        );
    }
}
