//! P1a: the MSVCR100 routines the primitive geometries need, bit-exact against the real CRT.

use aaemu_physics::math::crt;
use aaemu_physics::oracle::{math_trace, oracle_path, run_math};
use aaemu_physics::trace::MathTrace;

fn mismatches(t: &MathTrace) -> Vec<(u32, u32, u32)> {
    t.rows
        .iter()
        .map(|(i, o)| {
            (
                i[0] as u32,
                o[0] as u32,
                crt::asinf(f32::from_bits(i[0] as u32)).to_bits(),
            )
        })
        .filter(|(_, want, got)| want != got)
        .collect()
}

#[test]
fn asinf_is_bit_exact() {
    // the shared golden of tests/math_oracle.rs (1000 samples, seed 8)
    let t = math_trace("asinf", 1000, 8).unwrap();
    assert_eq!(mismatches(&t), vec![]);
    // with the oracle: 100,000 fresh samples
    if let Some(exe) = oracle_path() {
        let text = run_math(&exe, "asinf", 100_000, 0xA51F).unwrap();
        let t = MathTrace::parse(&text).unwrap();
        assert_eq!(t.rows.len(), 100_000);
        let bad = mismatches(&t);
        assert!(
            bad.is_empty(),
            "{} mismatches, e.g. {:08x?}",
            bad.len(),
            &bad[..bad.len().min(5)]
        );
    }
}

#[test]
fn asinf_special_values() {
    assert_eq!(crt::asinf(1.0).to_bits(), 0x3FC9_0FDB);
    assert_eq!(crt::asinf(-1.0).to_bits(), 0xBFC9_0FDB);
    assert_eq!(crt::asinf(0.0).to_bits(), 0);
    assert_eq!(crt::asinf(-0.0).to_bits(), 0x8000_0000);
    assert_eq!(crt::asinf(1e-6), 1e-6);
    assert_eq!(crt::asinf(1.5).to_bits(), 0xFFC0_0000);
    assert!(crt::asinf(f32::NAN).is_nan());
}
