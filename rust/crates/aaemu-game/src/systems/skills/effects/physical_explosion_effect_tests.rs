//! Port of AAEmu.UnitTests/Game/Models/Game/Skills/Effects/PhysicalExplosionEffectTests.cs

use super::unit_fx::compute_knock_displacement;

fn len(v: [f32; 3]) -> f32 {
    (v[0] * v[0] + v[1] * v[1] + v[2] * v[2]).sqrt()
}

#[test]
fn compute_knock_displacement_outside_radius_returns_null() {
    let result = compute_knock_displacement([0.0; 3], [20.0, 0.0, 0.0], 10.0, 1.0, 5000.0);
    assert!(result.is_none());
}

#[test]
fn compute_knock_displacement_inside_hole_size_returns_null() {
    let result = compute_knock_displacement([0.0; 3], [0.5, 0.0, 0.0], 10.0, 1.0, 5000.0);
    assert!(result.is_none());
}

#[test]
fn compute_knock_displacement_below_min_perceptible_knock_returns_null() {
    // 0.1 m base x 0.05 falloff < 0.5 m
    let result = compute_knock_displacement([0.0; 3], [9.5, 0.0, 0.0], 10.0, 1.0, 100.0);
    assert!(result.is_none());
}

#[test]
fn compute_knock_displacement_at_five_metres_knocks_outward_along_push_direction() {
    // 5 m base x (1 - 5/10) = 2.5 m along +X
    let d =
        compute_knock_displacement([0.0; 3], [5.0, 0.0, 0.0], 10.0, 1.0, 5000.0).expect("knocked");
    assert!(d[0] > 0.0);
    assert!(d[1].abs() < 0.001);
    assert!((len(d) - 2.5).abs() < 0.01);
}

#[test]
fn compute_knock_displacement_falloff_scales_linearly_with_distance() {
    let near = compute_knock_displacement([0.0; 3], [1.0, 0.0, 0.0], 10.0, 0.5, 5000.0);
    let far = compute_knock_displacement([0.0; 3], [9.0, 0.0, 0.0], 10.0, 0.5, 5000.0);
    let (near, far) = (near.expect("near"), far.expect("far"));
    assert!(len(near) > len(far));
}
