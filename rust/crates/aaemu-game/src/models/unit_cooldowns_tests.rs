//! Port of AAEmu.UnitTests/Game/Models/Game/Units/UnitCooldownsTests.cs
//!
//! `UnitCooldowns` takes the clock as an argument (`now_ms`, unix ms) instead of reading
//! `DateTime.UtcNow`; `Cooldowns.TryAdd(id, UtcNow + d)` becomes an insert of `now + d`.

use super::unit::UnitCooldowns;

const NOW: i64 = 1_700_000_000_000;

#[test]
fn add_cooldown_should_add_cooldown_when_skill_not_exists() {
    let mut cooldowns = UnitCooldowns::default();
    cooldowns.add_cooldown(100, 5000, NOW);
    assert!(cooldowns.cooldowns.contains_key(&100));
}

#[test]
fn add_cooldown_should_not_duplicate_when_skill_already_exists() {
    let mut cooldowns = UnitCooldowns::default();
    cooldowns.add_cooldown(100, 5000, NOW);
    cooldowns.add_cooldown(100, 10000, NOW);
    assert_eq!(cooldowns.cooldowns.len(), 1);
    // The first cooldown is kept (`if (!TryGetValue) TryAdd`).
    assert_eq!(cooldowns.cooldowns[&100], NOW + 5000);
}

#[test]
fn check_cooldown_should_return_false_when_skill_not_exists() {
    let mut cooldowns = UnitCooldowns::default();
    assert!(!cooldowns.check_cooldown(100, NOW));
}

#[test]
fn check_cooldown_should_return_true_when_cooldown_is_active() {
    let mut cooldowns = UnitCooldowns::default();
    cooldowns.cooldowns.insert(100, NOW + 60_000);
    assert!(cooldowns.check_cooldown(100, NOW));
}

#[test]
fn check_cooldown_should_return_false_and_remove_when_cooldown_expired() {
    let mut cooldowns = UnitCooldowns::default();
    cooldowns.cooldowns.insert(100, NOW - 1000);
    assert!(!cooldowns.check_cooldown(100, NOW));
    assert!(!cooldowns.cooldowns.contains_key(&100));
}

#[test]
fn remove_cooldown_should_remove_skill_when_exists() {
    let mut cooldowns = UnitCooldowns::default();
    cooldowns.cooldowns.insert(100, NOW + 60_000);
    cooldowns.remove_cooldown(100);
    assert!(!cooldowns.cooldowns.contains_key(&100));
}

#[test]
fn remove_cooldown_should_not_throw_when_skill_not_exists() {
    let mut cooldowns = UnitCooldowns::default();
    cooldowns.remove_cooldown(100);
    assert!(cooldowns.cooldowns.is_empty());
}

#[test]
fn add_cooldown_should_accept_various_skill_ids() {
    for skill_id in [0u32, 1, 100, 999_999] {
        let mut cooldowns = UnitCooldowns::default();
        cooldowns.add_cooldown(skill_id, 5000, NOW);
        assert!(cooldowns.cooldowns.contains_key(&skill_id), "{skill_id}");
    }
}

#[test]
fn check_cooldown_should_handle_various_durations() {
    for duration in [0u32, 100, 60_000, u32::MAX] {
        let mut cooldowns = UnitCooldowns::default();
        cooldowns.cooldowns.insert(100, NOW + i64::from(duration));
        let result = cooldowns.check_cooldown(100, NOW);
        // Only more than 250 ms left counts as "active".
        assert_eq!(result, duration > 250, "{duration}");
    }
}
