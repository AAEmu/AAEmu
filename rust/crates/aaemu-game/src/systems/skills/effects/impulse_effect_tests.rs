//! Port of AAEmu.UnitTests/Game/Models/Game/Skills/Effects/ImpulseEffectTests.cs
//!
//! `local_impulse_to_world_displacement` takes the horizontal (x, y) positions only: the C#
//! zeroes the direction's Z, so the vertical offset never takes part.

use super::unit_fx::local_impulse_to_world_displacement;

fn xy(p: [f32; 3]) -> (f32, f32) {
    (p[0], p[1])
}

#[test]
fn local_impulse_to_world_displacement_degenerate_direction_returns_unit_scaled_impulse() {
    let pos = [100.0, 100.0, 50.0];
    let result = local_impulse_to_world_displacement([0.0, 5000.0, 0.0], xy(pos), xy(pos));
    assert_eq!(result, [0.0, 5.0, 0.0]);
}

#[test]
fn local_impulse_to_world_displacement_target_east_of_caster_forward_impulse_pushes_east() {
    let result = local_impulse_to_world_displacement(
        [0.0, 5000.0, 0.0],
        xy([0.0, 0.0, 0.0]),
        xy([10.0, 0.0, 0.0]),
    );
    assert!((result[0] - 5.0).abs() < 0.001);
    assert!(result[1].abs() < 0.001);
    assert!(result[2].abs() < 0.001);
}

#[test]
fn local_impulse_to_world_displacement_right_impulse_perpendicular_to_forward() {
    // forward = +X, right = (-dir.Y, dir.X, 0) = (0, 1, 0)
    let result = local_impulse_to_world_displacement(
        [3000.0, 0.0, 0.0],
        xy([0.0, 0.0, 0.0]),
        xy([10.0, 0.0, 0.0]),
    );
    assert!(result[0].abs() < 0.001);
    assert!((result[1] - 3.0).abs() < 0.001);
    assert!(result[2].abs() < 0.001);
}

#[test]
fn local_impulse_to_world_displacement_up_impulse_always_along_world_z() {
    let result = local_impulse_to_world_displacement(
        [0.0, 0.0, 4000.0],
        xy([0.0, 0.0, 0.0]),
        xy([7.0, 3.0, 0.0]),
    );
    assert!(result[0].abs() < 0.001);
    assert!(result[1].abs() < 0.001);
    assert!((result[2] - 4.0).abs() < 0.001);
}

#[test]
fn local_impulse_to_world_displacement_z_component_ignored_in_direction_calc() {
    let impulse = [0.0, 5000.0, 0.0];
    let with_z =
        local_impulse_to_world_displacement(impulse, xy([0.0, 0.0, 50.0]), xy([10.0, 0.0, 0.0]));
    let no_z =
        local_impulse_to_world_displacement(impulse, xy([0.0, 0.0, 0.0]), xy([10.0, 0.0, 0.0]));
    assert_eq!(with_z, no_z);
}
