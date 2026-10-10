//! P1a: slot 14 CalculateMediumResistance, bit-exact against the oracle.

mod p1a_support;

use p1a_support::*;

fn run(cs: &[Case]) -> Vec<String> {
    let mut fails = Vec::new();
    for c in cs {
        let a = AnyGeom::of(c);
        let mut dp = c.v(28);
        let mut dl = c.v(31);
        a.g()
            .calculate_medium_resistance_acc(&c.plane(0), &c.gwd(6, true), &mut dp, &mut dl);
        let mut o = Vec::new();
        push_v(&mut o, dp);
        push_v(&mut o, dl);
        check(c, &o, &mut fails);
    }
    fails
}

#[test]
fn medium_resistance_matches_the_oracle() {
    let cs = cases("p1a_drag");
    let fails = run(&cs);
    finish("CalculateMediumResistance", cs.len(), &fails);
}

/// Large fuzz runs straight from the oracle (only with `AAEMU_PHYS_ORACLE`; no goldens).
#[test]
fn medium_resistance_large_fuzz_with_oracle() {
    let text = "gprobe probe=res type=box n=3000 seed=5101\n\
                gprobe probe=res type=sphere n=4000 seed=5102\n\
                gprobe probe=res type=cylinder n=6000 seed=5103\n\
                gprobe probe=res type=capsule n=3000 seed=5104\n\
                gprobe probe=res type=cylinder n=2000 seed=5105 mode=1\n\
                gprobe probe=res type=cylinder n=1000 seed=5106 mode=2\n\
                gprobe probe=res type=cylinder n=500 seed=5107 mode=3\n\
                gprobe probe=res type=sphere n=1000 seed=5108 mode=1\n";
    let Some(cs) = adhoc_cases(text) else {
        return;
    };
    let fails = run(&cs);
    finish("medium resistance fuzz", cs.len(), &fails);
}
