//! `CBoxGeom` (sizeof 200; vtable slots in `geometry.md` §2.1, column CBoxGeom).
//!
//! Object layout: `+48` box primitive (Basis `+48`, bOriented `+84`, center `+88`, size
//! `+100`), `+112` embedded `CSingleBoxTree`.

use super::{is_identity, GeomHeader, PrimitiveGeom, SingleBoxBv};
use crate::collision::contact::{GeomContact, IntersectionParams};
use crate::geometry::primitives::{BoxPrim, Plane, Primitive};
use crate::geometry::{GeomWorldData, Geometry, PhysProperties};
use crate::ids::GeomType;
use crate::math::{max_idiom, Quat, Vec3};

/// `CBoxGeom`.
#[derive(Clone, Debug, PartialEq)]
pub struct BoxGeom {
    pub hdr: GeomHeader,
    /// `+48` the box primitive (half-extents in `size`).
    pub prim: BoxPrim,
    /// `+112` embedded `CSingleBoxTree` data.
    pub bv: SingleBoxBv,
}

impl BoxGeom {
    /// `CGeomManager::CreatePrimitive(0, prim)` (0x3514B9F0): ctor 0x35075190 + `SetData`.
    #[must_use]
    pub fn new(prim: &BoxPrim) -> Self {
        let mut g = Self::empty();
        g.set_data(prim);
        g
    }

    /// The object right after the ctor `sub_35075190` (everything zero, `+20 = 3`, `+24 = 1`).
    #[must_use]
    pub fn empty() -> Self {
        Self {
            hdr: GeomHeader::solid(),
            prim: BoxPrim {
                basis: crate::math::Mat33::ZERO,
                oriented: 0,
                center: Vec3::ZERO,
                size: Vec3::ZERO,
            },
            bv: SingleBoxBv::default(),
        }
    }

    /// Slot 30 `SetData` (`sub_3513A590`): copies basis, centre and size, sets `bOriented =
    /// !IsIdentity(Basis)` (the incoming flag is ignored), rebuilds the single-box tree and
    /// `m_minVtxDist = ((sy + sx) + sz)·1e-4`.
    pub fn set_data(&mut self, prim: &BoxPrim) {
        self.prim.center = prim.center;
        self.prim.size = prim.size;
        self.prim.basis = prim.basis;
        self.prim.oriented = i32::from(!is_identity(&prim.basis));
        self.bv.set_box(&self.prim);
        let s = self.prim.size;
        self.hdr.min_vtx_dist = ((s.y + s.x) + s.z) * 1e-4;
    }

    /// The point relative to the centre, in the box frame (rotated by `Basis` when oriented),
    /// as slot 8 and others compute it.
    #[inline]
    #[must_use]
    pub fn to_local(&self, pt: Vec3) -> Vec3 {
        let d = pt - self.prim.center;
        if self.prim.oriented != 0 {
            self.prim.basis.mul_vec(d)
        } else {
            d
        }
    }
}

impl PrimitiveGeom for BoxGeom {
    fn header(&self) -> &GeomHeader {
        &self.hdr
    }
}

impl Geometry for BoxGeom {
    /// P1c hook: the intersection protocol (`trimesh::protocol::shapes`).
    fn as_protocol(&self) -> Option<&dyn crate::geometry::trimesh::protocol::GeomProtocol> {
        Some(self)
    }

    /// Slot 1: shared `return 0` stub 0x350376D0.
    fn geom_type(&self) -> GeomType {
        GeomType::Box
    }

    /// Slot 6 (0x35075240) → `CSingleBoxTree` slot 2 (0x351AE1D0): the tree's box.
    fn bbox(&self) -> BoxPrim {
        self.bv.box_
    }

    /// Slot 7 (0x3513F550): `V = ((sy·sx)·sz)·8`; `k = (float)(V·(double)1/3)`;
    /// `Ibody = ((sz²+sy²)·k, (sz²+sx²)·k, (sy²+sx²)·k)`; `q = quat(Basisᵀ)` when oriented
    /// (`sub_35023EB0`), else identity; `origin = center`.
    fn calc_physical_properties(&self) -> Option<PhysProperties> {
        let p = &self.prim;
        let q = if p.oriented != 0 {
            Quat::from_mat33(&p.basis.transpose())
        } else {
            Quat::IDENTITY
        };
        let s = p.size;
        let volume = ((s.y * s.x) * s.z) * 8.0;
        let (sx2, sy2, sz2) = (s.x * s.x, s.y * s.y, s.z * s.z);
        let k = (f64::from(volume) * (1.0 / 3.0)) as f32;
        Some(PhysProperties {
            volume,
            inertia: Vec3::new((sz2 + sy2) * k, (sz2 + sx2) * k, (sy2 + sx2) * k),
            q,
            origin: p.center,
        })
    }

    /// Slot 8 (0x3513A660): `sign(max(max(|x|−sx, |y|−sy), |z|−sz))` with the `max` idiom on
    /// the local point; 1 inside (including `-0.0`), 0 outside.
    fn point_inside_status(&self, pt: Vec3) -> i32 {
        let l = self.to_local(pt);
        let s = self.prim.size;
        let a = l.x.abs() - s.x;
        let b = l.y.abs() - s.y;
        let c = l.z.abs() - s.z;
        (max_idiom(max_idiom(a, b), c).to_bits() >> 31) as i32
    }

    /// Slot 13 (0x3513B1B0): converts the box to a temporary 12-triangle mesh
    /// (`sub_3513AC00`) and runs the mesh buoyancy (0x351DA620), see [`super::buoyancy`].
    fn calculate_buoyancy(&self, plane: &Plane, gwd: &GeomWorldData) -> (f32, Vec3) {
        super::buoyancy::box_buoyancy(self, plane, gwd)
    }

    /// Slot 14 from zeroed accumulators, see [`Geometry::calculate_medium_resistance_acc`].
    fn calculate_medium_resistance(&self, plane: &Plane, gwd: &GeomWorldData) -> (Vec3, Vec3) {
        let (mut dp, mut dl) = (Vec3::ZERO, Vec3::ZERO);
        self.calculate_medium_resistance_acc(plane, gwd, &mut dp, &mut dl);
        (dp, dl)
    }

    /// Slot 14 (0x3513E7C0), see [`super::drag`].
    fn calculate_medium_resistance_acc(
        &self,
        plane: &Plane,
        gwd: &GeomWorldData,
        dp: &mut Vec3,
        dl: &mut Vec3,
    ) {
        super::drag::box_medium_resistance(self, plane, gwd, dp, dl);
    }

    /// Slot 17 (0x350752C0): copies the 64-byte primitive.
    fn primitive(&self, _i_prim: i32) -> Option<Primitive> {
        Some(Primitive::Box(self.prim))
    }

    /// Slot 31 (0x350752A0): `((sy·sx)·sz)·8`.
    fn volume(&self) -> f32 {
        let s = self.prim.size;
        ((s.y * s.x) * s.z) * 8.0
    }

    /// Slot 32 (0x35075270): the centre.
    fn center(&self) -> Vec3 {
        self.prim.center
    }

    fn is_a_primitive(&self) -> bool {
        true
    }

    fn min_vtx_dist(&self) -> f32 {
        self.hdr.min_vtx_dist
    }

    fn priority(&self) -> i32 {
        self.hdr.priority
    }

    /// Slot 10: the shared primitive Intersect 0x35151F20 belongs to P2a/P2b (pair tables and
    /// unprojection); until it is wired this returns no contacts.
    fn intersect(
        &self,
        other: &dyn Geometry,
        gwd1: &GeomWorldData,
        gwd2: &GeomWorldData,
        params: &mut IntersectionParams,
        contacts: &mut Vec<GeomContact>,
    ) -> usize {
        // 0x35151F20 falls back to the generic `CGeometry::Intersect` when the other
        // geometry is not a primitive (wired by P1c); primitive pairs are not ported yet.
        if !other.is_a_primitive() {
            return crate::geometry::trimesh::geometry_impl::geometry_intersect(
                self, None, other, gwd1, gwd2, params, contacts,
            );
        }
        0
    }

    /// Slot 11 (0x3513D020), see [`super::closest`].
    fn find_closest_point(
        &self,
        gwd: &GeomWorldData,
        i_prim: &mut i32,
        i_feature: &mut i32,
        ptdst0: Vec3,
        ptdst1: Vec3,
        ptres: &mut [Vec3; 2],
        n_max_iters: i32,
    ) -> i32 {
        super::closest::box_find_closest_point(
            self,
            gwd,
            i_prim,
            i_feature,
            ptdst0,
            ptdst1,
            ptres,
            n_max_iters,
        )
    }

    /// Slot 12 (0x3513B0F0): through the temporary 12-triangle mesh, see [`super::buoyancy`].
    fn calc_volumetric_pressure(
        &self,
        gwd: &GeomWorldData,
        epicenter: Vec3,
        k: f32,
        rmin: f32,
        center_of_mass: Vec3,
        p: &mut Vec3,
        l: &mut Vec3,
    ) {
        super::buoyancy::box_volumetric_pressure(
            self,
            gwd,
            epicenter,
            k,
            rmin,
            center_of_mass,
            p,
            l,
        );
    }

    /// Slot 20 (0x3513A780), see [`super::misc`].
    fn get_feature(&self, i_prim: i32, i_feature: i32, pts: &mut [Vec3]) -> i32 {
        super::misc::box_get_feature(self, i_prim, i_feature, pts)
    }

    /// Slot 25 (0x3513B240), see [`super::stream`].
    fn save(&self, out: &mut Vec<u8>) {
        super::stream::save_box(self, out);
    }

    /// Slot 48 (0x3513B580), see [`super::protocol`].
    fn prepare_primitive(&self, gwd: &GeomWorldData) -> Option<Primitive> {
        Some(super::protocol::box_prepare_primitive(self, gwd))
    }

    /// Slot 52 (0x35075290).
    fn size_of_object(&self) -> i32 {
        200
    }
}
