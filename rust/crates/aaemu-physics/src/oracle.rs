//! Test support: run the CryPhysics oracle or fall back to checked-in golden traces.
//!
//! * `AAEMU_PHYS_ORACLE=<path to physoracle.exe>` — when set (Windows dev box with the 10.x
//!   runtime, see the crate README), every golden is **regenerated from the real DLL** and must
//!   match the checked-in file byte for byte; a mismatch fails the test (scenario or oracle drift).
//! * `AAEMU_PHYS_BLESS=1` (with the oracle) — rewrite the golden files instead of failing.
//! * Neither set — the golden files are used as is (CI without the DLL).
//!
//! Optional `AAEMU_PHYS_RT=<runtime dir>` is passed through to the oracle (default
//! `D:\aa-re\cryphysics\harness\rt`).
//!
//! Goldens live in `tests/golden/<name>.trace`, scenarios in `tests/scenarios/<name>.scn`.

use crate::trace::{MathTrace, Trace};
use std::path::{Path, PathBuf};
use std::process::Command;

/// Environment variable naming the oracle executable.
pub const ORACLE_ENV: &str = "AAEMU_PHYS_ORACLE";
/// Environment variable that makes golden mismatches rewrite the golden file.
pub const BLESS_ENV: &str = "AAEMU_PHYS_BLESS";

/// The oracle executable, if configured and present.
#[must_use]
pub fn oracle_path() -> Option<PathBuf> {
    let p = PathBuf::from(std::env::var_os(ORACLE_ENV)?);
    p.is_file().then_some(p)
}

fn bless() -> bool {
    std::env::var_os(BLESS_ENV).is_some_and(|v| v != "0")
}

/// `<crate>/tests/golden`.
#[must_use]
pub fn golden_dir() -> PathBuf {
    Path::new(env!("CARGO_MANIFEST_DIR"))
        .join("tests")
        .join("golden")
}

/// `<crate>/tests/scenarios`.
#[must_use]
pub fn scenario_dir() -> PathBuf {
    Path::new(env!("CARGO_MANIFEST_DIR"))
        .join("tests")
        .join("scenarios")
}

fn run(exe: &Path, args: &[&str]) -> Result<String, String> {
    let out = Command::new(exe)
        .args(args)
        .output()
        .map_err(|e| format!("cannot run {}: {e}", exe.display()))?;
    let text =
        String::from_utf8(out.stdout).map_err(|e| format!("oracle output is not UTF-8: {e}"))?;
    if !out.status.success() {
        return Err(format!(
            "oracle failed ({}): {}",
            out.status,
            text.lines().last().unwrap_or("")
        ));
    }
    Ok(text)
}

/// Run a scenario file through the oracle (raw output).
pub fn run_scenario(exe: &Path, scenario: &Path) -> Result<String, String> {
    run(exe, &[&scenario.to_string_lossy()])
}

/// Run a math probe through the oracle (raw output).
pub fn run_math(exe: &Path, name: &str, count: usize, seed: u64) -> Result<String, String> {
    run(
        exe,
        &["--math", name, &count.to_string(), &seed.to_string()],
    )
}

/// Compare fresh oracle output with a golden file (or bless it), then return the golden text.
fn check_golden(golden: &Path, fresh: Option<String>) -> Result<String, String> {
    let stored = std::fs::read_to_string(golden)
        .ok()
        .map(|s| s.replace("\r\n", "\n"));
    match (fresh, stored) {
        (None, Some(s)) => Ok(s),
        (None, None) => Err(format!(
            "golden {} missing and {ORACLE_ENV} not set",
            golden.display()
        )),
        (Some(f), Some(s)) if f == s => Ok(s),
        (Some(f), stored) => {
            if bless() || stored.is_none() {
                std::fs::write(golden, &f)
                    .map_err(|e| format!("cannot write {}: {e}", golden.display()))?;
                return Ok(f);
            }
            let s = stored.unwrap_or_default();
            let first = f
                .lines()
                .zip(s.lines())
                .enumerate()
                .find(|(_, (a, b))| a != b)
                .map(|(i, (a, b))| format!("line {}:\n  oracle: {a}\n  golden: {b}", i + 1))
                .unwrap_or_else(|| {
                    format!(
                        "length differs ({} vs {} lines)",
                        f.lines().count(),
                        s.lines().count()
                    )
                });
            Err(format!(
                "{} differs from the oracle ({first}); set {BLESS_ENV}=1 to rewrite",
                golden.display()
            ))
        }
    }
}

/// Golden trace of `tests/scenarios/<name>.scn` (regenerated and verified when the oracle is set).
pub fn scenario_trace(name: &str) -> Result<Trace, String> {
    let golden = golden_dir().join(format!("{name}.trace"));
    let fresh = match oracle_path() {
        Some(exe) => Some(run_scenario(
            &exe,
            &scenario_dir().join(format!("{name}.scn")),
        )?),
        None => None,
    };
    Trace::parse(&check_golden(&golden, fresh)?)
}

/// Raw golden text of `tests/scenarios/<name>.scn` (regenerated and verified when the oracle
/// is set), for scenarios whose lines the trace parser does not interpret.
pub fn check_scenario_golden(name: &str) -> Result<String, String> {
    let golden = golden_dir().join(format!("{name}.trace"));
    let fresh = match oracle_path() {
        Some(exe) => Some(run_scenario(
            &exe,
            &scenario_dir().join(format!("{name}.scn")),
        )?),
        None => None,
    };
    check_golden(&golden, fresh)
}

/// Golden math probe `tests/golden/math_<name>.trace` (regenerated and verified when the
/// oracle is set).
pub fn math_trace(name: &str, count: usize, seed: u64) -> Result<MathTrace, String> {
    let golden = golden_dir().join(format!("math_{name}.trace"));
    let fresh = match oracle_path() {
        Some(exe) => Some(run_math(&exe, name, count, seed)?),
        None => None,
    };
    MathTrace::parse(&check_golden(&golden, fresh)?)
}

/// Run an ad-hoc scenario text through the oracle, if available (no golden involved).
pub fn run_scenario_text(text: &str) -> Option<Result<Trace, String>> {
    let exe = oracle_path()?;
    let dir = std::env::temp_dir().join(format!("aaemu-physoracle-{}", std::process::id()));
    if let Err(e) = std::fs::create_dir_all(&dir) {
        return Some(Err(e.to_string()));
    }
    static COUNTER: std::sync::atomic::AtomicUsize = std::sync::atomic::AtomicUsize::new(0);
    let n = COUNTER.fetch_add(1, std::sync::atomic::Ordering::Relaxed);
    let path = dir.join(format!("adhoc{n}.scn"));
    if let Err(e) = std::fs::write(&path, text) {
        return Some(Err(e.to_string()));
    }
    let r = run_scenario(&exe, &path).and_then(|s| Trace::parse(&s));
    let _ = std::fs::remove_file(&path);
    Some(r)
}
