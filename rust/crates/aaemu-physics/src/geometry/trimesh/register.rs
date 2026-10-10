//! `CTriMesh` slot 54 `RegisterIntersection` (0x351D3950) and slots 58 / 59. (In progress.)

use super::protocol::{GeomProtocol, IntersectCtx};
use super::TriMesh;
use crate::collision::contact::PrimInters;
use crate::geometry::shapes::protocol::IntersectionProtocol;

/// Slots 49/57/58/59 of `CTriMesh`.
impl IntersectionProtocol for TriMesh {}

/// `CTriMesh` slot 54 (0x351D3950).
pub fn mesh_register_intersection(
    _m: &TriMesh,
    _ctx: &mut IntersectCtx<'_>,
    _side: usize,
    _other: &dyn GeomProtocol,
    _i_this: usize,
    _i_other: usize,
    _inters: &mut PrimInters,
) -> i32 {
    todo!("CTriMesh::RegisterIntersection 0x351D3950")
}
