//! Port of `AAEmu.Commons.Utils.Helper` (`CheckInterval` / `Triggered` extension methods).
//!
//! In C# the state is keyed by `(caller object, [CallerLineNumber])`. Here the caller passes
//! any hashable key identifying the object (an id, a pointer address, ...) and the call site is
//! captured with `#[track_caller]` (file, line and column), so several calls in one function
//! get independent state. Like the C# version, entries are never removed.

use std::collections::hash_map::DefaultHasher;
use std::collections::HashMap;
use std::hash::{Hash, Hasher};
use std::panic::Location;
use std::sync::{Mutex, OnceLock};
use std::time::{Duration, Instant};

type Key = (u64, &'static str, u32, u32);

fn key<K: Hash + ?Sized>(caller: &K, loc: &'static Location<'static>) -> Key {
    let mut h = DefaultHasher::new();
    caller.hash(&mut h);
    (h.finish(), loc.file(), loc.line(), loc.column())
}

fn intervals() -> &'static Mutex<HashMap<Key, Instant>> {
    static M: OnceLock<Mutex<HashMap<Key, Instant>>> = OnceLock::new();
    M.get_or_init(Default::default)
}

fn conditions() -> &'static Mutex<HashMap<Key, bool>> {
    static M: OnceLock<Mutex<HashMap<Key, bool>>> = OnceLock::new();
    M.get_or_init(Default::default)
}

/// `CheckInterval`: `true` if at least `interval_ms` passed since the last time this returned
/// `true` for this caller and call site (always `true` the first time).
#[track_caller]
pub fn check_interval<K: Hash + ?Sized>(caller: &K, interval_ms: u64) -> bool {
    let k = key(caller, Location::caller());
    let now = Instant::now();
    let mut map = intervals().lock().unwrap_or_else(|e| e.into_inner());
    if let Some(&next) = map.get(&k) {
        if next > now {
            return false;
        }
    }
    map.insert(k, now + Duration::from_millis(interval_ms));
    true
}

/// `Triggered`: `true` when `condition` changed from `false` to `true` since the previous call
/// for this caller and call site.
#[track_caller]
pub fn triggered<K: Hash + ?Sized>(caller: &K, condition: bool) -> bool {
    let k = key(caller, Location::caller());
    let mut map = conditions().lock().unwrap_or_else(|e| e.into_inner());
    let old = map.insert(k, condition).unwrap_or(false);
    condition && !old
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn interval() {
        let caller = "interval-test";
        let call = || check_interval(caller, 60_000);
        assert!(call());
        assert!(!call());
        // A different call site has its own timer.
        assert!(check_interval(caller, 60_000));
        // Zero interval always passes.
        let zero = || check_interval(&1u32, 0);
        assert!(zero());
        assert!(zero());
    }

    #[test]
    fn trigger() {
        let t = |c| triggered(&42u64, c);
        assert!(!t(false));
        assert!(t(true));
        assert!(!t(true));
        assert!(!t(false));
        assert!(t(true));
    }
}
