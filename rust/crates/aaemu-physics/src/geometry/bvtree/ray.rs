//! `CRayBV` (vtable 0x351EDEC8): the BV of a ray geometry is the ray itself (record type 3 in
//! the arena's ray slot, +0x39F48). `+8` geometry, `+16` pointer to the ray primitive (the port
//! keeps a copy; the ray geometry updates it with [`RayBv::set_ray`]).

use super::{vec_to_words, BvArena, BvHandle, BvTree, NodeContents};
use crate::geometry::primitives::{BoxPrim, RayPrim};
use crate::ids::BvTreeType;
use crate::math::{Mat33, Vec3};

/// `CRayBV`.
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct RayBv {
    pub ray: RayPrim,
}

impl RayBv {
    #[must_use]
    pub fn new(ray: RayPrim) -> Self {
        Self { ray }
    }

    /// Mirrors a change of the ray geometry's data (the original reads it through a pointer).
    pub fn set_ray(&mut self, ray: RayPrim) {
        self.ray = ray;
    }

    fn write(arena: &mut BvArena, origin: Vec3, dir: Vec3) -> BvHandle {
        let w = &mut arena.ray_bv;
        w[0] = 3;
        w[1] = 0;
        vec_to_words(w, 2, origin);
        vec_to_words(w, 5, dir);
        BvHandle::Ray
    }
}

impl BvTree for RayBv {
    /// 0x35024CD0.
    fn tree_type(&self) -> BvTreeType {
        BvTreeType::Ray
    }

    /// Slot 2 is a nop for `CRayBV` (the caller's box is left untouched); the port returns a
    /// default box. The ray geometry's own `GetBBox` (0x35199F50) is geometry slot 6 (P1a).
    fn bbox(&self) -> BoxPrim {
        BoxPrim::default()
    }

    /// Base `r1`.
    fn max_prims_in_node(&self) -> i32 {
        1
    }

    /// 0x35199DC0: `origin' = (R·o)·scale + offset`, `dir' = (R·d)·scale`.
    fn get_node_bv_transformed(
        &self,
        arena: &mut BvArena,
        r: &Mat33,
        offset: Vec3,
        scale: f32,
        _i_node: i32,
    ) -> BvHandle {
        let o = r.mul_vec(self.ray.origin) * scale + offset;
        let d = r.mul_vec(self.ray.dir) * scale;
        Self::write(arena, o, d)
    }

    /// 0x35199D40.
    fn get_node_bv(&self, arena: &mut BvArena, _i_node: i32) -> BvHandle {
        Self::write(arena, self.ray.origin, self.ray.dir)
    }

    /// 0x35199CD0: `(0, 1)`.
    fn get_node_contents(&self, _i_node: i32) -> NodeContents {
        NodeContents {
            first_prim: 0,
            n_prims: 1,
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::geometry::bvtree::BvPrim;

    #[test]
    fn transformed_ray_record() {
        let t = RayBv::new(RayPrim {
            origin: Vec3::new(1.0, 0.0, 0.0),
            dir: Vec3::new(0.0, 0.0, 2.0),
        });
        let mut a = BvArena::new();
        let rz = Mat33::from_array([0.0, -1.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0]);
        let h = t.get_node_bv_transformed(&mut a, &rz, Vec3::new(0.0, 0.0, 5.0), 2.0, 0);
        let r = a.bv_record(h);
        assert_eq!((r.kind, r.i_node), (3, 0));
        match r.prim {
            BvPrim::Ray(p) => {
                assert_eq!(p.origin, Vec3::new(0.0, 2.0, 5.0));
                assert_eq!(p.dir, Vec3::new(0.0, 0.0, 4.0));
            }
            _ => panic!(),
        }
    }
}
