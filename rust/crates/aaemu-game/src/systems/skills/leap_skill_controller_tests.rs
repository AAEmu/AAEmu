//! Port of AAEmu.UnitTests/Game/Models/Game/Skills/SkillControllers/LeapSkillControllerTests.cs

use super::controllers::{apply_direction_constraint, MoveDirection, Vec3};

const OWNER: Vec3 = Vec3::new(0.0, 0.0, 0.0);
const TARGET: Vec3 = Vec3::new(10.0, 0.0, 0.0);

#[test]
fn apply_direction_constraint_both_always_returns_candidate() {
    let candidate = Vec3::new(20.0, 0.0, 0.0); // past the target
    let result = apply_direction_constraint(MoveDirection::Both, OWNER, candidate, TARGET);
    assert_eq!(result, candidate);
}

#[test]
fn apply_direction_constraint_forward_only_allowed_when_landing_closer_to_target() {
    let candidate = Vec3::new(7.0, 0.0, 0.0); // 3 m from the target
    let result = apply_direction_constraint(MoveDirection::ForwardOnly, OWNER, candidate, TARGET);
    assert_eq!(result, candidate);
}

#[test]
fn apply_direction_constraint_forward_only_collapses_to_owner_when_landing_farther() {
    let candidate = Vec3::new(-5.0, 0.0, 0.0); // 15 m from the target
    let result = apply_direction_constraint(MoveDirection::ForwardOnly, OWNER, candidate, TARGET);
    assert_eq!(result, OWNER);
}

#[test]
fn apply_direction_constraint_backward_only_allowed_when_landing_farther() {
    let candidate = Vec3::new(-3.0, 0.0, 0.0); // 13 m from the target
    let result = apply_direction_constraint(MoveDirection::BackwardOnly, OWNER, candidate, TARGET);
    assert_eq!(result, candidate);
}

#[test]
fn apply_direction_constraint_backward_only_collapses_to_owner_when_landing_closer() {
    let candidate = Vec3::new(5.0, 0.0, 0.0); // 5 m from the target
    let result = apply_direction_constraint(MoveDirection::BackwardOnly, OWNER, candidate, TARGET);
    assert_eq!(result, OWNER);
}

#[test]
fn apply_direction_constraint_forward_only_boundary_distance_allows_candidate() {
    // Same distance as the owner: ForwardOnly only rejects `>`.
    let candidate = Vec3::new(20.0, 0.0, 0.0);
    let result = apply_direction_constraint(MoveDirection::ForwardOnly, OWNER, candidate, TARGET);
    assert_eq!(result, candidate);
}
