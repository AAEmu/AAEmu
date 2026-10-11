//! Debug aid: compare dumped solver cases (AAEMU_PHYS_DUMP_SOLVER) between the port and the DLL.
use aaemu_physics::solver::probe::{oracle_raw, word_diff, Case};
fn main() {
    let a: Vec<String> = std::env::args().collect();
    let lines: Vec<String> = std::fs::read_to_string(&a[1])
        .unwrap()
        .lines()
        .map(String::from)
        .collect();
    let exe = std::path::PathBuf::from(&a[2]);
    let ans = oracle_raw(&exe, &lines).unwrap();
    for (i, (l, o)) in lines.iter().zip(&ans).enumerate() {
        let p = Case::parse(l).unwrap().run_port();
        if &p != o {
            println!("case {i} differs:\n{}", word_diff(&p, o));
        } else {
            println!("case {i} ok");
        }
    }
}
