//! P4b debugging aid (ignored): `AAEMU_PHYS_P4B_CASE=<file with S lines>` runs each case
//! through the oracle and the port and prints both answers.

use aaemu_physics::oracle::oracle_path;
use aaemu_physics::solver::probe::{canon_nan, oracle_raw, word_diff, Case};

#[test]
#[ignore = "debugging aid"]
fn p4b_debug_case() {
    let Some(path) = std::env::var_os("AAEMU_PHYS_P4B_CASE") else {
        return;
    };
    let text = std::fs::read_to_string(path).expect("case file");
    let lines: Vec<String> = text
        .lines()
        .map(|l| l.trim().trim_start_matches("case:").trim().to_string())
        .filter(|l| l.starts_with("S "))
        .collect();
    let answers = oracle_path().map(|exe| oracle_raw(&exe, &lines).expect("oracle"));
    for (i, l) in lines.iter().enumerate() {
        let case = Case::parse(l).expect("parse");
        let (port, res) = case.run_port_full();
        println!("case {i}: {res:?}");
        if let Some(a) = &answers {
            println!("oracle: {}", a[i]);
            println!("port:   {port}");
            println!("diff:  {}", word_diff(&canon_nan(&a[i]), &canon_nan(&port)));
        } else {
            println!("port:   {port}");
        }
    }
}
