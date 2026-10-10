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
