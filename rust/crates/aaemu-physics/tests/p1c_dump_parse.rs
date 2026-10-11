//! P1c test support: the probe's mesh dump (a DLL-built cube, `tests/golden/p1c_dump_cube.trace`)
//! parses back into a `TriMesh`.

mod p1c_common;

#[test]
fn cube_dump_parses() {
    let text =
        std::fs::read_to_string(p1c_common::golden_dir().join("p1c_dump_cube.trace")).unwrap();
    let lines: Vec<&str> = text.lines().collect();
    let (m, used) = p1c_common::parse_mesh_dump(&lines);
    assert_eq!(used, lines.len());
    assert_eq!((m.n_tris, m.n_verts), (12, 8));
    assert_eq!(m.indices.len(), 36);
    assert_eq!(m.topology.len(), 12);
    assert_eq!(m.flags, 0x42);
    assert!(m.tree.is_some());
    assert_eq!(m.islands.as_ref().map(Vec::len), Some(1));
}
