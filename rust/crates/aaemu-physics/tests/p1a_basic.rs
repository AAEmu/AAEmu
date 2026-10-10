//! P1a: bit-exact checks of the simple primitive slots against the oracle (the real
//! `cryphysics_dedicate.dll`): construction / `SetData`, GetType, GetBBox,
//! CalcPhysicalProperties, GetVolume, GetCenter, GetPrimitive, the header fields, IsConvex,
//! GetPrimitiveCount, IsAPrimitive, sizeof, GetExtent, the ray's `ndir`, PointInsideStatus,
//! Save/Load and the CGF physics blob (`CGeomManager::LoadPhysGeometry`).
//!
//! Scenarios: `tests/scenarios/p1a_basic.scn`, `p1a_inside.scn`, `p1a_save.scn`.

mod p1a_support;

use aaemu_physics::geometry::shapes::stream::{load_phys_geometry, Reader};
use aaemu_physics::geometry::shapes::PrimitiveGeom;
use aaemu_physics::geometry::shapes::{BoxGeom, CapsuleGeom, CylinderGeom, SphereGeom};
use aaemu_physics::geometry::Geometry;
use aaemu_physics::math::Vec3;
use p1a_support::*;

/// The tokens `p1a_basic()` prints for a geometry.
fn basic_tokens(a: &AnyGeom) -> Vec<String> {
    let g = a.g();
    let mut o = vec![g.geom_type().id().to_string(), "bbox".into()];
    push_box(&mut o, &g.bbox());
    o.push("props".into());
    match g.calc_physical_properties() {
        Some(p) => {
            o.push("1".into());
            push_v(&mut o, p.inertia);
            for f in p.q.to_array() {
                o.push(hx(f));
            }
            push_v(&mut o, p.origin);
            o.push(hx(p.volume));
        }
        None => o.push("0".into()),
    }
    o.push("vol".into());
    o.push(hx(g.volume()));
    o.push("center".into());
    push_v(&mut o, g.center());
    let prim = g.primitive(0).unwrap();
    o.push("prim".into());
    o.push(prim.byte_size().to_string());
    match prim {
        aaemu_physics::geometry::primitives::Primitive::Box(b) => push_box(&mut o, &b),
        aaemu_physics::geometry::primitives::Primitive::Sphere(s) => {
            push_v(&mut o, s.center);
            o.push(hx(s.r));
        }
        aaemu_physics::geometry::primitives::Primitive::Cylinder(c)
        | aaemu_physics::geometry::primitives::Primitive::Capsule(c) => {
            push_v(&mut o, c.center);
            push_v(&mut o, c.axis);
            o.push(hx(c.r));
            o.push(hx(c.hh));
        }
        aaemu_physics::geometry::primitives::Primitive::Ray(r) => {
            push_v(&mut o, r.origin);
            push_v(&mut o, r.dir);
        }
        other => panic!("{other:?}"),
    }
    let hdr = match a {
        AnyGeom::Box(g) => *g.header(),
        AnyGeom::Sphere(g) => *g.header(),
        AnyGeom::Cylinder(g) => *g.header(),
        AnyGeom::Capsule(g) => *g.header(),
        AnyGeom::Ray(g) => *g.header(),
    };
    o.push("hdr".into());
    o.push(hx(hdr.min_vtx_dist));
    o.push(hi(hdr.priority));
    o.push(hi(hdr.flag24));
    assert_eq!(hdr.min_vtx_dist, g.min_vtx_dist());
    assert_eq!(hdr.priority, g.priority());
    o.push("cnt".into());
    o.push(g.primitive_count().to_string());
    o.push("conv".into());
    o.push(i32::from(g.is_convex(0.02)).to_string());
    o.push("isprim".into());
    o.push(i32::from(g.is_a_primitive()).to_string());
    o.push("size".into());
    o.push(g.size_of_object().to_string());
    o.push("ext".into());
    for f in 0..4 {
        o.push(hx(g.extent(f)));
    }
    match a {
        AnyGeom::Ray(r) => {
            o.push("ndir".into());
            push_v(&mut o, r.ndir);
        }
        AnyGeom::Cylinder(c) => {
            o.push("ntess".into());
            o.push(c.0.n_tess.to_string());
        }
        AnyGeom::Capsule(c) => {
            o.push("ntess".into());
            o.push(c.0.n_tess.to_string());
        }
        _ => {}
    }
    o
}

#[test]
fn basic_slots_match_the_oracle() {
    let cs = cases("p1a_basic");
    let mut fails = Vec::new();
    for c in &cs {
        check(c, &basic_tokens(&AnyGeom::of(c)), &mut fails);
    }
    finish("basic slots", cs.len(), &fails);
}

#[test]
fn point_inside_matches_the_oracle() {
    // includes the fixed degenerate primitives of `gdegen` (basic slots too)
    let cs = cases("p1a_inside");
    let mut fails = Vec::new();
    for c in &cs {
        let a = AnyGeom::of(c);
        if c.probe == "basic" {
            check(c, &basic_tokens(&a), &mut fails);
            continue;
        }
        let r = a.g().point_inside_status(c.v(0));
        check(c, &[r.to_string()], &mut fails);
    }
    finish("PointInsideStatus", cs.len(), &fails);
}

#[test]
fn save_load_matches_the_oracle() {
    let cs = cases("p1a_save");
    let mut fails = Vec::new();
    for c in &cs {
        let a = AnyGeom::of(c);
        let mut bytes = Vec::new();
        a.g().save(&mut bytes);
        let mut o = vec![bytes.len().to_string()];
        for w in bytes.chunks(4) {
            o.push(format!(
                "{:08x}",
                u32::from_le_bytes([w[0], w[1], w[2], w[3]])
            ));
        }
        // load into a fresh object of the same class (the oracle loads into a second,
        // differently initialised object; Load overwrites every serialized field)
        let mut r = Reader::new(&bytes);
        let loaded = match c.ty {
            0 => {
                let mut g = BoxGeom::empty();
                g.load(&mut r).unwrap();
                AnyGeom::Box(g)
            }
            4 => {
                let mut g = SphereGeom::empty();
                g.load(&mut r).unwrap();
                AnyGeom::Sphere(g)
            }
            5 => {
                let mut g = CylinderGeom::empty();
                g.0.load(&mut r).unwrap();
                AnyGeom::Cylinder(g)
            }
            6 => {
                let mut g = CapsuleGeom::empty();
                g.0.load(&mut r).unwrap();
                AnyGeom::Capsule(g)
            }
            _ => a,
        };
        o.push("load".into());
        o.push(r.pos.to_string());
        o.push("O".into());
        o.extend(basic_tokens(&loaded));
        check(c, &o, &mut fails);
    }
    finish("Save/Load", cs.len(), &fails);
}

#[test]
fn phys_blob_matches_load_phys_geometry() {
    let ls = lines("p1a_save", "gb");
    assert!(!ls.is_empty());
    let mut fails = Vec::new();
    for l in &ls {
        let i = l.iter().position(|s| s == "I").unwrap();
        let o = l.iter().position(|s| s == "O").unwrap();
        let mut bytes = Vec::new();
        for w in &l[i + 1..o] {
            bytes.extend_from_slice(&u32::from_str_radix(w, 16).unwrap().to_le_bytes());
        }
        let pg = load_phys_geometry(&bytes).unwrap();
        let h = pg.header;
        let mut got = vec![pg.len.to_string()];
        let mut push = |v: Vec3| {
            got.push(hx(v.x));
            got.push(hx(v.y));
            got.push(hx(v.z));
        };
        push(h.inertia);
        let mut got2 = got;
        for f in h.q.to_array() {
            got2.push(hx(f));
        }
        let mut got = got2;
        got.push(hx(h.origin.x));
        got.push(hx(h.origin.y));
        got.push(hx(h.origin.z));
        got.push(hx(h.volume));
        got.push(hi(h.unk52));
        got.push(hi(h.surface_idx));
        got.push("geom".into());
        got.push(hx(pg.geom.as_geom().min_vtx_dist()));
        if got.as_slice() != &l[o + 1..] {
            fails.push(format!("{l:?}\n  port {got:?}"));
        }
        // the blob writer reproduces the bytes (header words 0/14/15 are garbage in the
        // original; the probe wrote random values there, so compare the rest)
        let mut again = Vec::new();
        aaemu_physics::geometry::shapes::stream::save_geometry(pg.geom.as_geom(), &mut again);
        assert_eq!(&bytes[68..], &again[..], "SaveGeometry bytes");
    }
    finish("LoadPhysGeometry", ls.len(), &fails);
}

#[test]
fn geometry_trait_defaults() {
    let b = BoxGeom::new(&box_prim(&[
        0x3f80_0000,
        0,
        0,
        0,
        0x3f80_0000,
        0,
        0,
        0,
        0x3f80_0000,
        0,
        0,
        0,
        0,
        0x3f80_0000,
        0x4000_0000,
        0x4040_0000,
    ]));
    assert_eq!(b.prim.oriented, 0);
    assert_eq!(b.volume(), 48.0);
    assert_eq!(b.primitive_mat_id(0), -1);
    assert_eq!(b.foreign_idx(0), -1);
    assert_eq!(b.normal(0), Vec3::Z);
    assert_eq!(b.extent(0), 8.0);
}

/// Large fuzz runs straight from the oracle (only with `AAEMU_PHYS_ORACLE`; no goldens).
#[test]
fn basic_and_inside_large_fuzz_with_oracle() {
    let text = "gprobe probe=basic type=box n=3000 seed=1101\n\
                gprobe probe=basic type=sphere n=1000 seed=1102\n\
                gprobe probe=basic type=cylinder n=3000 seed=1103\n\
                gprobe probe=basic type=capsule n=3000 seed=1104\n\
                gprobe probe=basic type=ray n=1000 seed=1105\n\
                gprobe probe=inside type=box n=5000 seed=1201\n\
                gprobe probe=inside type=sphere n=3000 seed=1202\n\
                gprobe probe=inside type=cylinder n=5000 seed=1203\n\
                gprobe probe=inside type=capsule n=5000 seed=1204\n";
    let Some(cs) = adhoc_cases(text) else {
        return;
    };
    let mut fails = Vec::new();
    for c in &cs {
        let a = AnyGeom::of(c);
        if c.probe == "basic" {
            check(c, &basic_tokens(&a), &mut fails);
        } else {
            check(c, &[a.g().point_inside_status(c.v(0)).to_string()], &mut fails);
        }
    }
    finish("basic/inside fuzz", cs.len(), &fails);
}
