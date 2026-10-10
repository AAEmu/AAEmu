//! Port of AAEmu.UnitTests/Game/Models/Game/Units/CheckBuffsExcludingTagsTests.cs
//!
//! `Buffs.CheckBuffsExcludingTags` takes the buff-tag lookup (`SkillManager.GetBuffTags`) as an
//! argument, so the matching paths the C# leaves to integration tests are covered here too.

use super::buff::{Buff, Buffs, SkillCaster};
use crate::data::skills::{BuffKind, BuffTemplate};

fn no_tags(_: u32) -> &'static [u32] {
    &[]
}

#[test]
fn returns_false_when_id_list_null() {
    let buffs = Buffs::default();
    assert!(!buffs.check_buffs_excluding_tags(None, &[42], no_tags));
}

#[test]
fn returns_false_when_id_list_empty() {
    let buffs = Buffs::default();
    assert!(!buffs.check_buffs_excluding_tags(Some(&[]), &[42], no_tags));
}

#[test]
fn returns_false_when_no_active_effects() {
    let buffs = Buffs::default();
    assert!(!buffs.check_buffs_excluding_tags(Some(&[1, 2, 3]), &[42], no_tags));
}

// ---- matching paths (not reachable without the SkillManager singleton in the C# tests) ----

fn with_buff(id: u32) -> Buffs {
    let mut buffs = Buffs::default();
    let b = Buff::new(
        BuffTemplate::simple(id, BuffKind::Good),
        SkillCaster::default(),
        None,
        0,
    );
    buffs.add_buff(b, 0, 0, 0);
    buffs
}

#[test]
fn returns_true_when_listed_effect_has_no_excluded_tag() {
    let buffs = with_buff(2);
    let tags = |id: u32| -> &'static [u32] {
        if id == 2 {
            &[7]
        } else {
            &[]
        }
    };
    assert!(buffs.check_buffs_excluding_tags(Some(&[1, 2, 3]), &[42], tags));
}

#[test]
fn returns_false_when_listed_effect_has_excluded_tag() {
    let buffs = with_buff(2);
    let tags = |id: u32| -> &'static [u32] {
        if id == 2 {
            &[7, 42]
        } else {
            &[]
        }
    };
    assert!(!buffs.check_buffs_excluding_tags(Some(&[1, 2, 3]), &[42], tags));
}

#[test]
fn returns_false_when_active_effect_not_listed() {
    let buffs = with_buff(9);
    assert!(!buffs.check_buffs_excluding_tags(Some(&[1, 2, 3]), &[42], no_tags));
}
