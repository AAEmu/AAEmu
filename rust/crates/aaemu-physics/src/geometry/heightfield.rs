//! **P1c — heightfield**: `CHeightfield` : `CTriMesh` (0x320 bytes, SetData 0x35154F40,
//! PointInsideStatus 0x35154D00) and `CHeightfieldBV`, which hands out the triangles of the
//! overlapped cells (cell split on the u+v = 1 diagonal). Terrain is registered with world
//! slot 17 `SetHeightfieldData`, which creates one terrain entity per caller slot
//! (world+1568+8·i; part flags 0xFDFF). A heightfield geometry added to an ordinary entity gets
//! an empty bbox and never collides (oracle `ray_heightfield.scn`). `geometry.md` §1.3.
//! (In progress.)
