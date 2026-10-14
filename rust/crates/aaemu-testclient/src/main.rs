//! `aaemu-testclient` — headless 1.2 client bot.
//!
//! ```text
//! aaemu-testclient run    [--config F] [--login H:P] [--user U] [--password P] [--character C]
//!                         [--scenario login|lobby|full|combat|npc-return|load] [--out capture.jsonl]
//!                         [--npc-return-distance M] [--npc-return-away-secs S]
//! aaemu-testclient parity [--config F] --a H:P --b H:P [--a-label cs] [--b-label rs]
//!                         [--user U] [--scenario full] [--masks masks.json] [--out-dir D]
//!                         [--rounds N]
//! aaemu-testclient diff   --a capA.jsonl --b capB.jsonl [--masks masks.json]
//! aaemu-testclient load   [--config F] [--login H:P] --bots N [--prefix load] [--ramp-ms 20]
//!                         [--scenario load]
//! ```
//!
//! Settings come from `--config` (JSON, see `testclient.example.json`), then the flags. Keep
//! real test credentials in a git-ignored `testclient.local.json`.

use std::collections::{BTreeMap, HashMap};
use std::path::{Path, PathBuf};
use std::time::Duration;

use aaemu_testclient::diff::{self, DiffConfig};
use aaemu_testclient::load;
use aaemu_testclient::record::{self, Recorder};
use aaemu_testclient::scenario::{self, BotConfig, Kind, Outcome};
use anyhow::{bail, Context};
use tracing_subscriber::EnvFilter;

struct Args {
    cmd: String,
    flags: HashMap<String, String>,
}

impl Args {
    fn parse() -> anyhow::Result<Self> {
        let mut it = std::env::args().skip(1);
        let cmd = it.next().unwrap_or_else(|| "help".into());
        let mut flags = HashMap::new();
        while let Some(a) = it.next() {
            let Some(k) = a.strip_prefix("--") else {
                bail!("unexpected argument '{a}'");
            };
            let v = it.next().with_context(|| format!("--{k} needs a value"))?;
            flags.insert(k.to_string(), v);
        }
        Ok(Self { cmd, flags })
    }
    fn get(&self, k: &str) -> Option<&str> {
        self.flags.get(k).map(String::as_str)
    }
}

fn bot_config(args: &Args) -> anyhow::Result<BotConfig> {
    let mut v: serde_json::Value = match args.get("config") {
        Some(p) => serde_json::from_str(
            &std::fs::read_to_string(p).with_context(|| format!("reading {p}"))?,
        )?,
        None => serde_json::json!({}),
    };
    let obj = v.as_object_mut().context("config must be a JSON object")?;
    for (flag, key) in [
        ("login", "login"),
        ("user", "user"),
        ("password", "password"),
        ("character", "character"),
    ] {
        if let Some(x) = args.get(flag) {
            obj.insert(key.into(), x.into());
        }
    }
    for (flag, key) in [
        ("skill", "skill_id"),
        ("quest", "quest_id"),
        ("gs", "game_server_id"),
    ] {
        if let Some(x) = args.get(flag) {
            obj.insert(key.into(), x.parse::<u64>()?.into());
        }
    }
    if let Some(x) = args.get("npc-return-distance") {
        obj.insert("npc_return_distance".into(), x.parse::<f64>()?.into());
    }
    if let Some(x) = args.get("npc-return-away-secs") {
        obj.insert("npc_return_away_secs".into(), x.parse::<u64>()?.into());
    }
    obj.entry("login").or_insert("127.0.0.1:1237".into());
    obj.entry("password").or_insert("testpass".into());
    if !obj.contains_key("character") {
        let user = obj
            .get("user")
            .and_then(|u| u.as_str())
            .unwrap_or("bot")
            .to_string();
        obj.insert("character".into(), user.into());
    }
    Ok(serde_json::from_value(v)?)
}

fn print_outcome(label: &str, o: &Outcome) {
    for s in &o.steps {
        println!(
            "[{label}] {:<5} {:<20} {:>6} ms  {}",
            if s.ok { "ok" } else { "FAIL" },
            s.name,
            s.ms,
            s.note
        );
    }
}

fn vars_path(capture: &Path) -> PathBuf {
    capture.with_extension("vars.json")
}

async fn run_one(
    kind: Kind,
    cfg: &BotConfig,
    out: Option<&Path>,
    label: &str,
) -> anyhow::Result<Outcome> {
    let rec = Recorder::new(true);
    let o = scenario::run(kind, cfg, &rec).await;
    print_outcome(label, &o);
    if let Some(p) = out {
        rec.save(p)?;
        std::fs::write(vars_path(p), serde_json::to_string_pretty(&o.vars)?)?;
        println!(
            "[{label}] {} frames captured to {}",
            rec.events().len(),
            p.display()
        );
    }
    Ok(o)
}

fn load_masks(args: &Args) -> anyhow::Result<DiffConfig> {
    Ok(match args.get("masks") {
        Some(p) => serde_json::from_str(&std::fs::read_to_string(p)?)?,
        None => DiffConfig::default(),
    })
}

fn read_vars(capture: &Path) -> BTreeMap<String, u64> {
    std::fs::read_to_string(vars_path(capture))
        .ok()
        .and_then(|s| serde_json::from_str(&s).ok())
        .unwrap_or_default()
}

fn diff_files(a: &Path, b: &Path, cfg: &DiffConfig, la: &str, lb: &str) -> anyhow::Result<usize> {
    let ea = record::load(a)?;
    let eb = record::load(b)?;
    let r = diff::diff(&ea, &read_vars(a), &eb, &read_vars(b), cfg);
    println!("{}", r.render(la, lb));
    Ok(r.findings.len())
}

#[tokio::main]
async fn main() -> anyhow::Result<()> {
    tracing_subscriber::fmt()
        .with_env_filter(
            EnvFilter::try_from_default_env().unwrap_or_else(|_| EnvFilter::new("warn")),
        )
        .init();
    let args = Args::parse()?;
    let kind: Kind = args.get("scenario").unwrap_or("full").parse()?;
    match args.cmd.as_str() {
        "run" => {
            let cfg = bot_config(&args)?;
            let out = args.get("out").map(PathBuf::from);
            let o = run_one(kind, &cfg, out.as_deref(), "run").await?;
            if !o.ok() {
                bail!("scenario failed: {}", o.error.unwrap_or_default());
            }
        }
        "parity" => {
            let base = bot_config(&args)?;
            let dir = PathBuf::from(args.get("out-dir").unwrap_or("."));
            std::fs::create_dir_all(&dir)?;
            let la = args.get("a-label").unwrap_or("cs").to_string();
            let lb = args.get("b-label").unwrap_or("rs").to_string();
            let scen = args.get("scenario").unwrap_or("full");
            // `--rounds 2` runs the scenario again with the same account: the second round
            // covers the "existing character, fresh session" path.
            let rounds: usize = args.get("rounds").unwrap_or("1").parse()?;
            let masks = load_masks(&args)?;
            let mut total = 0;
            for round in 1..=rounds {
                let mut paths = Vec::new();
                for (label, addr) in [(&la, args.get("a")), (&lb, args.get("b"))] {
                    let mut cfg = base.clone();
                    cfg.login = addr
                        .context("--a and --b login addresses are required")?
                        .to_string();
                    let name = if rounds > 1 {
                        format!("{scen}-r{round}-{label}.jsonl")
                    } else {
                        format!("{scen}-{label}.jsonl")
                    };
                    let p = dir.join(name);
                    let o = run_one(kind, &cfg, Some(&p), label).await?;
                    if !o.ok() {
                        println!("[{label}] scenario failed: {}", o.error.unwrap_or_default());
                    }
                    paths.push(p);
                }
                println!("--- round {round}");
                total += diff_files(&paths[0], &paths[1], &masks, &la, &lb)?;
            }
            if total > 0 {
                std::process::exit(2);
            }
        }
        "diff" => {
            let a = PathBuf::from(args.get("a").context("--a capture")?);
            let b = PathBuf::from(args.get("b").context("--b capture")?);
            let n = diff_files(&a, &b, &load_masks(&args)?, "a", "b")?;
            if n > 0 {
                std::process::exit(2);
            }
        }
        "load" => {
            let cfg = bot_config(&args)?;
            let n: usize = args.get("bots").unwrap_or("50").parse()?;
            let prefix = args.get("prefix").unwrap_or("load");
            let ramp = Duration::from_millis(args.get("ramp-ms").unwrap_or("20").parse()?);
            let kind: Kind = args.get("scenario").unwrap_or("load").parse()?;
            let r = load::run(kind, &cfg, prefix, n, ramp).await;
            println!("{}", r.render());
            if r.ok != r.bots {
                std::process::exit(2);
            }
        }
        _ => {
            println!("usage: aaemu-testclient run|parity|diff|load [--flags]; see the crate docs");
        }
    }
    Ok(())
}
