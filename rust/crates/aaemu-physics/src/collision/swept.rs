//! **P2b — unprojection / continuous collision** (stub). UNPROJECT table at 0x35209FA0
//! (`[2][8][8]`, mode 0 linear, 1 rotational; 13 large functions 0x3519A5C0–0x351A9330),
//! `RegisterIntersection` (CGeometry slot 54, 0x3514F3F0; CTriMesh 0x351D3950), sweep handling
//! inside Intersect. Uses [`crate::collision::contact::UnprojParams`] /
//! [`crate::collision::contact::UnprojResult`]. `geometry.md` §4.4, §4.6, §5.
