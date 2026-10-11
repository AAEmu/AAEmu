//! P1b: BV trees against the real CGF physics blobs of the game data (`AAEMU_PHYS_CGF_DIR`,
//! default the sample CGFs under `D:\aa-re\zone\agent_geom\data\samples`; without data the
//! tests pass vacuously and say so).
//!
//! * `real_blob_trees_roundtrip`: every serialized tree (OBB, AABB, SingleBox) loads and saves
//!   back to the identical bytes, ending exactly at the end of the blob (asserted).
//! * `real_blob_trees_rebuild`: the stored mesh (already in the order the original build left
//!   it) is rebuilt with the stored parameters and compared with the stored tree.
//!
//! The stored trees were made by the resource compiler, not by this DLL, and not always from
//! exactly the vertices that were saved: on the 1,500-model corpus 566 of 962 AABB and 77 of
//! 580 OBB trees rebuild bit-exactly; the others differ by last-bit roundings of the root box /
//! fitted axes (e.g. `ncom_board02`: the stored root implies a minimum x one ulp below every
//! stored vertex). `p1b_oracle.rs` checks the port against the 10.x DLL itself on the same
//! meshes, where every build matches, so the rebuild test only reports its counts.

use aaemu_physics::geometry::bvtree::{
    load_tree, AabbTree, BuildMesh, MemReader, MemWriter, ObbTree,
};

mod p1b_common;
use p1b_common::{all_meshes, corpus_dir, MeshBlob};

#[test]
fn real_blob_trees_roundtrip() {
    let meshes = all_meshes();
    if meshes.is_empty() {
        eprintln!("p1b: no CGF corpus at {} — skipped", corpus_dir().display());
        return;
    }
    let mut counts = [0usize; 3];
    for (name, blob, m) in &meshes {
        let mut r = MemReader::new(&blob[m.tree_off..]);
        let tree = load_tree(m.tree_type, &mut r, m.n_tris)
            .unwrap_or_else(|e| panic!("{name}: load failed: {e}"));
        assert_eq!(r.remaining(), 0, "{name}: the tree does not end the blob");
        let mut w = MemWriter::new();
        tree.save(&mut w);
        assert!(
            w.buf == blob[m.tree_off..],
            "{name}: type {} saves differently",
            m.tree_type
        );
        let mut mw = MemWriter::measuring();
        tree.save(&mut mw);
        assert_eq!(mw.pos, w.buf.len(), "{name}: measuring pass size");
        counts[m.tree_type as usize] += 1;
    }
    eprintln!(
        "p1b: byte-exact tree round trips: OBB {}, AABB {}, SingleBox {}",
        counts[0], counts[1], counts[2]
    );
}

fn mesh_of(m: &MeshBlob) -> Option<(Vec<aaemu_physics::math::Vec3>, Vec<i32>)> {
    Some((m.verts.clone()?, m.idx.clone()?))
}

/// Candidate relative maxSkipDim values (mesh flags 0x400 / 0x100 / neither).
const SKIPS: [f32; 3] = [0.1, 10.0, 0.0];

fn aabb_rebuilds(m: &MeshBlob, blob: &[u8]) -> Option<bool> {
    let (verts, idx0) = mesh_of(m)?;
    let stored = AabbTree::load(&mut MemReader::new(&blob[m.tree_off..]), m.n_tris).ok()?;
    for rel in SKIPS {
        for flat in [false, true] {
            let mut idx = idx0.clone();
            let mut t = AabbTree::new(
                stored.min_tris_per_side,
                stored.max_tris_per_leaf,
                rel,
                stored.basis,
                flat,
            );
            t.build(&mut BuildMesh {
                vertices: &verts,
                indices: &mut idx,
                mats: None,
                foreign_idx: None,
                aux: None,
            });
            let same = idx == idx0
                && t.center == stored.center
                && t.size == stored.size
                && t.max_leaf_tris == stored.max_leaf_tris
                && t.max_skip_dim.to_bits() == stored.max_skip_dim.to_bits()
                && t.nodes.len() == stored.nodes.len()
                && t.nodes
                    .iter()
                    .zip(&stored.nodes)
                    .enumerate()
                    .all(|(i, (a, b))| {
                        // Node 1 is unused (only ntris written); internal nodes' byte 11 is
                        // uninitialised memory in the original.
                        if i == 1 {
                            return a.ntris == b.ntris;
                        }
                        a.ichild == b.ichild
                            && a.bounds == b.bounds
                            && a.ntris == b.ntris
                            && (b.ntris == 0 || a.single_coll == b.single_coll)
                    });
            if same {
                return Some(true);
            }
        }
    }
    Some(false)
}

fn obb_rebuilds(m: &MeshBlob, blob: &[u8]) -> Option<bool> {
    let (verts, idx0) = mesh_of(m)?;
    let stored = ObbTree::load(&mut MemReader::new(&blob[m.tree_off..]), m.n_tris).ok()?;
    for rel in SKIPS {
        let mut idx = idx0.clone();
        let mut t = ObbTree::new(stored.min_tris_per_side, stored.max_tris_per_leaf, rel);
        t.build(&mut BuildMesh {
            vertices: &verts,
            indices: &mut idx,
            mats: None,
            foreign_idx: None,
            aux: None,
        });
        let same = idx == idx0
            && t.max_leaf_tris == stored.max_leaf_tris
            && t.max_skip_dim.to_bits() == stored.max_skip_dim.to_bits()
            && t.nodes.len() == stored.nodes.len()
            && t.nodes
                .iter()
                .zip(&stored.nodes)
                .enumerate()
                .all(|(i, (a, b))| {
                    // Node 1 is unused: only iParent and ntris are written.
                    if i == 1 {
                        a.i_parent == b.i_parent && a.ntris == b.ntris
                    } else {
                        a == b
                    }
                });
        if same {
            return Some(true);
        }
    }
    Some(false)
}

#[test]
fn real_blob_trees_rebuild() {
    let meshes = all_meshes();
    if meshes.is_empty() {
        eprintln!("p1b: no CGF corpus at {} — skipped", corpus_dir().display());
        return;
    }
    // [tree type][has vertex map] → (exact, differ)
    let mut tally = [[(0usize, 0usize); 2]; 2];
    let mut aabb_failures = Vec::new();
    for (name, blob, m) in &meshes {
        let res = match m.tree_type {
            0 => obb_rebuilds(m, blob),
            1 => aabb_rebuilds(m, blob),
            _ => None,
        };
        let Some(exact) = res else { continue };
        let t = &mut tally[m.tree_type as usize][usize::from(m.has_vtx_map)];
        if exact {
            t.0 += 1;
        } else {
            t.1 += 1;
            if m.tree_type == 1 && !m.has_vtx_map {
                aabb_failures.push(name.clone());
            }
        }
    }
    eprintln!(
        "p1b rebuild vs stored (exact, differ): AABB {:?} + {:?} with a vertex map; OBB {:?} + {:?} with a vertex map",
        tally[1][0], tally[1][1], tally[0][0], tally[0][1]
    );
    let exact: usize = tally.iter().flatten().map(|t| t.0).sum();
    let total: usize = tally.iter().flatten().map(|t| t.0 + t.1).sum();
    assert!(total == 0 || exact > 0, "no stored tree rebuilds exactly");
    let _ = aabb_failures;
}
