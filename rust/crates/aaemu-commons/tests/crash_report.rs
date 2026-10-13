//! `aaemu_commons::crash`: a child process (this test binary re-run with `AAEMU_CRASH_CHILD`)
//! crashes for real and the parent checks the report it left in `Crash.log`.

use std::path::{Path, PathBuf};
use std::process::Command;

const CHILD_ENV: &str = "AAEMU_CRASH_CHILD";

fn temp_dir(name: &str) -> PathBuf {
    let dir = std::env::temp_dir().join(format!("aaemu-crash-{name}-{}", std::process::id()));
    let _ = std::fs::remove_dir_all(&dir);
    std::fs::create_dir_all(&dir).unwrap();
    dir
}

/// Re-runs this test binary for `test` only, as the crashing child, and returns `Crash.log`.
fn run_child(test: &str, dir: &Path) -> String {
    let status = Command::new(std::env::current_exe().unwrap())
        .args([test, "--exact", "--nocapture", "--test-threads=1"])
        .env(CHILD_ENV, dir)
        .status()
        .unwrap();
    assert!(!status.success(), "the child should have crashed");
    std::fs::read_to_string(dir.join("Crash.log")).expect("Crash.log written")
}

#[inline(never)]
fn recurse(n: u64) -> u64 {
    if n == u64::MAX {
        return 0;
    }
    let buf = std::hint::black_box([n; 32]);
    recurse(n + 1).wrapping_add(buf[7])
}

#[test]
fn panic_is_reported_with_backtrace() {
    if let Ok(dir) = std::env::var(CHILD_ENV) {
        aaemu_commons::crash::install(Path::new(&dir));
        std::thread::Builder::new()
            .name("panicker".into())
            .spawn(|| panic!("boom from the test"))
            .unwrap()
            .join()
            .ok();
        std::process::exit(3);
    }
    let dir = temp_dir("panic");
    let log = run_child("panic_is_reported_with_backtrace", &dir);
    assert!(log.contains("PANIC"), "{log}");
    assert!(log.contains("thread 'panicker' panicked at"), "{log}");
    assert!(log.contains("boom from the test"), "{log}");
    assert!(log.contains("stack backtrace:"), "{log}");
    let _ = std::fs::remove_dir_all(&dir);
}

#[cfg(all(windows, target_arch = "x86_64"))]
#[test]
fn native_stack_overflow_is_reported() {
    if let Ok(dir) = std::env::var(CHILD_ENV) {
        aaemu_commons::crash::install(Path::new(&dir));
        let r = std::thread::Builder::new()
            .name("overflower".into())
            .stack_size(256 << 10)
            .spawn(|| recurse(0))
            .unwrap()
            .join();
        unreachable!("{r:?}");
    }
    let dir = temp_dir("overflow");
    let log = run_child("native_stack_overflow_is_reported", &dir);
    assert!(log.contains("stack overflow"), "{log}");
    assert!(log.contains("thread 'overflower'"), "{log}");
    assert!(
        log.contains("most frequent functions on the stack:"),
        "{log}"
    );
    assert!(log.contains("crash_report::recurse"), "{log}");
    let dumps = std::fs::read_dir(dir.join("Crash")).unwrap().count();
    assert_eq!(dumps, 1, "{log}");
    let _ = std::fs::remove_dir_all(&dir);
}
