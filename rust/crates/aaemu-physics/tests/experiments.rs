//! Oracle experiments that settle the open questions of CRYPHYSICS-MAP (Appendix A).

use aaemu_physics::math::{Quat, Vec3};
use aaemu_physics::oracle::scenario_trace;
use aaemu_physics::trace::Trace;

fn trace(name: &str) -> Trace {
    scenario_trace(name).unwrap_or_else(|e| panic!("{name}: {e}"))
}

/// `peek` lines as (offset, words).
fn peeks(t: &Trace) -> Vec<(usize, Vec<u32>)> {
    t.other
        .iter()
        .filter(|l| l[0] == "peek")
        .map(|l| {
            (
                l[2].parse().unwrap(),
                l[3..]
                    .iter()
                    .map(|w| u32::from_str_radix(w, 16).unwrap())
                    .collect(),
            )
        })
        .collect()
}

fn quat(w: &[u32]) -> Quat {
    Quat::from_bits([w[0], w[1], w[2], w[3]])
}

/// Q: is the entity orientation `B.q ⊗ qfb` or `qfb ⊗ B.q`?
/// A: `normalize(B.q ⊗ qfb)` with the engine's lane order and `((x²+w²)+y²)+z²` normalisation,
/// bit-exact, at two different steps.
#[test]
fn entity_orientation_is_body_q_times_qfb() {
    let t = trace("exp_quat_order");
    let p = peeks(&t);
    for chunk in [&p[0..4], &p[5..9]] {
        let (q_new, bq, qfb) = (quat(&chunk[1].1), quat(&chunk[2].1), quat(&chunk[3].1));
        assert_eq!(chunk[1].0, 460);
        assert_eq!(chunk[2].0, 756);
        assert_eq!(chunk[3].0, 856);
        assert_eq!((bq * qfb).normalized_xwyz().to_bits(), q_new.to_bits());
        assert_ne!((qfb * bq).normalized_xwyz().to_bits(), q_new.to_bits());
        assert_eq!(
            quat(&chunk[0].1).to_bits(),
            q_new.to_bits(),
            "committed == new after the step"
        );
    }
}

/// Q: are buoyancy impulses applied in the same step or the next?
/// A: the same (sub)step. ApplyBuoyancy calls `Action(action_impulse{iApplyTime=0, iSource=1},
/// bThreadSafe=1)`: applied immediately to P/L (|Δv|,|Δω| clamped to 5, no wake), after the
/// integration of that substep and before gravity. A box released at rest fully submerged has
/// an upward velocity after the first world step, already moved inside it (second substep), and
/// the deferred-impulse accumulator (E+956) is never used.
#[test]
fn buoyancy_applies_in_the_same_step() {
    let t = trace("exp_buoyancy_same_step");
    let s0 = &t.step(0).unwrap().entities[0];
    let s1 = &t.step(1).unwrap().entities[0];
    assert_eq!(s0.v_f(), Some(Vec3::ZERO));
    let v1 = s1.v_f().unwrap();
    assert!(
        v1.z > 0.7,
        "net upward velocity after one step: {v1:?} (a next-step buoyancy would give -0.327)"
    );
    assert!(
        s1.pos_f().z > s0.pos_f().z,
        "already moved within step 1 (substeps)"
    );
    for (off, words) in peeks(&t) {
        if off == 956 {
            assert!(
                words.iter().all(|w| *w == 0),
                "deferred P/L accumulator unused"
            );
        }
    }
}

/// Q: which entity list at world+664+8k holds which simulation class?
/// A: k = simclass: 0 static (664), 1 sleeping rigid (672), 2 active rigid (680), 3 living (688),
/// 4 independent (696; particles are also on the independent stepping list at +760). Entities are
/// linked into the lists by the first TimeStep after creation.
#[test]
fn simclass_lists() {
    let t = trace("exp_simclass_lists");
    let lw: Vec<&Vec<String>> = t.other.iter().filter(|l| l[0] == "listwhere").collect();
    let lists = |i: usize| lw[i][2..].join(" ");
    for i in 0..5 {
        assert_eq!(lists(i), "", "not listed before the first step");
    }
    assert_eq!(lists(5), "664", "static");
    assert_eq!(lists(6), "680", "awake rigid");
    assert_eq!(lists(7), "688", "living");
    assert_eq!(lists(8), "696 760", "particle");
    assert_eq!(lists(9), "680", "rigid without parts");
    assert_eq!(lists(10), "672", "rigid asleep after landing");
}
