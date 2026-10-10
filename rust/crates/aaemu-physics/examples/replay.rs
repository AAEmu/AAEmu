fn main() {
    let a: Vec<String> = std::env::args().collect();
    let scn = std::fs::read_to_string(&a[1]).unwrap();
    let dir = std::path::Path::new(&a[1]).parent();
    match aaemu_physics::scenario::Scenario::run_in(&scn, dir) {
        Ok(s) => print!("{s}"),
        Err(e) => eprintln!("ERR {e}"),
    }
}
