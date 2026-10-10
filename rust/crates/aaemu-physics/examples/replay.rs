fn main() {
    let a: Vec<String> = std::env::args().collect();
    let scn = std::fs::read_to_string(&a[1]).unwrap();
    match aaemu_physics::scenario::Scenario::run(&scn) {
        Ok(s) => print!("{s}"),
        Err(e) => eprintln!("ERR {e}"),
    }
}
