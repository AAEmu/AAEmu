//! The [`Geometry`] slots of `CTriMesh` and `CHeightfield`, wired to the ports in the
//! submodules, and [`geometry_intersect`]: slot 10 through the `Geometry` interface.
//!
//! **Per-caller state.** The original keeps the intersection scratch (BV pools, contact pool
//! counters, …) in a per-thread arena; [`geometry_intersect`] uses a thread-local
//! [`CallerArena`] and process-wide copies of the INTERSECT/OVERLAP/UNPROJECT tables.
//!
//! **Caches.** `CTriMesh` caches its volume, centre, extents and physical properties in the
//! object the first time they are asked for (`+516..+536`); the cached and the computed values
//! are identical. The `&self` slots here use the cache when it is filled and otherwise compute
//! on a copy; [`TriMesh::warm_caches`] fills them once so that later calls are cheap.
//!
//! **`gwd` of slot 10.** The engine writes `(0,0,1)` into A's velocity through its `const
//! geom_world_data*` when a sweep has no velocity; the trait passes `&GeomWorldData`, so that
//! write is not visible to the caller here (it is through [`super::intersect::intersect`]).

use super::intersect::{intersect, IntersectExtra, Tables};
use super::protocol::{CallerArena, GeomProtocol};
use super::TriMesh;
use crate::collision::contact::{GeomContact, IntersectionParams};
use crate::collision::pairs::PairTables;
use crate::collision::swept::UnprojTables;
use crate::geometry::heightfield::protocol::{HfOther, SharedHeightfield};
use crate::geometry::primitives::{BoxPrim, Plane, Primitive};
use crate::geometry::{GeomWorldData, Geometry, PhysProperties};
use crate::ids::GeomType;
use crate::math::Vec3;
use std::cell::RefCell;
use std::sync::OnceLock;

/// The dispatch tables (built once).
#[must_use]
pub fn tables() -> Tables<'static> {
    static T: OnceLock<(PairTables, UnprojTables)> = OnceLock::new();
    let t = T.get_or_init(|| (PairTables::build(), UnprojTables::build()));
    Tables {
        pairs: &t.0,
        unproj: &t.1,
    }
}

thread_local! {
    static ARENA: RefCell<CallerArena> = RefCell::new(CallerArena::new());
}

/// Runs `f` with this thread's [`CallerArena`] (the per-caller scratch of the original).
pub fn with_arena<R>(f: impl FnOnce(&mut CallerArena) -> R) -> R {
    ARENA.with(|a| f(&mut a.borrow_mut()))
}

/// Slot 10 of `a` against a geometry seen through the [`Geometry`] interface: the generic
/// `CGeometry::Intersect` (0x3514FD70), or — when `a` is a mesh — `CTriMesh::Intersect`
/// (0x351C9910, its ray branch). Appends the contacts to `contacts`; returns their number.
/// `other` must offer the protocol ([`Geometry::as_protocol`]); otherwise no contact.
pub fn geometry_intersect(
    a: &dyn GeomProtocol,
    mesh: Option<&TriMesh>,
    other: &dyn Geometry,
    gwd1: &GeomWorldData,
    gwd2: &GeomWorldData,
    params: &mut IntersectionParams,
    contacts: &mut Vec<GeomContact>,
) -> usize {
    let Some(b) = other.as_protocol() else {
        return 0;
    };
    let mut ga = *gwd1;
    let r = with_arena(|arena| match mesh {
        Some(m) => m.intersect_slot10(
            tables(),
            arena,
            b,
            Some(&mut ga),
            Some(gwd2),
            Some(params),
            IntersectExtra::default(),
        ),
        None => intersect(
            tables(),
            arena,
            a,
            b,
            Some(&mut ga),
            Some(gwd2),
            Some(params),
            IntersectExtra::default(),
        ),
    });
    let n = r.contacts.len();
    contacts.extend(r.contacts);
    n
}

impl TriMesh {
    /// Fills the volume / centre / extent caches (slots 31, 32, 42) so that the `&self`
    /// [`Geometry`] slots do not compute on a copy.
    pub fn warm_caches(&mut self) {
        let _ = self.get_volume();
        let _ = self.get_center();
        let _ = self.get_extent(1);
        let _ = self.get_extent(2);
    }
}

fn unused(x: f32) -> bool {
    super::props::is_unused_bits(x)
}

impl Geometry for TriMesh {
    fn geom_type(&self) -> GeomType {
        GeomType::TriMesh
    }

    /// Slot 6 (0x35103340).
    fn bbox(&self) -> BoxPrim {
        self.get_bbox()
    }

    /// Slot 7 (0x351DA290).
    fn calc_physical_properties(&self) -> Option<PhysProperties> {
        Some(TriMesh::calc_physical_properties(&mut self.clone()))
    }

    /// Slot 8 → 61 (0x351DB380 → 0x351C8600).
    fn point_inside_status(&self, pt: Vec3) -> i32 {
        TriMesh::point_inside_status(&mut self.clone(), pt)
    }

    /// Slot 13 (0x351DA620).
    fn calculate_buoyancy(&self, plane: &Plane, gwd: &GeomWorldData) -> (f32, Vec3) {
        TriMesh::calculate_buoyancy(self, plane, gwd)
    }

    /// Slot 14 (0x351CD0C0) from zeroed accumulators.
    fn calculate_medium_resistance(&self, plane: &Plane, gwd: &GeomWorldData) -> (Vec3, Vec3) {
        let (mut p, mut l) = (Vec3::ZERO, Vec3::ZERO);
        TriMesh::calculate_medium_resistance(self, plane, gwd, &mut p, &mut l);
        (p, l)
    }

    /// Slot 14 with the binary's in/out accumulators.
    fn calculate_medium_resistance_acc(
        &self,
        plane: &Plane,
        gwd: &GeomWorldData,
        dp: &mut Vec3,
        dl: &mut Vec3,
    ) {
        TriMesh::calculate_medium_resistance(self, plane, gwd, dp, dl);
    }

    /// Slot 17 (0x3514DB60).
    fn primitive(&self, i_prim: i32) -> Option<Primitive> {
        self.get_primitive(i_prim).map(Primitive::Triangle)
    }

    /// Slot 28.
    fn primitive_count(&self) -> i32 {
        self.get_primitive_count()
    }

    /// Slot 31 (0x351DB0D0).
    fn volume(&self) -> f32 {
        if !unused(self.cached_volume) {
            return self.cached_volume;
        }
        TriMesh::get_volume(&mut self.clone())
    }

    /// Slot 32 (0x351BE500).
    fn center(&self) -> Vec3 {
        if !unused(self.cached_center.x) {
            return self.cached_center;
        }
        TriMesh::get_center(&mut self.clone())
    }

    fn is_a_primitive(&self) -> bool {
        false
    }

    fn min_vtx_dist(&self) -> f32 {
        self.min_vtx_dist
    }

    fn priority(&self) -> i32 {
        self.priority
    }

    /// Slot 10 (0x351C9910): the ray branch when the ray grid is built, else the generic
    /// `Intersect`.
    fn intersect(
        &self,
        other: &dyn Geometry,
        gwd1: &GeomWorldData,
        gwd2: &GeomWorldData,
        params: &mut IntersectionParams,
        contacts: &mut Vec<GeomContact>,
    ) -> usize {
        geometry_intersect(self, Some(self), other, gwd1, gwd2, params, contacts)
    }

    /// Slot 11 (0x351C6780).
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
        TriMesh::find_closest_point(
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

    /// Slot 12 (0x351CBE20).
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
        TriMesh::calc_volumetric_pressure(self, gwd, epicenter, k, rmin, center_of_mass, p, l);
    }

    /// Slot 16.
    fn primitive_mat_id(&self, i_prim: i32) -> i32 {
        self.get_primitive_mat_id(i_prim)
    }

    /// Slot 18.
    fn foreign_idx(&self, i_prim: i32) -> i32 {
        self.get_foreign_idx(i_prim)
    }

    /// Slot 19.
    fn normal(&self, i_prim: i32) -> Vec3 {
        self.get_normal(i_prim)
    }

    /// Slot 20 (0x351BE700).
    fn get_feature(&self, i_prim: i32, i_feature: i32, pts: &mut [Vec3]) -> i32 {
        let (n, p) = TriMesh::get_feature(self, i_prim, i_feature);
        for (d, s) in pts.iter_mut().zip(p.iter()).take(n.max(0) as usize) {
            *d = *s;
        }
        n
    }

    /// Slot 21 (0x351C2490).
    fn is_convex(&self, tolerance: f32) -> bool {
        TriMesh::is_convex(&mut self.clone(), tolerance) != 0
    }

    /// Slot 25 (0x351CECB0).
    fn save(&self, out: &mut Vec<u8>) {
        out.extend(self.save_to_vec());
    }

    /// Slot 42 (0x351C2040).
    fn extent(&self, form: i32) -> f32 {
        TriMesh::get_extent(&mut self.clone(), form)
    }

    /// Slot 48 (0x351C2A60): the first triangle in world space.
    fn prepare_primitive(&self, gwd: &GeomWorldData) -> Option<Primitive> {
        Some(Primitive::Triangle(self.prepare_primitive_slot48(gwd)))
    }

    fn as_protocol(&self) -> Option<&dyn GeomProtocol> {
        Some(self)
    }
}

impl Geometry for SharedHeightfield {
    fn geom_type(&self) -> GeomType {
        GeomType::Heightfield
    }

    fn bbox(&self) -> BoxPrim {
        self.read().bbox()
    }

    /// Slot 7 is the mesh's (0x351DA290) on the heightfield's empty mesh base.
    fn calc_physical_properties(&self) -> Option<PhysProperties> {
        Some(TriMesh::calc_physical_properties(
            &mut self.read().mesh.clone(),
        ))
    }

    /// Slot 8 (0x35154D00).
    fn point_inside_status(&self, pt: Vec3) -> i32 {
        self.read().point_inside_status(pt)
    }

    /// Slot 13 is the mesh's; the heightfield's mesh base has nTris 0 (volume 0, centre 0).
    fn calculate_buoyancy(&self, plane: &Plane, gwd: &GeomWorldData) -> (f32, Vec3) {
        self.read().mesh.calculate_buoyancy(plane, gwd)
    }

    /// Slot 14 is the mesh's (no triangles: zero).
    fn calculate_medium_resistance(&self, plane: &Plane, gwd: &GeomWorldData) -> (Vec3, Vec3) {
        let (mut p, mut l) = (Vec3::ZERO, Vec3::ZERO);
        self.read()
            .mesh
            .calculate_medium_resistance(plane, gwd, &mut p, &mut l);
        (p, l)
    }

    /// Slot 17 (0x351036C0).
    fn primitive(&self, i_prim: i32) -> Option<Primitive> {
        let hf = self.read();
        let mut p = *hf.prim();
        hf.get_primitive(i_prim, &mut p);
        Some(Primitive::Heightfield(p))
    }

    fn primitive_count(&self) -> i32 {
        self.read().primitive_count()
    }

    /// Slot 31 is the mesh's.
    fn volume(&self) -> f32 {
        self.read().mesh.clone().get_volume()
    }

    /// Slot 32 (0x35103420).
    fn center(&self) -> Vec3 {
        self.read().center()
    }

    fn is_a_primitive(&self) -> bool {
        false
    }

    fn min_vtx_dist(&self) -> f32 {
        self.read().mesh.min_vtx_dist
    }

    fn priority(&self) -> i32 {
        self.read().mesh.priority
    }

    /// Slot 10 (0x35156E20): rays take the heightfield's own branch, everything else the
    /// generic `Intersect`.
    fn intersect(
        &self,
        other: &dyn Geometry,
        gwd1: &GeomWorldData,
        gwd2: &GeomWorldData,
        params: &mut IntersectionParams,
        contacts: &mut Vec<GeomContact>,
    ) -> usize {
        let Some(b) = other.as_protocol() else {
            return 0;
        };
        let o = match other.as_ray() {
            Some(r) => HfOther::Ray(r),
            None => HfOther::Geom(b),
        };
        let mut ga = *gwd1;
        let r = with_arena(|arena| {
            self.intersect(
                tables(),
                arena,
                o,
                &mut ga,
                Some(gwd2),
                Some(params),
                IntersectExtra::default(),
                None,
            )
        });
        let n = r.contacts.len();
        contacts.extend(r.contacts);
        n
    }

    /// Slot 11 (0x35155CB0).
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
        super::closest::heightfield_find_closest_point(
            self.read().prim(),
            gwd,
            i_prim,
            i_feature,
            ptdst0,
            ptdst1,
            ptres,
            n_max_iters,
        )
    }

    fn as_protocol(&self) -> Option<&dyn GeomProtocol> {
        Some(self)
    }
}

impl SharedHeightfield {
    fn read(&self) -> std::sync::RwLockReadGuard<'_, crate::geometry::heightfield::Heightfield> {
        self.0
            .read()
            .unwrap_or_else(std::sync::PoisonError::into_inner)
    }
}
