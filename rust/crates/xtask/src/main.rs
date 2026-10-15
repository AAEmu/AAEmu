//! Repository automation, run as `cargo xtask <command>` from anywhere below the repository root
//! (the alias lives in the repository's `.cargo/config.toml`; without it use
//! `cargo run -p xtask -- <command>` inside `rust/`).
//!
//! * `publish` — release build plus a distributable folder (binaries, configuration, data, SQL),
//!   also used by `Scripts/publish.sh`.
//! * `dev` — local orchestration: MySQL in Docker
//!   (the `db` service of `docker-compose.yaml`), then the login server, then the game server
//!   once the login server accepts game-server registrations, all wired through environment
//!   variables.
//! * `ci` — the checks CI runs: `cargo fmt --check`, `cargo clippy -D warnings`, `cargo test`, and
//!   the S->C packet coverage ratchet (`tools/opcode_coverage/sc_usage.py --check`, needs python).

use std::collections::HashMap;
use std::io::{BufRead, BufReader};
use std::net::{SocketAddr, TcpStream};
use std::path::{Path, PathBuf};
use std::process::{Child, Command, Stdio};
use std::time::{Duration, Instant};

use anyhow::{bail, Context, Result};

const USAGE: &str = "\
Usage: cargo xtask <command> [options]

Commands:
  publish   Build release binaries and lay out a distributable server folder
              --out <dir>        output directory (default: <repo>/publish)
              --target <triple>  cross-compile for this target (default: host)
              --profile <name>   cargo profile (default: release)
              --name <name>      folder name (default: aaemu-<version>-<target>)
              --no-compact-db    do not copy Data/*.sqlite3 into the game folder
              --archive          also pack the folder (.zip on Windows, .tar.gz elsewhere)
  dev       Start MySQL (docker compose service `db`), the login server and the game server
              --release          run release builds
              --no-db            do not start MySQL; use --db-host/--db-port or Config.Local.json
              --db-host <host>   MySQL host passed to the servers (default: 127.0.0.1)
              --db-port <port>   MySQL port (default: DB_PORT from .env, else 3306)
  ci        cargo fmt --check, clippy -D warnings, test, S->C coverage ratchet (the CI checks)
";

/// The login server's internal (game-server registration) port in the shipped `Config.json`.
const LOGIN_INTERNAL_PORT: u16 = 1234;

fn main() {
    let args: Vec<String> = std::env::args().skip(1).collect();
    let result = match args.first().map(String::as_str) {
        Some("publish") => publish(&args[1..]),
        Some("dev") => dev(&args[1..]),
        Some("ci") => ci(),
        Some("help" | "-h" | "--help") | None => {
            print!("{USAGE}");
            Ok(())
        }
        Some(other) => Err(anyhow::anyhow!("unknown command `{other}`\n\n{USAGE}")),
    };
    if let Err(e) = result {
        eprintln!("error: {e:#}");
        std::process::exit(1);
    }
}

/// The repository root (`rust/crates/xtask/../../..`).
fn repo_root() -> PathBuf {
    let root = Path::new(env!("CARGO_MANIFEST_DIR")).join("../../..");
    std::path::absolute(&root).unwrap_or(root)
}

/// The cargo workspace (`<repo>/rust`).
fn workspace_dir() -> PathBuf {
    repo_root().join("rust")
}

fn cargo() -> Command {
    let mut cmd = Command::new(std::env::var_os("CARGO").unwrap_or_else(|| "cargo".into()));
    cmd.current_dir(workspace_dir());
    cmd
}

fn run(cmd: &mut Command) -> Result<()> {
    let status = cmd
        .status()
        .with_context(|| format!("failed to start {cmd:?}"))?;
    if !status.success() {
        bail!("{cmd:?} failed ({status})");
    }
    Ok(())
}

/// Simple `--flag` / `--key value` option parser.
struct Opts {
    flags: Vec<String>,
    values: HashMap<String, String>,
}

impl Opts {
    fn parse(args: &[String], with_value: &[&str], flags: &[&str]) -> Result<Self> {
        let mut o = Opts {
            flags: Vec::new(),
            values: HashMap::new(),
        };
        let mut it = args.iter();
        while let Some(a) = it.next() {
            if with_value.contains(&a.as_str()) {
                let v = it.next().with_context(|| format!("{a} needs a value"))?;
                o.values.insert(a.clone(), v.clone());
            } else if flags.contains(&a.as_str()) {
                o.flags.push(a.clone());
            } else {
                bail!("unknown option `{a}`\n\n{USAGE}");
            }
        }
        Ok(o)
    }

    fn flag(&self, name: &str) -> bool {
        self.flags.iter().any(|f| f == name)
    }

    fn value(&self, name: &str) -> Option<&str> {
        self.values.get(name).map(String::as_str)
    }
}

// ---------------------------------------------------------------------------------------------
// publish
// ---------------------------------------------------------------------------------------------

/// Packages built by `publish` and the folder their binaries go to.
const PUBLISH_PACKAGES: &[(&str, &str)] = &[
    ("aaemu-login", "AAEmu.Login"),
    ("aaemu-game", "AAEmu.Game"),
    ("aaemu-tool-worldconverter", "Tools"),
    ("aaemu-tool-updatesfortransform", "Tools"),
    ("aaemu-testclient", "Tools"),
    ("aaemu-launcher", "Tools"),
];

fn host_triple() -> Result<String> {
    let rustc = std::env::var_os("RUSTC").unwrap_or_else(|| "rustc".into());
    let out = Command::new(rustc)
        .arg("-vV")
        .output()
        .context("running rustc -vV")?;
    String::from_utf8_lossy(&out.stdout)
        .lines()
        .find_map(|l| l.strip_prefix("host: ").map(str::to_string))
        .context("rustc -vV printed no host triple")
}

/// Builds the packages and returns `(package name, executable path)` for every binary.
fn build_binaries(profile: &str, target: Option<&str>) -> Result<Vec<(String, PathBuf)>> {
    let mut cmd = cargo();
    cmd.args(["build", "--bins", "--profile", profile])
        .arg("--message-format=json-render-diagnostics");
    for (pkg, _) in PUBLISH_PACKAGES {
        cmd.args(["-p", pkg]);
    }
    if let Some(t) = target {
        cmd.args(["--target", t]);
    }
    cmd.stdout(Stdio::piped());
    let mut child = cmd.spawn().context("starting cargo build")?;
    let mut bins = Vec::new();
    for line in BufReader::new(child.stdout.take().expect("piped stdout")).lines() {
        let line = line?;
        let Ok(msg) = serde_json::from_str::<serde_json::Value>(&line) else {
            continue;
        };
        if msg["reason"] != "compiler-artifact" {
            continue;
        }
        let Some(exe) = msg["executable"].as_str() else {
            continue;
        };
        let pkg = msg["package_id"].as_str().unwrap_or_default();
        if let Some((name, _)) = PUBLISH_PACKAGES
            .iter()
            .find(|(name, _)| package_id_names(pkg, name))
        {
            bins.push((name.to_string(), PathBuf::from(exe)));
        }
    }
    let status = child.wait()?;
    if !status.success() {
        bail!("cargo build failed ({status})");
    }
    Ok(bins)
}

/// Whether a cargo package id (`path+file:///…/aaemu-game#0.1.0` or the older
/// `aaemu-game 0.1.0 (path+file:///…)`) names `name`.
fn package_id_names(id: &str, name: &str) -> bool {
    if id.starts_with(&format!("{name} ")) {
        return true;
    }
    let before_version = id.rsplit_once('#').map_or(id, |(p, _)| p);
    before_version
        .rsplit(['/', '\\'])
        .next()
        .is_some_and(|last| last == name || last.starts_with(&format!("{name}@")))
}

/// Copies `src` into `dst` recursively; `skip` gets the path relative to `src`.
fn copy_tree(src: &Path, dst: &Path, skip: &dyn Fn(&Path) -> bool) -> Result<()> {
    fn walk(root: &Path, dir: &Path, dst: &Path, skip: &dyn Fn(&Path) -> bool) -> Result<()> {
        for entry in std::fs::read_dir(dir).with_context(|| format!("reading {}", dir.display()))? {
            let entry = entry?;
            let path = entry.path();
            let rel = path.strip_prefix(root).expect("below root");
            if skip(rel) {
                continue;
            }
            let target = dst.join(rel);
            if entry.file_type()?.is_dir() {
                std::fs::create_dir_all(&target)?;
                walk(root, &path, dst, skip)?;
            } else {
                if let Some(parent) = target.parent() {
                    std::fs::create_dir_all(parent)?;
                }
                std::fs::copy(&path, &target).with_context(|| {
                    format!("copying {} to {}", path.display(), target.display())
                })?;
            }
        }
        Ok(())
    }
    std::fs::create_dir_all(dst)?;
    walk(src, src, dst, skip)
}

/// Files of an `app/` folder that are never published: local overrides (may hold
/// credentials) and the client files (`game_pak` is several GB; `ClientData/readme.txt` stays).
fn skip_app_file(rel: &Path, skip_compact_db: bool) -> bool {
    let name = rel
        .file_name()
        .map(|n| n.to_string_lossy().to_lowercase())
        .unwrap_or_default();
    let mut parts = rel.components().map(|c| c.as_os_str().to_string_lossy());
    let top = parts.next().unwrap_or_default();
    if top == "ClientData" && rel.components().count() > 1 {
        return name != "readme.txt";
    }
    name == "config.local.json" || (skip_compact_db && name.ends_with(".sqlite3"))
}

fn publish(args: &[String]) -> Result<()> {
    let o = Opts::parse(
        args,
        &["--out", "--target", "--profile", "--name"],
        &["--no-compact-db", "--archive"],
    )?;
    let root = repo_root();
    let profile = o.value("--profile").unwrap_or("release");
    let target = o.value("--target");
    let triple = match target {
        Some(t) => t.to_string(),
        None => host_triple()?,
    };
    let out = o
        .value("--out")
        .map(PathBuf::from)
        .unwrap_or_else(|| root.join("publish"));
    let name = o.value("--name").map_or_else(
        || format!("aaemu-{}-{triple}", env!("CARGO_PKG_VERSION")),
        str::to_string,
    );
    let dist = out.join(&name);

    println!("Building ({profile}, {triple}) ...");
    let bins = build_binaries(profile, target)?;
    for (pkg, _) in PUBLISH_PACKAGES {
        if !bins.iter().any(|(p, _)| p == pkg) {
            bail!("cargo build produced no binary for {pkg}");
        }
    }

    if dist.exists() {
        std::fs::remove_dir_all(&dist)
            .with_context(|| format!("removing the old {}", dist.display()))?;
    }
    println!("Laying out {} ...", dist.display());
    for (pkg, exe) in &bins {
        let folder = PUBLISH_PACKAGES
            .iter()
            .find(|(p, _)| p == pkg)
            .map(|(_, f)| *f)
            .expect("known package");
        let dir = dist.join(folder);
        std::fs::create_dir_all(&dir)?;
        let file = exe.file_name().expect("executable file name");
        std::fs::copy(exe, dir.join(file)).with_context(|| format!("copying {}", exe.display()))?;
    }
    let skip_db = o.flag("--no-compact-db");
    let crates = workspace_dir().join("crates");
    copy_tree(
        &crates.join("aaemu-login/app"),
        &dist.join("AAEmu.Login"),
        &|rel| skip_app_file(rel, skip_db),
    )?;
    copy_tree(
        &crates.join("aaemu-game/app"),
        &dist.join("AAEmu.Game"),
        &|rel| skip_app_file(rel, skip_db),
    )?;
    // The servers search `SQL/updates` upwards from their executable's directory.
    copy_tree(&root.join("SQL"), &dist.join("SQL"), &|_| false)?;
    for f in ["README.md", "LICENSE.GPL", "LICENSE.MIT"] {
        if root.join(f).is_file() {
            std::fs::copy(root.join(f), dist.join(f))?;
        }
    }
    if !dist.join("AAEmu.Game/Data/compact.sqlite3").is_file() {
        println!(
            "note: no compact.sqlite3 was published; copy it to {} before starting the game server",
            dist.join("AAEmu.Game/Data").display()
        );
    }

    if o.flag("--archive") {
        let archive = if cfg!(windows) {
            out.join(format!("{name}.zip"))
        } else {
            out.join(format!("{name}.tar.gz"))
        };
        let _ = std::fs::remove_file(&archive);
        let mut tar = Command::new("tar");
        if cfg!(windows) {
            tar.arg("-a").arg("-cf");
        } else {
            tar.arg("-czf");
        }
        tar.arg(&archive).arg("-C").arg(&out).arg(&name);
        run(&mut tar)?;
        println!("Archive: {}", archive.display());
    }
    println!("Published to {}", dist.display());
    Ok(())
}

// ---------------------------------------------------------------------------------------------
// dev
// ---------------------------------------------------------------------------------------------

/// `KEY=VALUE` lines of a `.env` file (comments and blank lines ignored).
fn read_env_file(path: &Path) -> Result<HashMap<String, String>> {
    let text =
        std::fs::read_to_string(path).with_context(|| format!("reading {}", path.display()))?;
    Ok(parse_env(&text))
}

fn parse_env(text: &str) -> HashMap<String, String> {
    text.lines()
        .map(str::trim)
        .filter(|l| !l.is_empty() && !l.starts_with('#'))
        .filter_map(|l| l.split_once('='))
        .map(|(k, v)| {
            let v = v.trim();
            let v = v
                .strip_prefix('"')
                .and_then(|v| v.strip_suffix('"'))
                .unwrap_or(v);
            (k.trim().to_string(), v.to_string())
        })
        .collect()
}

fn wait_for_port(addr: SocketAddr, timeout: Duration, child: &mut Child, what: &str) -> Result<()> {
    let start = Instant::now();
    loop {
        if TcpStream::connect_timeout(&addr, Duration::from_millis(500)).is_ok() {
            return Ok(());
        }
        if let Some(status) = child.try_wait()? {
            bail!("{what} exited before it was ready ({status})");
        }
        if start.elapsed() > timeout {
            bail!("{what} did not open {addr} within {timeout:?}");
        }
        std::thread::sleep(Duration::from_millis(500));
    }
}

fn dev(args: &[String]) -> Result<()> {
    let o = Opts::parse(args, &["--db-host", "--db-port"], &["--release", "--no-db"])?;
    let root = repo_root();

    // .env (the docker compose settings, incl. the MySQL root password)
    let env_path = root.join(".env");
    if !env_path.exists() {
        std::fs::copy(root.join(".env.example"), &env_path)
            .context("creating .env from .env.example")?;
        println!("Created .env from .env.example");
    }
    let env = read_env_file(&env_path)?;

    let db_host = o.value("--db-host").unwrap_or("127.0.0.1").to_string();
    let db_port = o
        .value("--db-port")
        .map(str::to_string)
        .or_else(|| env.get("DB_PORT").cloned())
        .unwrap_or_else(|| "3306".into());
    let db_user = env.get("DB_USER").cloned().unwrap_or_else(|| "root".into());
    let db_password = env.get("DB_PASSWORD").cloned().unwrap_or_default();

    if !o.flag("--no-db") {
        println!("Starting MySQL (docker compose service `db`) ...");
        run(Command::new("docker")
            .args(["compose", "up", "-d", "--wait", "db"])
            .current_dir(&root))
        .context("starting MySQL; is Docker running? (use --no-db to use your own MySQL)")?;
    }

    let release = o.flag("--release");
    let profile_args: &[&str] = if release { &["--release"] } else { &[] };
    println!("Building aaemu-login and aaemu-game ...");
    run(cargo()
        .args(["build", "-p", "aaemu-login", "-p", "aaemu-game"])
        .args(profile_args))?;

    let mut db_env: Vec<(String, String)> = Vec::new();
    if !o.flag("--no-db") || o.value("--db-host").is_some() || o.value("--db-port").is_some() {
        db_env.extend([
            ("Connections__MySQLProvider__Host".into(), db_host),
            ("Connections__MySQLProvider__Port".into(), db_port),
        ]);
    }
    if !o.flag("--no-db") {
        db_env.extend([
            ("Connections__MySQLProvider__User".into(), db_user),
            ("Connections__MySQLProvider__Password".into(), db_password),
        ]);
    }
    db_env.push(("Connections__AutoApplyUpdates".into(), "true".into()));

    let server = |pkg: &str, extra: &[(&str, String)]| -> Result<Child> {
        let mut cmd = cargo();
        cmd.args(["run", "-q", "-p", pkg]).args(profile_args);
        for (k, v) in &db_env {
            cmd.env(k, v);
        }
        for (k, v) in extra {
            cmd.env(k, v);
        }
        cmd.spawn().with_context(|| format!("starting {pkg}"))
    };

    println!("Starting the login server ...");
    let mut login = server(
        "aaemu-login",
        &[
            (
                "Connections__MySQLProvider__Database",
                "aaemu_login".to_string(),
            ),
            ("GameServers__0__Id", "1".into()),
            ("GameServers__0__Name", "AAEmu.Game".into()),
            ("GameServers__0__Host", "127.0.0.1".into()),
            ("GameServers__0__Port", "1239".into()),
        ],
    )?;
    let internal = SocketAddr::from(([127, 0, 0, 1], LOGIN_INTERNAL_PORT));
    if let Err(e) = wait_for_port(
        internal,
        Duration::from_secs(300),
        &mut login,
        "aaemu-login",
    ) {
        let _ = login.kill();
        return Err(e);
    }

    println!("Starting the game server ...");
    let game = match server(
        "aaemu-game",
        &[
            ("Connections__MySQLProvider__Database", "aaemu_game".into()),
            ("LoginNetwork__Host", "127.0.0.1".into()),
            ("LoginNetwork__Port", LOGIN_INTERNAL_PORT.to_string()),
        ],
    ) {
        Ok(g) => g,
        Err(e) => {
            let _ = login.kill();
            return Err(e);
        }
    };
    println!("Login: 127.0.0.1:1237, game: 127.0.0.1:1239. Press Ctrl+C to stop both servers.");

    // Ctrl+C reaches the servers directly (same console / process group) and they shut down
    // gracefully; wait for both.
    let mut running: Vec<(&str, Child)> = vec![("aaemu-login", login), ("aaemu-game", game)];
    while !running.is_empty() {
        let mut i = 0;
        while i < running.len() {
            if let Some(status) = running[i].1.try_wait()? {
                println!("{} exited ({status})", running[i].0);
                running.remove(i);
            } else {
                i += 1;
            }
        }
        std::thread::sleep(Duration::from_millis(500));
    }
    Ok(())
}

// ---------------------------------------------------------------------------------------------
// ci
// ---------------------------------------------------------------------------------------------

fn ci() -> Result<()> {
    run(cargo().args(["fmt", "--all", "--check"]))?;
    run(cargo().args([
        "clippy",
        "--workspace",
        "--all-targets",
        "--",
        "-D",
        "warnings",
    ]))?;
    run(cargo().args(["test", "--workspace"]))?;
    sc_ratchet()
}

/// The S->C packet coverage ratchet (`rust/tools/opcode_coverage/sc_usage.py --check`): the lists
/// of opcodes without a Rust type and of packets never sent may only shrink. Needs only python and
/// the checked-in sources; skipped with a notice when no python interpreter is installed.
fn sc_ratchet() -> Result<()> {
    let script = workspace_dir().join("tools/opcode_coverage/sc_usage.py");
    for python in ["python3", "python"] {
        let mut cmd = Command::new(python);
        cmd.arg(&script).arg("--check");
        match cmd.status() {
            Ok(status) if status.success() => return Ok(()),
            Ok(status) => bail!("{cmd:?} failed ({status})"),
            Err(e) if e.kind() == std::io::ErrorKind::NotFound => continue,
            Err(e) => return Err(e).with_context(|| format!("failed to start {cmd:?}")),
        }
    }
    eprintln!("warning: python not found; skipping the S->C coverage ratchet");
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn package_ids() {
        assert!(package_id_names(
            "path+file:///C:/r/rust/crates/aaemu-game#0.1.0",
            "aaemu-game"
        ));
        assert!(!package_id_names(
            "path+file:///C:/r/rust/crates/aaemu-game#0.1.0",
            "aaemu-login"
        ));
        assert!(package_id_names(
            "path+file:///r/crates/xtask#xtask@0.1.0",
            "xtask"
        ));
        assert!(package_id_names(
            "aaemu-login 0.1.0 (path+file:///r/crates/aaemu-login)",
            "aaemu-login"
        ));
    }

    #[test]
    fn app_files_skipped_on_publish() {
        let p = Path::new;
        assert!(!skip_app_file(p("Config.json"), false));
        assert!(skip_app_file(p("Config.Local.json"), false));
        assert!(!skip_app_file(p("ClientData"), false));
        assert!(!skip_app_file(p("ClientData/readme.txt"), false));
        assert!(skip_app_file(p("ClientData/game_pak"), false));
        assert!(!skip_app_file(p("Data/compact.sqlite3"), false));
        assert!(skip_app_file(p("Data/compact.sqlite3"), true));
        assert!(!skip_app_file(p("Data/Worlds/world_spawns.json"), true));
    }

    #[test]
    fn env_file() {
        let env = parse_env("# c\nDB_USER=root\n\nDB_PASSWORD=\"p=w\"\n");
        assert_eq!(env["DB_USER"], "root");
        assert_eq!(env["DB_PASSWORD"], "p=w");
    }
}
