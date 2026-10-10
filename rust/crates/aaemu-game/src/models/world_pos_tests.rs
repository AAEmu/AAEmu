//! Port of AAEmu.UnitTests/Game/Models/Game/World/WorldPosTests.cs
//!
//! `WorldPos` is a `Copy` value type in Rust (`models::movement::WorldPos`): `Clone()` returns
//! an independent copy, so "not the same reference" becomes "mutating the copy leaves the
//! original unchanged".

use super::WorldPos;

/// `new WorldPos(x, y, z)`.
fn world_pos(x: i64, y: i64, z: f32) -> WorldPos {
    WorldPos { x, y, z }
}

#[test]
fn constructor_should_initialize_properties() {
    let pos = world_pos(100, 200, 50.5);
    assert_eq!(pos.x, 100);
    assert_eq!(pos.y, 200);
    assert_eq!(pos.z, 50.5);
}

#[test]
fn default_constructor_should_initialize_to_zero() {
    let pos = WorldPos::default();
    assert_eq!(pos.x, 0);
    assert_eq!(pos.y, 0);
    assert_eq!(pos.z, 0.0);
}

#[test]
fn clone_should_return_new_instance_with_same_values() {
    let original = world_pos(100, 200, 50.5);
    #[allow(clippy::clone_on_copy)]
    let mut clone = original.clone();
    assert_eq!(clone.x, original.x);
    assert_eq!(clone.y, original.y);
    assert_eq!(clone.z, original.z);
    // IsNotSameReferenceAs: the clone is a separate value.
    clone.x = 1;
    assert_eq!(clone.x, 1);
    assert_eq!(original.x, 100);
}

#[test]
fn clone_should_handle_various_values() {
    for (x, y, z) in [
        (0, 0, 0.0),
        (1, 2, 3.0),
        (-100, -200, -50.5),
        (i64::MAX, i64::MIN, f32::MAX),
    ] {
        let original = world_pos(x, y, z);
        #[allow(clippy::clone_on_copy)]
        let clone = original.clone();
        assert_eq!(clone.x, original.x);
        assert_eq!(clone.y, original.y);
        assert_eq!(clone.z, original.z);
    }
}

#[test]
#[allow(clippy::field_reassign_with_default)]
fn properties_can_be_modified() {
    let mut pos = WorldPos::default();
    pos.x = 10;
    pos.y = 20;
    pos.z = 30.5;
    assert_eq!(pos.x, 10);
    assert_eq!(pos.y, 20);
    assert_eq!(pos.z, 30.5);
}
