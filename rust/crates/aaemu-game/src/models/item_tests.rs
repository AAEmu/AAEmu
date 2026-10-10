//! Port of AAEmu.UnitTests/Game/Models/Game/Items/ItemTests.cs
//!
//! `new Item()` takes `AppConfiguration.Id` in C#; the Rust model has no global configuration,
//! so the world id is passed explicitly ([`WORLD_ID`]). The C# `Item.Template` reference is the
//! template id in Rust (templates live in `ItemData`), C# `DateTime`s are unix seconds and the
//! C# property setters that mark the item dirty are `set_grade` / `set_count` / `set_slot` /
//! `set_flag` / `remove_flag` / `set_ucc` (plain field writes do not touch `dirty`).

use std::cmp::Ordering;

use crate::data::items::{
    ItemTemplate, ITEM_APPRAISAL_CERTIFICATE, ITEM_BOUND_TAX_CERTIFICATE, ITEM_COINS,
    ITEM_CREST_INK, ITEM_CREST_STAMP, ITEM_DAWN_STONE, ITEM_SALON_CERTIFICATE, ITEM_SHEET_MUSIC,
    ITEM_TAX_CERTIFICATE, ITEM_TREASURE_MAP_WITH_COORDINATES,
};
use crate::models::item::{Item, ItemClass, ITEM_FLAG_HAS_UCC, ITEM_FLAG_SOUL_BOUND};

/// `AppConfiguration.Instance.Id` stand-in for `new Item()`.
const WORLD_ID: u8 = 1;

/// `ItemFlag.None`.
const ITEM_FLAG_NONE: u8 = 0;

fn named_template(id: u32, name: &str) -> ItemTemplate {
    let mut t = ItemTemplate::plain(id, 0);
    t.name = name.to_string();
    t
}

fn item_with_slot(slot: i32) -> Item {
    let mut i = Item::new(WORLD_ID);
    i.set_slot(slot);
    i
}

// ---- Constructor Tests ----

#[test]
fn constructor_default_creates_item_with_default_values() {
    let item = Item::new(WORLD_ID);

    assert_eq!(item.id, 0);
    assert_eq!(item.template_id, 0);
    assert_eq!(item.count, 0);
    assert_eq!(item.slot, -1);
    assert_eq!(item.grade, 0);
    // `Template` is null: no template id, plain `Item` class.
    assert_eq!(item.template_id, 0);
    assert_eq!(item.class, ItemClass::Item);
    assert_eq!(item.flags, ITEM_FLAG_NONE);
}

#[test]
fn constructor_with_world_id_sets_world_id() {
    let world_id: u8 = 1;

    let item = Item::new(world_id);

    assert_eq!(item.world_id, world_id);
}

#[test]
fn constructor_with_id_template_and_count_creates_item_with_values() {
    let id: u64 = 1000;
    let template = named_template(100, "Test Item");
    let count = 5;

    let item = Item::with_template(WORLD_ID, id, &template, count);

    assert_eq!(item.id, id);
    assert_eq!(item.template_id, template.id);
    assert_eq!(item.count, count);
    assert_eq!(item.slot, -1);
}

#[test]
fn constructor_with_all_parameters_creates_item_with_all_values() {
    let world_id: u8 = 2;
    let id: u64 = 2000;
    let template = named_template(200, "Another Item");
    let count = 10;

    let item = Item::with_template(world_id, id, &template, count);

    assert_eq!(item.world_id, world_id);
    assert_eq!(item.id, id);
    assert_eq!(item.template_id, template.id);
    assert_eq!(item.count, count);
}

// ---- Property Tests ----

#[test]
fn grade_set_and_get_returns_correct_value() {
    let mut item = Item::new(WORLD_ID);
    item.set_grade(5);
    assert_eq!(item.grade, 5);
}

#[test]
fn item_flags_set_and_get_returns_correct_value() {
    let mut item = Item::new(WORLD_ID);
    item.flags = ITEM_FLAG_SOUL_BOUND;
    assert_eq!(item.flags, ITEM_FLAG_SOUL_BOUND);
}

#[test]
fn item_flags_can_combine_flags() {
    let mut item = Item::new(WORLD_ID);
    item.flags = ITEM_FLAG_SOUL_BOUND | ITEM_FLAG_HAS_UCC;
    assert_eq!(item.flags, ITEM_FLAG_SOUL_BOUND | ITEM_FLAG_HAS_UCC);
}

#[test]
fn lifespan_mins_set_and_get_returns_correct_value() {
    let mut item = Item::new(WORLD_ID);
    item.lifespan_mins = 60;
    assert_eq!(item.lifespan_mins, 60);
}

#[test]
fn ucc_id_set_value_sets_has_ucc_flag() {
    let mut item = Item::new(WORLD_ID);

    item.set_ucc(123);

    assert_eq!(item.ucc_id, 123);
    assert!(item.has_flag(ITEM_FLAG_HAS_UCC));
}

#[test]
fn ucc_id_set_zero_removes_has_ucc_flag() {
    let mut item = Item::new(WORLD_ID);
    item.set_ucc(123);

    item.set_ucc(0);

    assert_eq!(item.ucc_id, 0);
    assert!(!item.has_flag(ITEM_FLAG_HAS_UCC));
}

#[test]
fn expiration_time_set_value_updates_value() {
    let mut item = Item::new(WORLD_ID);
    // DateTime.UtcNow.AddDays(7) as unix seconds.
    let expiration_time = chrono::Utc::now().timestamp() + 7 * 24 * 3600;

    item.expiration_time = expiration_time;

    assert_eq!(item.expiration_time, expiration_time);
}

#[test]
fn expiration_online_minutes_left_set_value_updates_value() {
    let mut item = Item::new(WORLD_ID);
    item.expiration_online_minutes_left = 120.5;
    assert_eq!(item.expiration_online_minutes_left, 120.5);
}

#[test]
fn charge_count_set_and_get_returns_correct_value() {
    let mut item = Item::new(WORLD_ID);
    item.charge_count = 10;
    assert_eq!(item.charge_count, 10);
}

#[test]
fn charge_start_time_set_and_get_returns_correct_value() {
    let mut item = Item::new(WORLD_ID);
    let start_time = chrono::Utc::now().timestamp();

    item.charge_start_time = start_time;

    assert_eq!(item.charge_start_time, start_time);
}

// ---- Flag Manipulation Tests ----

#[test]
fn set_flag_adds_flag() {
    let mut item = Item::new(WORLD_ID);
    item.set_flag(ITEM_FLAG_SOUL_BOUND);
    assert!(item.has_flag(ITEM_FLAG_SOUL_BOUND));
}

#[test]
fn remove_flag_removes_flag() {
    let mut item = Item::new(WORLD_ID);
    item.flags = ITEM_FLAG_SOUL_BOUND | ITEM_FLAG_HAS_UCC;

    item.remove_flag(ITEM_FLAG_HAS_UCC);

    assert!(!item.has_flag(ITEM_FLAG_HAS_UCC));
    assert!(item.has_flag(ITEM_FLAG_SOUL_BOUND));
}

// ---- Static Properties Tests ----

#[test]
fn dawn_stone_returns_correct_id() {
    assert_eq!(ITEM_DAWN_STONE, 327);
}

#[test]
fn coins_returns_correct_id() {
    assert_eq!(ITEM_COINS, 500);
}

#[test]
fn tax_certificate_returns_correct_id() {
    assert_eq!(ITEM_TAX_CERTIFICATE, 31891);
}

#[test]
fn bound_tax_certificate_returns_correct_id() {
    assert_eq!(ITEM_BOUND_TAX_CERTIFICATE, 31892);
}

#[test]
fn appraisal_certificate_returns_correct_id() {
    assert_eq!(ITEM_APPRAISAL_CERTIFICATE, 28085);
}

#[test]
fn crest_stamp_returns_correct_id() {
    assert_eq!(ITEM_CREST_STAMP, 17662);
}

#[test]
fn crest_ink_returns_correct_id() {
    assert_eq!(ITEM_CREST_INK, 17663);
}

#[test]
fn sheet_music_returns_correct_id() {
    assert_eq!(ITEM_SHEET_MUSIC, 28051);
}

#[test]
fn salon_certificate_returns_correct_id() {
    assert_eq!(ITEM_SALON_CERTIFICATE, 30811);
}

#[test]
fn treasure_map_with_coordinates_returns_correct_id() {
    assert_eq!(ITEM_TREASURE_MAP_WITH_COORDINATES, 24581);
}

// ---- IsDirty Tests ----

#[test]
fn is_dirty_default_is_true() {
    let item = Item::new(WORLD_ID);
    assert!(item.dirty);
}

#[test]
fn is_dirty_set_false_becomes_false() {
    let mut item = Item::new(WORLD_ID);
    item.dirty = false;
    assert!(!item.dirty);
}

#[test]
fn setting_grade_sets_is_dirty_true() {
    let mut item = Item::new(WORLD_ID);
    item.dirty = false;

    item.set_grade(3);

    assert!(item.dirty);
}

#[test]
fn setting_count_sets_is_dirty_true() {
    let mut item = Item::new(WORLD_ID);
    item.dirty = false;

    item.set_count(100);

    assert!(item.dirty);
}

#[test]
fn setting_slot_sets_is_dirty_true() {
    let mut item = Item::new(WORLD_ID);
    item.dirty = false;

    item.set_slot(5);

    assert!(item.dirty);
}

// ---- CompareTo Tests ----
// `CompareTo_NullItem_ReturnsOne` has no Rust equivalent (`cmp_slot` takes `&Item`, there is
// no null item).

#[test]
fn compare_to_item_with_lower_slot_returns_negative() {
    let item1 = item_with_slot(5);
    let item2 = item_with_slot(10);
    assert_eq!(item1.cmp_slot(&item2), Ordering::Less);
}

#[test]
fn compare_to_item_with_higher_slot_returns_positive() {
    let item1 = item_with_slot(10);
    let item2 = item_with_slot(5);
    assert_eq!(item1.cmp_slot(&item2), Ordering::Greater);
}

#[test]
fn compare_to_item_with_same_slot_returns_zero() {
    let item1 = item_with_slot(5);
    let item2 = item_with_slot(5);
    assert_eq!(item1.cmp_slot(&item2), Ordering::Equal);
}

// ---- Edge Cases ----

#[test]
fn count_can_be_zero() {
    let mut item = Item::new(WORLD_ID);
    item.set_count(0);
    assert_eq!(item.count, 0);
}

#[test]
fn count_can_be_negative() {
    let mut item = Item::new(WORLD_ID);
    item.set_count(-1);
    assert_eq!(item.count, -1);
}

#[test]
fn grade_can_be_max_byte() {
    let mut item = Item::new(WORLD_ID);
    item.set_grade(u8::MAX);
    assert_eq!(item.grade, u8::MAX);
}

#[test]
fn id_can_be_max_u_int64() {
    let mut item = Item::new(WORLD_ID);
    item.id = u64::MAX;
    assert_eq!(item.id, u64::MAX);
}

#[test]
fn template_id_can_be_max_u_int32() {
    let mut item = Item::new(WORLD_ID);
    item.template_id = u32::MAX;
    assert_eq!(item.template_id, u32::MAX);
}
