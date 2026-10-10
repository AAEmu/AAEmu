//! **P1a — primitive geometries** (stub): `CBoxGeom` (0xC8 bytes, ctor 0x35075190),
//! `CSphereGeom` (0x98, 0x35038C20), `CCylinderGeom` (0xB0, 0x35038960), `CCapsuleGeom`
//! (0xB0, 0x35038AB0) and `CRayGeom` (0x70, 0x3514D950), each implementing
//! [`crate::geometry::Geometry`] on top of [`crate::geometry::primitives`]:
//! GetBBox, CalcPhysicalProperties, PointInsideStatus, GetPrimitive/SetData, GetVolume/GetCenter,
//! CalculateBuoyancy (the box converts itself to a 12-triangle mesh, 0x3513AC00) and
//! CalculateMediumResistance (the box has its own code, 0x3513E7C0). Slot table:
//! `rust/docs/physics/cryphysics/geometry.md` §2.1.
