//! P1b: the BV slots driving a tree-vs-tree traversal the way `CGeometry::Intersect`'s
//! traversal (sub_3514DFD0) uses them: tree A in its own frame (slots 11/14/16), tree B placed
//! into A's frame (slots 9/15/16), descend into the node with the larger split priority
//! (slot 12), leaves through slot 23. The box overlap here is a plain separating-axis test (the
//! engine's OVERLAP table is P2a's); the point is that every pair of triangles whose boxes
//! overlap is reached, and that the BV pool is balanced afterwards.

use aaemu_physics::geometry::bvtree::{
    AabbTree, BuildMesh, BvArena, BvHandle, BvTree, ObbTree, SingleBoxTree,
};
use aaemu_physics::geometry::primitives::BoxPrim;
use aaemu_physics::math::{Mat33, Vec3};
use std::collections::BTreeSet;

/// Separating-axis test for two oriented boxes (rows of `basis` = axes), in f64 with a small
/// tolerance so that touching boxes count as overlapping.
fn boxes_overlap(a: &BoxPrim, b: &BoxPrim) -> bool {
    // An unoriented record's basis words are stale pool memory: the consumer must not read them.
    let axes_of = |p: &BoxPrim| -> Vec<[f64; 3]> {
        if p.oriented == 0 {
            vec![[1.0, 0.0, 0.0], [0.0, 1.0, 0.0], [0.0, 0.0, 1.0]]
        } else {
            (0..3).map(|i| row(&p.basis, i)).collect()
        }
    };
    let ax = axes_of(a);
    let bx = axes_of(b);
    let d = [
        f64::from(b.center.x - a.center.x),
        f64::from(b.center.y - a.center.y),
        f64::from(b.center.z - a.center.z),
    ];
    let mut axes: Vec<[f64; 3]> = Vec::new();
    axes.extend(ax.iter().copied());
    axes.extend(bx.iter().copied());
    for u in &ax {
        for v in &bx {
            let c = [
                u[1] * v[2] - u[2] * v[1],
                u[2] * v[0] - u[0] * v[2],
                u[0] * v[1] - u[1] * v[0],
            ];
            if dot(&c, &c) > 1e-12 {
                axes.push(c);
            }
        }
    }
    let (sa, sb) = (a.size, b.size);
    for l in &axes {
        let ra: f64 = (0..3)
            .map(|i| f64::from(sa[i]) * dot(&ax[i], l).abs())
            .sum();
        let rb: f64 = (0..3)
            .map(|i| f64::from(sb[i]) * dot(&bx[i], l).abs())
            .sum();
        if dot(&d, l).abs() > (ra + rb) * (1.0 + 1e-5) + 1e-5 {
            return false;
        }
    }
    true
}

fn row(m: &Mat33, i: usize) -> [f64; 3] {
    [
        f64::from(m.m[i][0]),
        f64::from(m.m[i][1]),
        f64::from(m.m[i][2]),
    ]
}

fn dot(a: &[f64; 3], b: &[f64; 3]) -> f64 {
    a[0] * b[0] + a[1] * b[1] + a[2] * b[2]
}

struct Ctx<'a> {
    a: &'a dyn BvTree,
    b: &'a dyn BvTree,
    r: Mat33,
    off: Vec3,
    scale: f32,
    pairs: BTreeSet<(i32, i32)>,
}

fn traverse(cx: &mut Ctx<'_>, ar: &mut BvArena, ha: BvHandle, hb: BvHandle) {
    if !boxes_overlap(&ar.bv_box(ha), &ar.bv_box(hb)) {
        return;
    }
    let va = cx.a.split_priority(ar, ha);
    let vb = cx.b.split_priority(ar, hb);
    if va > vb && va > 0.0 {
        let (a1, a2) = cx.a.get_node_children_bvs(ar, ha).unwrap();
        traverse(cx, ar, a1, hb);
        traverse(cx, ar, a2, hb);
        cx.a.release_last_bvs(ar);
    } else if vb > 0.0 {
        let (b1, b2) =
            cx.b.get_node_children_bvs_transformed(ar, &cx.r, cx.off, cx.scale, hb)
                .unwrap();
        traverse(cx, ar, ha, b1);
        traverse(cx, ar, ha, b2);
        cx.b.release_last_bvs(ar);
    } else {
        let ca = cx.a.get_node_contents(ar.bv_node(ha));
        let cb = cx.b.get_node_contents(ar.bv_node(hb));
        for i in ca.first_prim..ca.first_prim + ca.n_prims {
            for j in cb.first_prim..cb.first_prim + cb.n_prims {
                cx.pairs.insert((i, j));
            }
        }
    }
}

/// A wavy sheet of `n × n` quads.
fn sheet(n: usize, phase: f32) -> (Vec<Vec3>, Vec<i32>) {
    let mut v = Vec::new();
    for i in 0..=n {
        for j in 0..=n {
            let (x, y) = (i as f32 * 0.5, j as f32 * 0.5);
            v.push(Vec3::new(
                x,
                y,
                (x * 0.9 + phase).sin() * 0.6 + (y * 1.3).cos() * 0.4,
            ));
        }
    }
    let mut idx = Vec::new();
    let w = (n + 1) as i32;
    for i in 0..n as i32 {
        for j in 0..n as i32 {
            let a = i * w + j;
            idx.extend([a, a + w, a + 1, a + 1, a + w, a + w + 1]);
        }
    }
    (v, idx)
}

/// The triangle's corners in the frame of `r·x·scale + off`.
fn tri_pts(v: &[Vec3], idx: &[i32], t: i32, r: &Mat33, off: Vec3, scale: f32) -> Vec<[f64; 3]> {
    (0..3)
        .map(|k| {
            let p = r.mul_vec(v[idx[(3 * t + k) as usize] as usize]) * scale + off;
            [f64::from(p.x), f64::from(p.y), f64::from(p.z)]
        })
        .collect()
}

fn box_pts(b: &BoxPrim) -> (Vec<[f64; 3]>, Vec<[f64; 3]>) {
    let ax: Vec<[f64; 3]> = if b.oriented == 0 {
        vec![[1.0, 0.0, 0.0], [0.0, 1.0, 0.0], [0.0, 0.0, 1.0]]
    } else {
        (0..3).map(|i| row(&b.basis, i)).collect()
    };
    let c = [
        f64::from(b.center.x),
        f64::from(b.center.y),
        f64::from(b.center.z),
    ];
    let mut pts = Vec::new();
    for k in 0..8 {
        let mut p = c;
        for (a, axis) in ax.iter().enumerate() {
            let sgn = if k >> a & 1 == 0 { -1.0 } else { 1.0 };
            for (pi, ai) in p.iter_mut().zip(axis) {
                *pi += sgn * f64::from(b.size[a]) * ai;
            }
        }
        pts.push(p);
    }
    (pts, ax)
}

fn sub(a: &[f64; 3], b: &[f64; 3]) -> [f64; 3] {
    [a[0] - b[0], a[1] - b[1], a[2] - b[2]]
}

fn cross(a: &[f64; 3], b: &[f64; 3]) -> [f64; 3] {
    [
        a[1] * b[2] - a[2] * b[1],
        a[2] * b[0] - a[0] * b[2],
        a[0] * b[1] - a[1] * b[0],
    ]
}

/// Convex sets given by their points intersect *with margin* (no separating axis among `axes`
/// even after shrinking by 1e-4).
fn penetrate(p: &[[f64; 3]], q: &[[f64; 3]], axes: &[[f64; 3]]) -> bool {
    axes.iter().filter(|l| dot(l, l) > 1e-12).all(|l| {
        let n = dot(l, l).sqrt();
        let (pa, pb) = p.iter().fold((f64::MAX, f64::MIN), |(lo, hi), x| {
            (lo.min(dot(x, l) / n), hi.max(dot(x, l) / n))
        });
        let (qa, qb) = q.iter().fold((f64::MAX, f64::MIN), |(lo, hi), x| {
            (lo.min(dot(x, l) / n), hi.max(dot(x, l) / n))
        });
        pa + 1e-4 < qb && qa + 1e-4 < pb
    })
}

fn edges(t: &[[f64; 3]]) -> Vec<[f64; 3]> {
    vec![sub(&t[1], &t[0]), sub(&t[2], &t[1]), sub(&t[0], &t[2])]
}

fn tris_intersect(a: &[[f64; 3]], b: &[[f64; 3]]) -> bool {
    let (ea, eb) = (edges(a), edges(b));
    let mut axes = vec![cross(&ea[0], &ea[1]), cross(&eb[0], &eb[1])];
    for x in &ea {
        for y in &eb {
            axes.push(cross(x, y));
        }
    }
    penetrate(a, b, &axes)
}

fn tri_box_intersect(t: &[[f64; 3]], b: &BoxPrim) -> bool {
    let (pts, ax) = box_pts(b);
    let e = edges(t);
    let mut axes = ax.clone();
    axes.push(cross(&e[0], &e[1]));
    for x in &ax {
        for y in &e {
            axes.push(cross(x, y));
        }
    }
    penetrate(t, &pts, &axes)
}

fn build_aabb(v: &[Vec3], idx: &mut [i32]) -> AabbTree {
    let mut t = AabbTree::new(2, 4, 0.1, Mat33::IDENTITY, false);
    t.build(&mut BuildMesh {
        vertices: v,
        indices: idx,
        mats: None,
        foreign_idx: None,
        aux: None,
    });
    t
}

fn build_obb(v: &[Vec3], idx: &mut [i32]) -> ObbTree {
    let mut t = ObbTree::new(2, 4, 0.1);
    t.build(&mut BuildMesh {
        vertices: v,
        indices: idx,
        mats: None,
        foreign_idx: None,
        aux: None,
    });
    t
}

/// A rotation that stands the second sheet up across the first: `a` about z, then 1.2 rad
/// about x.
fn rot(a: f32) -> Mat33 {
    let (c, s) = (a.cos(), a.sin());
    let (cx, sx) = (1.2f32.cos(), 1.2f32.sin());
    let rz = Mat33::from_array([c, -s, 0.0, s, c, 0.0, 0.0, 0.0, 1.0]);
    let rx = Mat33::from_array([1.0, 0.0, 0.0, 0.0, cx, -sx, 0.0, sx, cx]);
    rx.mul_mat(&rz)
}

fn run(
    a: &dyn BvTree,
    b: &dyn BvTree,
    va: (&[Vec3], &[i32]),
    vb: (&[Vec3], &[i32]),
    r: Mat33,
    off: Vec3,
    scale: f32,
) {
    let mut ar = BvArena::new();
    let ha = a.get_node_bv(&mut ar, 0);
    let hb = b.get_node_bv_transformed(&mut ar, &r, off, scale, 0);
    let mut cx = Ctx {
        a,
        b,
        r,
        off,
        scale,
        pairs: BTreeSet::new(),
    };
    traverse(&mut cx, &mut ar, ha, hb);
    assert_eq!(ar.bv_count, 2, "BV pool not balanced");
    assert!(!cx.pairs.is_empty());
    // Every pair of intersecting triangles is reached.
    let na = (va.1.len() / 3) as i32;
    let nb = (vb.1.len() / 3) as i32;
    let mut overlapping = 0;
    for i in 0..na {
        let ti = tri_pts(va.0, va.1, i, &Mat33::IDENTITY, Vec3::ZERO, 1.0);
        for j in 0..nb {
            let tj = tri_pts(vb.0, vb.1, j, &r, off, scale);
            if tris_intersect(&ti, &tj) {
                overlapping += 1;
                assert!(
                    cx.pairs.contains(&(i, j)),
                    "triangle pair ({i}, {j}) missed"
                );
            }
        }
    }
    assert!(overlapping > 0);
}

#[test]
fn aabb_vs_obb_traversal_reaches_every_overlap() {
    let (va, mut ia) = sheet(8, 0.0);
    let (vb, mut ib) = sheet(6, 1.0);
    let ta = build_aabb(&va, &mut ia);
    let tb = build_obb(&vb, &mut ib);
    run(
        &ta,
        &tb,
        (&va, &ia),
        (&vb, &ib),
        rot(0.4),
        Vec3::new(0.7, 1.0, -1.0),
        1.1,
    );
}

#[test]
fn obb_vs_aabb_traversal_reaches_every_overlap() {
    let (va, mut ia) = sheet(7, 0.5);
    let (vb, mut ib) = sheet(7, 2.0);
    let ta = build_obb(&va, &mut ia);
    let tb = build_aabb(&vb, &mut ib);
    run(
        &ta,
        &tb,
        (&va, &ia),
        (&vb, &ib),
        rot(-0.9),
        Vec3::new(1.2, 0.4, -0.1),
        0.9,
    );
}

#[test]
fn mesh_vs_single_box_query() {
    let (va, mut ia) = sheet(10, 0.3);
    let ta = build_aabb(&va, &mut ia);
    let q = SingleBoxTree::new(
        BoxPrim::aligned(Vec3::new(1.0, 1.5, 0.0), Vec3::new(0.6, 0.4, 2.0)),
        1,
    );
    let mut ar = BvArena::new();
    let ha = ta.get_node_bv(&mut ar, 0);
    let r = rot(0.25);
    let hb = q.get_node_bv_transformed(&mut ar, &r, Vec3::new(0.5, 0.5, 0.0), 1.0, 0);
    let mut cx = Ctx {
        a: &ta,
        b: &q,
        r,
        off: Vec3::new(0.5, 0.5, 0.0),
        scale: 1.0,
        pairs: BTreeSet::new(),
    };
    traverse(&mut cx, &mut ar, ha, hb);
    assert_eq!(ar.bv_count, 2);
    let qb = ar.bv_box(hb);
    for i in 0..(ia.len() / 3) as i32 {
        let ti = tri_pts(&va, &ia, i, &Mat33::IDENTITY, Vec3::ZERO, 1.0);
        if tri_box_intersect(&ti, &qb) {
            assert!(cx.pairs.contains(&(i, 0)), "triangle {i} missed");
        }
    }
}
