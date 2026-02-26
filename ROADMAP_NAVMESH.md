# NavMesh Roadmap

## Completed
- [x] Multi-layer navmesh (BAI layer 0 + brush layer 1)
- [x] ReaderWriterLockSlim (concurrent height queries, no AI freeze)
- [x] NavMesh cache system (savenavmesh/importnavmesh/auto-load on boot)
- [x] Export/import pipeline (OBJ geometry + binary navmesh)
- [x] VertsUVs model fix (format 3.7+ producing triangles)
- [x] Root transform fix (multi-node model positioning)
- [x] BAI AABB filter (single-layer fallback)
- [x] borderSize increase (tile connectivity)
- [x] Per-tile try-catch (one tile failure doesn't kill cell)
- [x] Removed GetHeight from StopMovement/LookTowards (prevent wrong-layer snap)
- [x] ReturnStateBehavior.CorrectIdlePositionZ queries navmesh instead of blind Z set
- [x] QueueAllLoadedCells on NavMeshManager init (PreLoadTerrain catch-up)
- [x] StreamWriter export (no OOM on full world export)

## Next: Performance
- [ ] Parallelize cell loading (PreLoadTerrain currently sequential ~1 cell/sec)
- [ ] Parallelize navmesh build (currently single-threaded ProcessBuildQueue)
- [ ] Incremental cache: save/load per-cell .bin tiles instead of monolithic world .bin

## Future: Off-Mesh Links
- [ ] `/navlink` command — create bidirectional link between current position and target
- [ ] JSON persistence (Data/NavMesh/offmesh_links.json)
- [ ] Inject into DtNavMeshCreateParams.offMeshConVerts during build
- [ ] Links serialized in .bin — survives save/load cycle
- [ ] FindPath automatically uses links for cross-layer transitions (stairs, elevators)

## Future: Geometry Overlay
- [ ] `/navblock <radius>` — mark area as non-walkable (invisible wall)
- [ ] `/navwalk <radius> <height>` — add custom walkable surface
- [ ] Overlay geometry injected during build alongside BAI + brush
- [ ] JSON persistence per world

## Future: Visual Editor
- [ ] Blender plugin or standalone tool to edit navmesh visually
- [ ] Load .bin + OBJ, paint walkable/blocked areas
- [ ] Export modified .bin for server import
