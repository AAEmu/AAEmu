//! `CTriMesh` slots 58 / 59 (0x351C4610 / 0x351C5B90, feature polygons of the contact-area
//! builder) with sub_351BE120, and the area part of slot 54 that uses them.
//!
//! **Slot 58** (face polygon of triangle `t`): when the frame's normal is zero it is derived
//! from the triangle (normal `R·n`, first axis `R·normalize(v1 − v0)`, second `n × a0`, origin
//! `R·v0·s + o`). Then the coplanar patch around `t` (neighbours with `n·n' > 0.9998`, flood
//! fill through a 512-entry ring queue) is merged into one boundary loop of vertex entries
//! `{vertex, code = tri << 8 | edge, prev, next}` (≤ 512), every added triangle being marked
//! used in the BV tree (slot 25). The loop is output in the frame's 2D coordinates, walking
//! forward when the triangle's world normal faces the frame normal, backward otherwise;
//! `ids_a` are the edge codes, `ids_b` the edge across from each vertex to the next
//! (sub_351BE120). The output repeats its first point.
//!
//! **Slot 59** (segment of edge `e` of triangle `t`): the edge is extended along collinear
//! edges of neighbouring triangles that lie in the frame's plane (direction tests in the
//! mesh's *local* frame against the world frame's origin — kept), forward from the edge's end
//! (up to 100 steps shared by both directions); the entries prepended before the start are
//! not output (kept: the output starts at entry 0).

#![allow(clippy::neg_cmp_op_on_partial_ord)]

use super::polyclip::{intersect_polygons, lift, PolyOut, PolyScratch};
use super::AreaKind;
use crate::collision::contact::GeomContact;
use crate::geometry::bvtree::{BvArena, BvTestState, BvTree};
use crate::geometry::shapes::protocol::{ContactFrame, FeaturePolygon, GeomUnderTest};
use crate::geometry::trimesh::protocol::{GeomProtocol, IntersectCtx};
use crate::geometry::trimesh::TriMesh;
use crate::math::Vec3;

use super::candidates::{adj_slot, DEC, INC};

/// The BV marking a slot-58 call performs (tree + the side's used-node state).
pub struct Marker<'a> {
    pub tree: &'a dyn BvTree,
    pub state: &'a mut BvTestState,
    pub arena: &'a mut BvArena,
}

/// One entry of the boundary loop.
#[derive(Clone, Copy, Debug, Default)]
struct Ent {
    vtx: i32,
    code: i32,
    prev: i32,
    next: i32,
}

impl TriMesh {
    fn vtx(&self, i: i32) -> Vec3 {
        self.vertices[i as usize]
    }

    fn tri_vtx(&self, t: i32, k: usize) -> i32 {
        self.indices[3 * t as usize + k]
    }

    /// sub_351BE120: the edge code (`tri << 8 | edge | 0x20`) of the edge from the start vertex
    /// of edge `code` to vertex `v`, found by turning around the vertex (≤ 50 steps), or −1.
    #[must_use]
    pub fn edge_to_vertex(&self, code: i32, v: i32) -> i32 {
        let mut t = code >> 8;
        let start = t;
        let mut e = DEC[(code & 0x1F) as usize];
        let mut left = 50;
        loop {
            if self.tri_vtx(t, e) == v {
                return e as i32 | (t << 8) | 0x20;
            }
            let nb = self.topology[t as usize][e];
            if nb < 0 {
                return -1;
            }
            let s = adj_slot(&self.topology[nb as usize], t);
            t = nb;
            if self.tri_vtx(nb, INC[s]) == v {
                return s as i32 | (nb << 8) | 0x20;
            }
            e = DEC[s];
            left -= 1;
            if !(nb != start && left > 0) {
                return -1;
            }
        }
    }

    /// World position `R·v·s + o` as slots 58/59 compute it, minus the frame origin.
    fn world_rel(&self, v: Vec3, gt: &GeomUnderTest, o: Vec3) -> Vec3 {
        let r = &gt.r.m;
        let s = gt.scale;
        let x = ((v.x * r[0][0] + v.y * r[0][1]) + v.z * r[0][2]) * s + gt.offset.x;
        let y = s * ((v.x * r[1][0] + v.y * r[1][1]) + v.z * r[1][2]) + gt.offset.y;
        let z = s * ((v.y * r[2][1] + v.x * r[2][0]) + v.z * r[2][2]) + gt.offset.z;
        Vec3::new(x - o.x, y - o.y, z - o.z)
    }

    /// Slot 58 (0x351C4610) for triangle `t`; `mark` performs BV slot 25 on the added triangles
    /// (`None`: no marking, the trait entry point).
    pub fn face_polygon_mesh(
        &self,
        frame: &mut ContactFrame,
        t: i32,
        gt: &GeomUnderTest,
        mut mark: Option<Marker<'_>>,
        out: &mut FeaturePolygon,
    ) -> i32 {
        let r = &gt.r.m;
        let fnv = frame.n;
        if (fnv.x * fnv.x + fnv.y * fnv.y) + fnv.z * fnv.z == 0.0 {
            let nl = self.normals[t as usize];
            frame.n = Vec3::new(
                (nl.x * r[0][0] + nl.y * r[0][1]) + nl.z * r[0][2],
                (nl.x * r[1][0] + nl.y * r[1][1]) + nl.z * r[1][2],
                (nl.y * r[2][1] + nl.x * r[2][0]) + nl.z * r[2][2],
            );
            let v0 = self.vtx(self.tri_vtx(t, 0));
            let v1 = self.vtx(self.tri_vtx(t, 1));
            let (dx, dy, dz) = (v1.x - v0.x, v1.y - v0.y, v1.z - v0.z);
            let l = (dx * dx + dy * dy) + dz * dz;
            let (ex, ey, ez) = if l > 1e-20 {
                let q = l.sqrt();
                (dx * (1.0 / q), (1.0 / q) * dy, (1.0 / q) * dz)
            } else {
                (0.0, 0.0, 1.0)
            };
            let a0 = Vec3::new(
                (ex * r[0][0] + ey * r[0][1]) + ez * r[0][2],
                (ex * r[1][0] + ey * r[1][1]) + ez * r[1][2],
                (ey * r[2][1] + ex * r[2][0]) + ez * r[2][2],
            );
            let n = frame.n;
            frame.axes[0] = a0;
            frame.axes[1] = Vec3::new(
                n.y * a0.z - n.z * a0.y,
                n.z * a0.x - n.x * a0.z,
                n.x * a0.y - n.y * a0.x,
            );
            let s = gt.scale;
            frame.origin = Vec3::new(
                ((v0.x * r[0][0] + v0.y * r[0][1]) + v0.z * r[0][2]) * s + gt.offset.x,
                s * ((v0.y * r[1][1] + v0.x * r[1][0]) + v0.z * r[1][2]) + gt.offset.y,
                s * ((v0.y * r[2][1] + v0.x * r[2][0]) + v0.z * r[2][2]) + gt.offset.z,
            );
        }
        let nt = self.n_tris.max(0) as usize;
        let nv = self.n_verts.max(0) as usize;
        let mut vbit = vec![false; nv];
        let mut tbit = vec![false; nt];
        let nl = self.normals[t as usize];
        let mut ents = vec![Ent::default(); 512];
        for k in 0..3 {
            ents[k] = Ent {
                vtx: self.tri_vtx(t, k),
                code: (t << 8) | k as i32,
                prev: [2, 0, 1][k],
                next: [1, 2, 0][k],
            };
        }
        let mut nent = 3usize;
        // ring queue {tri, from, entry}
        let mut q = vec![[0i32; 3]; 512];
        let mut head: i32 = -1;
        let mut tail: usize = 0;
        let mut qn: i32 = 0;
        let coplanar = |nb: i32| {
            let m = self.normals[nb as usize];
            (nl.y * m.y + nl.x * m.x) + nl.z * m.z > 0.9998_f32
        };
        let enqueue = |nb: i32,
                       from: i32,
                       ent: i32,
                       tbit: &mut Vec<bool>,
                       q: &mut Vec<[i32; 3]>,
                       head: &mut i32,
                       qn: &mut i32,
                       mark: &mut Option<Marker<'_>>| {
            *head = (*head + 1) & 0x1FF;
            let d = *qn - 511;
            *qn = (d & (d >> 31)) + 512;
            q[*head as usize] = [nb, from, ent];
            tbit[nb as usize] = true;
            if let Some(m) = mark.as_mut() {
                m.tree.mark_used_triangle(nb, m.state, m.arena);
            }
        };
        for e in 0..3 {
            let nb = self.topology[t as usize][e];
            if nb >= 0 && !tbit[nb as usize] && coplanar(nb) {
                enqueue(
                    nb, t, e as i32, &mut tbit, &mut q, &mut head, &mut qn, &mut mark,
                );
            }
        }
        let mut v151: i32 = 0;
        let mut v166: i32 = 2;
        while qn > 0 {
            qn -= 1;
            let [nb, from, ent] = q[tail];
            tail = (tail + 1) & 0x1FF;
            v151 = ent;
            let s = adj_slot(&self.topology[nb as usize], from);
            let apex = self.tri_vtx(nb, DEC[s]);
            let mut v60 = 0;
            let mut v153 = DEC[s];
            let mut insert = !vbit[apex as usize];
            if !insert {
                let mut v76: i32 = 0;
                let mut v85 = ents[ent as usize].prev;
                if ents[v85 as usize].vtx == apex {
                    v151 = v85;
                    v166 = v85;
                    v60 = 1;
                    v153 = DEC[s];
                    v76 = ents[ents[v151 as usize].next as usize].next;
                }
                let n2 = ents[ents[ent as usize].next as usize].next;
                if ents[n2 as usize].vtx == apex {
                    v76 = n2;
                    v166 = ent;
                    v85 = ent;
                    v153 = INC[s];
                    v60 += 1;
                }
                if v60 == 2 {
                    let mut a = ents[ent as usize].prev;
                    let mut b = ents[ents[ent as usize].next as usize].next;
                    while ents[a as usize].vtx == ents[b as usize].vtx {
                        a = ents[a as usize].prev;
                        b = ents[b as usize].next;
                    }
                    v76 = b;
                    v85 = ents[a as usize].next;
                }
                if v60 > 0 {
                    ents[v85 as usize].next = v76;
                    ents[v76 as usize].prev = v85;
                } else {
                    insert = true;
                }
            }
            let v92;
            if insert {
                if nent == 512 {
                    break;
                }
                let old_next = ents[ent as usize].next;
                ents[nent] = Ent {
                    vtx: apex,
                    code: DEC[s] as i32 | (nb << 8),
                    prev: ent,
                    next: old_next,
                };
                ents[old_next as usize].prev = nent as i32;
                ents[ent as usize].next = nent as i32;
                vbit[apex as usize] = true;
                nent += 1;
                let nb2 = self.topology[nb as usize][INC[s]];
                if nb2 >= 0 && !tbit[nb2 as usize] && coplanar(nb2) {
                    enqueue(
                        nb2, nb, ent, &mut tbit, &mut q, &mut head, &mut qn, &mut mark,
                    );
                }
                v153 = DEC[s];
                v92 = ents[ent as usize].next;
                v166 = v92;
            } else if v60 < 2 {
                v92 = v166;
            } else {
                continue;
            }
            let nb3 = self.topology[nb as usize][v153];
            if nb3 >= 0 && !tbit[nb3 as usize] && coplanar(nb3) {
                enqueue(
                    nb3, nb, v92, &mut tbit, &mut q, &mut head, &mut qn, &mut mark,
                );
            }
        }
        // output
        let fwd = {
            let wn = Vec3::new(
                (nl.x * r[0][0] + nl.y * r[0][1]) + nl.z * r[0][2],
                (nl.x * r[1][0] + nl.y * r[1][1]) + nl.z * r[1][2],
                (nl.y * r[2][1] + nl.x * r[2][0]) + nl.z * r[2][2],
            );
            let n = frame.n;
            ((n.y * wn.y + n.x * wn.x) + n.z * wn.z).to_bits() >> 31 == 0
        };
        out.pts.clear();
        out.ids_a.clear();
        out.ids_b.clear();
        out.ids_shared = false;
        let mut k = v151;
        let mut cnt = 0usize;
        loop {
            let e = ents[k as usize];
            let p = self.world_rel(self.vtx(e.vtx), gt, frame.origin);
            out.pts.push(proj(frame, p));
            out.ids_a.push(e.code);
            if ((e.code >> 8) as u32) >= self.n_tris as u32 {
                out.ids_b.push(0);
                break;
            }
            cnt += 1;
            let nk = if fwd { e.next } else { e.prev };
            out.ids_b
                .push(self.edge_to_vertex(e.code, ents[nk as usize].vtx));
            k = nk;
            if !(k != v151 && cnt < 0x200) {
                break;
            }
        }
        close(out, cnt);
        cnt as i32
    }

    /// Slot 59 (0x351C5B90) for edge `feat & 0x1F` of triangle `t`.
    pub fn edge_segment_mesh(
        &self,
        frame: &mut ContactFrame,
        t: i32,
        feat: i32,
        gt: &GeomUnderTest,
        out: &mut FeaturePolygon,
    ) -> i32 {
        let e = (feat & 0x1F) as usize;
        let nv = self.n_verts.max(0) as usize;
        let mut vbit = vec![false; nv];
        let mut ents = vec![Ent::default(); 512];
        let ie = INC[e];
        ents[0] = Ent {
            vtx: self.tri_vtx(t, e),
            code: e as i32 | (t << 8),
            prev: -1,
            next: 1,
        };
        ents[1] = Ent {
            vtx: self.tri_vtx(t, ie),
            code: (t << 8) | ie as i32,
            prev: 0,
            next: -1,
        };
        let va = self.vtx(ents[0].vtx);
        let vb = self.vtx(ents[1].vtx);
        let (dx, dy, dz) = (vb.x - va.x, vb.y - va.y, vb.z - va.z);
        vbit[ents[0].vtx as usize] = true;
        vbit[ents[1].vtx as usize] = true;
        let r = &gt.r.m;
        let n = frame.n;
        let lx = (n.y * r[1][0] + n.x * r[0][0]) + n.z * r[2][0];
        let ly = (n.x * r[0][1] + n.y * r[1][1]) + n.z * r[2][1];
        let lz = (n.y * r[1][2] + n.x * r[0][2]) + n.z * r[2][2];
        let v34 = lz * dy - ly * dz;
        let v35 = ly * dx - dy * lx;
        let v36 = dz * lx - lz * dx;
        let v37 = v35 * dy - v36 * dz;
        let v38 = dz * v34 - v35 * dx;
        let v39 = v36 * dx - dy * v34;
        let l = (v38 * v38 + v37 * v37) + v39 * v39;
        let (wx, wy, wz) = if l > 1e-20 {
            let q = l.sqrt();
            (v37 * (1.0 / q), v38 * (1.0 / q), v39 * (1.0 / q))
        } else {
            (0.0, 0.0, 1.0)
        };
        let mut steps = 0;
        let mut cur = t;
        let mut stop_t = t;
        let mut edge = e;
        let mut fwd_flag = 1usize; // v106
        let mut link = 1usize; // v98
        let mut last = 1i32; // v105
        let mut tail = 1usize; // v96
        let mut n_ent = 2usize; // v100 / v101
        loop {
            loop {
                let nb = self.topology[cur as usize][edge];
                if nb < 0 || nb == stop_t {
                    break;
                }
                let s = adj_slot(&self.topology[nb as usize], cur);
                edge = DEC[s];
                let a = self.vtx(self.tri_vtx(nb, edge));
                let bq = self.vtx(self.tri_vtx(nb, INC[edge]));
                let (ex, ey, ez) = (bq.x - a.x, bq.y - a.y, bq.z - a.z);
                if ((ey * wy + ex * wx) + ez * wz).abs() < 0.01 {
                    let o = frame.origin;
                    let fnv = frame.n;
                    let d = (((ey * 0.5 + a.y) - o.y) * fnv.y + fnv.x * ((ex * 0.5 + a.x) - o.x))
                        + ((ez * 0.5 + a.z) - o.z) * fnv.z;
                    if d * d < ((ey * ey + ex * ex) + ez * ez) * 0.0004_f32 {
                        stop_t = nb;
                        edge = DEC[edge];
                        let e2 = INC[edge];
                        let v = self.tri_vtx(nb, e2);
                        if vbit[v as usize] || n_ent == 512 {
                            break;
                        }
                        let mut en = Ent {
                            vtx: v,
                            code: e2 as i32 | (nb << 8),
                            prev: 0,
                            next: 0,
                        };
                        if link == 1 {
                            en.next = -1;
                        } else {
                            en.prev = -1;
                        }
                        if fwd_flag ^ 1 == 0 {
                            en.prev = last;
                        } else {
                            en.next = last;
                        }
                        ents[n_ent] = en;
                        last = n_ent as i32;
                        if link == 1 {
                            ents[tail].next = n_ent as i32;
                        } else {
                            ents[tail].prev = n_ent as i32;
                        }
                        vbit[v as usize] = true;
                        tail = n_ent;
                        n_ent += 1;
                    }
                }
                cur = nb;
                steps += 1;
                if steps >= 100 {
                    break;
                }
            }
            link = link.wrapping_sub(1);
            stop_t = t;
            cur = t;
            last = 0;
            tail = 0;
            edge = DEC[e];
            if fwd_flag == 0 {
                break;
            }
            fwd_flag -= 1;
        }
        out.pts.clear();
        out.ids_a.clear();
        out.ids_b.clear();
        out.ids_shared = false;
        let mut j: i32 = 0;
        let mut cnt = 0usize;
        while j >= 0 {
            if cnt >= 0x200 {
                break;
            }
            let en = ents[j as usize];
            let p = self.world_rel(self.vtx(en.vtx), gt, frame.origin);
            out.pts.push(proj(frame, p));
            out.ids_a.push(en.code);
            let idb = if en.next < 0 {
                -1
            } else {
                self.edge_to_vertex(en.code, ents[en.next as usize].vtx)
            };
            cnt += 1;
            out.ids_b.push(idb);
            j = en.next;
        }
        close(out, cnt);
        cnt as i32
    }
}

/// `x = (p.y·a0.y + p.x·a0.x) + p.z·a0.z`, `y = (p.x·a1.x + p.y·a1.y) + p.z·a1.z`.
fn proj(f: &ContactFrame, p: Vec3) -> [f32; 2] {
    let (a0, a1) = (f.axes[0], f.axes[1]);
    [
        (p.y * a0.y + p.x * a0.x) + p.z * a0.z,
        (p.x * a1.x + p.y * a1.y) + p.z * a1.z,
    ]
}

/// The closing copy of entry 0 at index `n`.
fn close(out: &mut FeaturePolygon, n: usize) {
    out.pts.truncate(n);
    out.ids_a.truncate(n);
    out.ids_b.truncate(n);
    let (p, a, b) = (out.pts[0], out.ids_a[0], out.ids_b[0]);
    out.pts.push(p);
    out.ids_a.push(a);
    out.ids_b.push(b);
}

/// The area section of slot 54 (0x351D7149…0x351D78F9): builds the patch of `c.area`.
#[allow(clippy::needless_range_loop)]
pub(super) fn build_area(
    ctx: &mut IntersectCtx<'_>,
    this: &dyn GeomProtocol,
    other: &dyn GeomProtocol,
    side: usize,
    kind: AreaKind,
    c: &mut GeomContact,
) {
    let so = side ^ 1;
    let sx = |k: usize| if k == 0 { side } else { so };
    let geom = |k: usize| -> &dyn GeomProtocol {
        if k == 0 {
            this
        } else {
            other
        }
    };
    let mut frame = ContactFrame::default();
    let mut p1 = FeaturePolygon::default();
    let mut p2 = FeaturePolygon::default();
    let (n1, n2, closed, k_face);
    {
        let area = c.area.as_mut().expect("area");
        match kind {
            AreaKind::FaceFace(i, j) => {
                area.i_type = 0;
                let fa = ctx.s[side].gt.face_cands[i];
                let fb = ctx.s[so].gt.face_cands[j];
                c.n = fa.n;
                area.n = fb.n;
                n1 = this.proto_face_polygon(
                    ctx,
                    side,
                    &mut frame,
                    fa.i_prim,
                    fa.i_feature,
                    &mut p1,
                );
                n2 =
                    other.proto_face_polygon(ctx, so, &mut frame, fb.i_prim, fb.i_feature, &mut p2);
                closed = 1;
                k_face = 0usize;
            }
            AreaKind::FaceEdge(k, i, j) => {
                area.i_type = 1;
                let f = ctx.s[sx(k)].gt.face_cands[i];
                let ed = ctx.s[sx(k ^ 1)].gt.edge_cands[j];
                let cn = c.n;
                let fnv = f.n;
                let d = (cn.y * fnv.y + cn.x * fnv.x) + cn.z * fnv.z;
                let sg = (((((d.to_bits() as i32) >> 30) & !1) + 1) * (1 - 2 * k as i32)) as f32;
                c.n = Vec3::new(fnv.x * sg, fnv.y * sg, fnv.z * sg);
                let e = ed.dir;
                let c1 = e.z * fnv.y - e.y * fnv.z;
                let c2 = e.x * fnv.z - fnv.x * e.z;
                let c3 = fnv.x * e.y - e.x * fnv.y;
                let x5 = c1 * e.z - c3 * e.x;
                let x4 = c3 * e.y - c2 * e.z;
                let x8 = c2 * e.x - c1 * e.y;
                let l = (x5 * x5 + x4 * x4) + x8 * x8;
                let mut an = if l > 1e-20 {
                    let inv = 1.0 / l.sqrt();
                    Vec3::new(x4 * inv, x5 * inv, x8 * inv)
                } else {
                    Vec3::new(0.0, 0.0, 1.0)
                };
                let cn = c.n;
                let d2 = (cn.y * an.y + cn.x * an.x) + cn.z * an.z;
                let m = (-1 - (((d2.to_bits() as i32) >> 30) & !1)) as f32;
                an = Vec3::new(m * an.x, m * an.y, m * an.z);
                if k != 0 {
                    area.n = c.n;
                    c.n = an;
                } else {
                    area.n = an;
                }
                n1 = geom(k).proto_face_polygon(
                    ctx,
                    sx(k),
                    &mut frame,
                    f.i_prim,
                    f.i_feature,
                    &mut p1,
                );
                n2 = geom(k ^ 1).proto_edge_segment(
                    ctx,
                    sx(k ^ 1),
                    &mut frame,
                    ed.i_prim,
                    ed.i_feature,
                    &mut p2,
                );
                closed = 0;
                k_face = k;
            }
            AreaKind::None => unreachable!(),
        }
    }
    let scr: &mut PolyScratch = &mut ctx.arena.poly;
    let (cnt, which) = intersect_polygons(scr, [&p1.pts, &p2.pts], [n1, n2], closed);
    let pts: Vec<[f32; 2]> = match which {
        PolyOut::Input0 => p1.pts.clone(),
        PolyOut::Input1 => p2.pts.clone(),
        PolyOut::Own(v) => v,
    };
    let ids = ctx.arena.poly.ids.clone();
    let area = c.area.as_mut().expect("area");
    let mut written = 0i32;
    if cnt != 0 {
        let m = cnt.min(area.nmaxpt);
        let polys = [&p1, &p2];
        let pick = |pg: &FeaturePolygon, both: bool, i: usize| -> i32 {
            let arr = if both && !pg.ids_shared {
                &pg.ids_b
            } else {
                &pg.ids_a
            };
            arr.get(i).copied().unwrap_or(0)
        };
        for i in 0..m.max(0) as usize {
            area.pt[i] = lift(&frame, pts.get(i).copied().unwrap_or([0.0, 0.0]));
            let id = ids[i];
            let lo = (id & 0xFFFF) - 1;
            let hi = (id >> 16) - 1;
            let both = (lo | hi) >= 0;
            let s0 = lo >> 31;
            let i0 = (lo - s0) as usize;
            let s1 = hi >> 31;
            let i1 = (hi - s1) as usize;
            let a = pick(polys[0], both, i0);
            let b = pick(polys[1], both, i1);
            area.prim[k_face][i] = (a >> 8) | s0;
            area.feature[k_face][i] = (a & 0xFF) | s0;
            area.prim[k_face ^ 1][i] = (b >> 8) | s1;
            area.feature[k_face ^ 1][i] = (b & 0xFF) | s1;
            written += 1;
        }
    }
    area.npt = written;
    ctx.arena.n_area_pts += written;
    ctx.arena.n_areas += 1;
}
