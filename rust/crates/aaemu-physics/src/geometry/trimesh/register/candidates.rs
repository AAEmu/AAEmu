//! `CTriMesh` slot 57 `GetUnprojectionCandidates` (0x351C3380, inferred name).
//!
//! `(this, iSide, unproj_result* res, prim** prim, int** features, gtest*)`: the triangle the
//! caller's `*prim` points at (its index is the record's `+48`) is recomputed into the gtest's
//! `+296` buffer (sub_351C27A0, with the gtest's current — possibly moved — transform) and
//! `*prim` is redirected there, `*features` to the `+312` buffer; `+160 = 0`. Then, from the
//! feature the unprojection reported on this side (`res.iFeature[iSide]`):
//!
//! 1. an edge or face feature (`& 0x60 ≠ 0`) whose point lies within `m_minVtxDist` of a
//!    vertex `k` (first in order, `d² ≤ mvd²`) becomes the vertex `k | 0x80`;
//! 2. **face** (`0x40`): one face candidate (the triangle), feature list `[64]`, returns 1;
//! 3. **edge** (`0x20 | e`, incl. the unprojection's `0xA0 | e`): edge candidate `[0]` along
//!    `v[inc e] − v[e]` with `n0 = n × dir`, face candidate `[0]` the triangle; across the
//!    edge the neighbour becomes the single candidate primitive (`unproj[0]`, feature
//!    `0xA0 | its edge`, id), `edge[0].n1 = dir × n'`, face candidate `[1]` the neighbour;
//!    returns 1. An open edge sets `n1 = n0` and returns 0;
//! 4. **vertex** (anything else): face `[0]` the triangle, edge `[0]` its edge leaving the
//!    vertex; then the triangles around the vertex (across `dec(e)`, then each one's edge
//!    `dec(slot)`, until an open edge, the start triangle or `valency − 1` of them) fill
//!    `unproj[k]` with feature `slot | 0x80`, id, face `[k+1]` and edge `[k+1]`
//!    (`n0 = dir × previous face`, `n1 = dir × own normal`); `edge[0].n1 = dir0 × last normal`;
//!    returns the number of ring triangles.
//!
//! The code `0x60` returns 0 without touching the candidate counts (stale lists, kept). Ids are
//! the material (−1 without materials); the face case writes none (the `+320` entry of the
//! leaf list stays).

#![allow(clippy::neg_cmp_op_on_partial_ord)]

use super::{CandidateList, CurPrim};
use crate::collision::contact::UnprojResult;
use crate::geometry::primitives::{Primitive, Triangle};
use crate::geometry::shapes::protocol::{EdgeCandidate, FaceCandidate};
use crate::geometry::trimesh::protocol::Side;
use crate::geometry::trimesh::TriMesh;
use crate::math::Vec3;

/// 0x3520A770 / 0x3520A780: `inc` / `dec` mod 3.
pub(crate) const INC: [usize; 3] = [1, 2, 0];
pub(crate) const DEC: [usize; 3] = [2, 0, 1];

/// The slot of triangle `t` in the adjacency of `nb` as the original computes it:
/// `((e2 ^ (e2 − 1)) >> 31 & 2) | ((e1 ^ (e1 − 1)) >> 31 & 1 & ~((e2 ^ (e2 − 1)) >> 31))`
/// with `e_k = topo[nb][k] − t` (2 if slot 2 holds `t`, else 1 if slot 1 does, else 0).
#[must_use]
pub(crate) fn adj_slot(topo: &[i32; 3], t: i32) -> usize {
    let e1 = topo[1].wrapping_sub(t);
    let e2 = topo[2].wrapping_sub(t);
    let z1 = (e1 ^ e1.wrapping_sub(1)) >> 31;
    let z2 = (e2 ^ e2.wrapping_sub(1)) >> 31;
    ((z2 & 2) | (z1 & 1 & !z2)) as usize
}

impl TriMesh {
    /// The id slots 57 give triangle `i`: its material or −1 (a local `char` set to −1).
    #[must_use]
    pub(crate) fn cand_id(&self, i: i32) -> i8 {
        self.mats.as_ref().map_or(-1, |m| m[i as usize])
    }

    /// Slot 57 (0x351C3380) on side record `sd` for the triangle `cur_idx` (see the module
    /// docs). Returns the candidate list the caller iterates.
    pub fn unprojection_candidates_mesh(
        &self,
        i_side: i32,
        res: &UnprojResult,
        cur_idx: i32,
        sd: &mut Side,
    ) -> CandidateList {
        let nv = sd.gt.n_unproj_prims.max(1) as usize;
        {
            let buf = sd.gt.unproj_prims.get_or_insert_with(Vec::new);
            if buf.len() < nv {
                buf.resize(nv, Primitive::Triangle(Triangle::default()));
            }
        }
        if sd.unproj_idx.len() < nv {
            sd.unproj_idx.resize(nv, 0);
        }
        if sd.gt.aux_idx.len() < nv {
            sd.gt.aux_idx.resize(nv, 0);
        }
        if sd.gt.ids.len() < nv {
            sd.gt.ids.resize(nv, 0);
        }
        let mut feat = res.i_feature[(i_side & 1) as usize];
        let t = cur_idx;
        let tri = self.triangle_in_gtest(t as usize, &sd.gt);
        set_unproj(sd, 0, tri, t);
        sd.gt.transform_prims = 0;
        if feat & 0x60 != 0 {
            let mvd = self.min_vtx_dist;
            let p = res.pt;
            let mut k = 0;
            while k < 3 {
                let v = tri.pt[k];
                let d2 = ((v.x - p.x) * (v.x - p.x) + (v.y - p.y) * (v.y - p.y))
                    + (v.z - p.z) * (v.z - p.z);
                // `comiss; jbe`: a NaN distance also stops the search
                if !(d2 > mvd * mvd) {
                    break;
                }
                k += 1;
            }
            if k < 3 {
                feat = k as i32 | 0x80;
            }
        }
        let topo = |i: i32| self.topology[i as usize];
        let n = tri.n;
        let class = feat & 0x60;
        if class == 0x40 {
            sd.gt.face_cands = vec![face(n, t)];
            sd.gt.edge_cands.clear();
            sd.gt.aux_idx[0] = 64;
            return list(sd, 1);
        }
        if class == 0x60 {
            return list(sd, 0);
        }
        if class == 0x20 {
            let e = (feat & 0x1F) as usize;
            let d = sub(tri.pt[INC[e]], tri.pt[e]);
            let n0 = Vec3::new(
                n.y * d.z - n.z * d.y,
                n.z * d.x - n.x * d.z,
                n.x * d.y - n.y * d.x,
            );
            let mut ec = EdgeCandidate {
                dir: d,
                n: [n0, Vec3::ZERO],
                i_prim: t,
                i_feature: feat,
            };
            sd.gt.face_cands = vec![face(n, t)];
            let nb = topo(t)[e];
            if nb < 0 {
                ec.n[1] = ec.n[0];
                sd.gt.edge_cands = vec![ec];
                return list(sd, 0);
            }
            sd.gt.aux_idx[0] = adj_slot(&topo(nb), t) as i32 | 0xA0;
            let t2 = self.triangle_in_gtest(nb as usize, &sd.gt);
            sd.gt.ids[0] = self.cand_id(nb);
            set_unproj(sd, 0, t2, nb);
            let m = t2.n;
            ec.n[1] = Vec3::new(
                m.z * d.y - m.y * d.z,
                m.x * d.z - m.z * d.x,
                m.y * d.x - m.x * d.y,
            );
            sd.gt.edge_cands = vec![ec];
            sd.gt.face_cands.push(face(m, nb));
            return list(sd, 1);
        }
        // vertex
        let e = (feat & 0x1F) as usize;
        let d0 = sub(tri.pt[INC[e]], tri.pt[e]);
        let mut faces = vec![face(n, t)];
        let mut edges = vec![EdgeCandidate {
            dir: d0,
            n: [
                Vec3::new(
                    n.y * d0.z - n.z * d0.y,
                    n.z * d0.x - n.x * d0.z,
                    n.x * d0.y - n.y * d0.x,
                ),
                Vec3::ZERO,
            ],
            i_prim: t,
            i_feature: (feat & 0x1F) | 0x20,
        }];
        let mut prev = t;
        let mut k = 0usize;
        let mut nb = topo(t)[DEC[e]];
        let cap = sd.gt.n_unproj_prims - 1;
        if nb >= 0 {
            loop {
                let tk = self.triangle_in_gtest(nb as usize, &sd.gt);
                set_unproj(sd, k, tk, nb);
                sd.gt.ids[k] = self.cand_id(nb);
                let slot = adj_slot(&topo(nb), prev);
                sd.gt.aux_idx[k] = slot as i32 | 0x80;
                let d = sub(tk.pt[INC[slot]], tk.pt[slot]);
                let f = faces[k].n;
                let nn = tk.n;
                edges.push(EdgeCandidate {
                    dir: d,
                    n: [
                        Vec3::new(
                            f.z * d.y - f.y * d.z,
                            f.x * d.z - d.x * f.z,
                            d.x * f.y - f.x * d.y,
                        ),
                        Vec3::new(
                            d.z * nn.y - d.y * nn.z,
                            d.x * nn.z - d.z * nn.x,
                            d.y * nn.x - d.x * nn.y,
                        ),
                    ],
                    i_prim: nb,
                    i_feature: slot as i32 | 0x20,
                });
                faces.push(face(nn, nb));
                prev = nb;
                nb = topo(nb)[DEC[slot]];
                k += 1;
                if !(nb >= 0 && nb != t && (k as i32) < cap) {
                    break;
                }
            }
        }
        let last = match &sd.gt.unproj_prims.as_ref().expect("unproj buffer")[k.max(1) - 1] {
            Primitive::Triangle(tr) => tr.n,
            _ => unreachable!(),
        };
        edges[0].n[1] = Vec3::new(
            last.z * d0.y - last.y * d0.z,
            last.x * d0.z - d0.x * last.z,
            d0.x * last.y - last.x * d0.y,
        );
        sd.gt.face_cands = faces;
        sd.gt.edge_cands = edges;
        list(sd, k as i32)
    }
}

fn sub(a: Vec3, b: Vec3) -> Vec3 {
    Vec3::new(a.x - b.x, a.y - b.y, a.z - b.z)
}

fn face(n: Vec3, t: i32) -> FaceCandidate {
    FaceCandidate {
        n,
        i_prim: t,
        i_feature: 64,
    }
}

fn set_unproj(sd: &mut Side, k: usize, tri: Triangle, idx: i32) {
    sd.gt.unproj_prims.as_mut().expect("unproj buffer")[k] = Primitive::Triangle(tri);
    sd.unproj_idx[k] = idx;
}

/// The candidates `0..n` of the `+296` buffer; `base` is entry 0 (where `*prim` points).
fn list(sd: &Side, n: i32) -> CandidateList {
    let buf = sd.gt.unproj_prims.as_ref().expect("unproj buffer");
    let cur = |k: usize| CurPrim {
        prim: buf[k],
        idx: sd.unproj_idx[k],
    };
    let m = n.max(0) as usize;
    CandidateList {
        n,
        base: cur(0),
        prims: (0..m).map(cur).collect(),
        feats: (0..m).map(|k| sd.gt.aux_idx[k]).collect(),
        ids: (0..m).map(|k| sd.gt.ids[k]).collect(),
    }
}
