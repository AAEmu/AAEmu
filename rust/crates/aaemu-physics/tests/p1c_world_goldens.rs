//! P1c in the world: the 17 P1c oracle goldens (`tests/golden/p1c_*.trace`, scenarios
//! `tests/scenarios/p1c_*.scn`, see `tests/p1c_scenarios.rs`) replayed on the port through the
//! scenario interpreter ([`aaemu_physics::scenario::Scenario`]) and compared line by line, bit
//! for bit: trimesh geometry built by the ported `CreateMesh` / `LoadPhysGeometry` /
//! `RegisterGeometry`, heightfield terrain through `SetHeightfieldData`, rigid bodies on and
//! with meshes, RWI against meshes and terrain.
//!
//! Scenarios that read real models (`cgf=`) need the model corpus (`AAEMU_PHYS_CGF_DIR`); without
//! it they are skipped (not failed). The goldens are the committed traces (regenerating them
//! through the oracle is `tests/p1c_scenarios.rs`'s job).

use aaemu_physics::oracle::{golden_dir, scenario_dir};
use aaemu_physics::scenario::Scenario;

/// The corpus files the scenario needs are present (`true` for synthetic scenarios).
fn corpus_present(scn: &str) -> bool {
    let paths: Vec<&str> = scn
        .lines()
        .filter(|l| !l.trim_start().starts_with('#'))
        .flat_map(str::split_whitespace)
        .filter_map(|t| t.strip_prefix("cgf="))
        .collect();
    if paths.is_empty() {
        return true;
    }
    let Some(dir) = std::env::var_os("AAEMU_PHYS_CGF_DIR") else {
        return false;
    };
    let dir = std::path::PathBuf::from(dir);
    paths
        .iter()
        .all(|p| dir.join(p.replace('\\', "/")).is_file())
}

fn replay(name: &str) -> Result<(), String> {
    let scn = std::fs::read_to_string(scenario_dir().join(format!("{name}.scn")))
        .map_err(|e| e.to_string())?;
    if !corpus_present(&scn) {
        eprintln!("{name}: model corpus absent (AAEMU_PHYS_CGF_DIR), skipped");
        return Ok(());
    }
    let golden = std::fs::read_to_string(golden_dir().join(format!("{name}.trace")))
        .map_err(|e| e.to_string())?
        .replace("\r\n", "\n");
    let port = Scenario::run_in(&scn, Some(&scenario_dir()))?;
    let mut step = String::new();
    for (i, (a, b)) in port.lines().zip(golden.lines()).enumerate() {
        if b.starts_with("step ") {
            step = b.to_string();
        }
        if a != b {
            return Err(format!(
                "{name}: line {} ({step}) differs\n port:   {a}\n golden: {b}",
                i + 1
            ));
        }
    }
    if port.lines().count() != golden.lines().count() {
        return Err(format!(
            "{name}: {} lines vs golden {}",
            port.lines().count(),
            golden.lines().count()
        ));
    }
    Ok(())
}

macro_rules! golden {
    ($t:ident, $n:literal) => {
        #[test]
        fn $t() {
            if let Err(e) = replay($n) {
                panic!("{e}");
            }
        }
    };
}

golden!(p1c_mesh_gen, "p1c_mesh_gen");
golden!(p1c_drop_meshgrid, "p1c_drop_meshgrid");
golden!(p1c_drop_bumpy, "p1c_drop_bumpy");
golden!(p1c_drop_terrainmesh, "p1c_drop_terrainmesh");
golden!(p1c_drop_terrain_flat, "p1c_drop_terrain_flat");
golden!(p1c_drop_terrain_slope, "p1c_drop_terrain_slope");
golden!(p1c_drop_terrain_bumps, "p1c_drop_terrain_bumps");
golden!(p1c_drop_cgf, "p1c_drop_cgf");
golden!(p1c_mesh_on_meshgrid, "p1c_mesh_on_meshgrid");
golden!(p1c_mesh_on_meshobj, "p1c_mesh_on_meshobj");
golden!(p1c_mesh_on_terrain, "p1c_mesh_on_terrain");
golden!(p1c_mesh_pair, "p1c_mesh_pair");
golden!(p1c_cgf_rigid, "p1c_cgf_rigid");
golden!(p1c_cgf_terrain, "p1c_cgf_terrain");
golden!(p1c_rays_mesh, "p1c_rays_mesh");
golden!(p1c_rays_cgf, "p1c_rays_cgf");
golden!(p1c_rays_terrain, "p1c_rays_terrain");
