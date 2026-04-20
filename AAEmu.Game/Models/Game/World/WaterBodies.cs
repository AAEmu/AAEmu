using System.Collections.Generic;
using System.Numerics;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.CryEngine.Objects;
using Newtonsoft.Json;

namespace AAEmu.Game.Models.Game.World;

public class WaterBodies
{
    [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public float OceanLevel { get; set; }

    [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public List<WaterBodyArea> Areas { get; set; } = [];

    [JsonIgnore] internal readonly object _lock = new();

    /// <summary>XY grid for <see cref="Areas"/> so river/lake queries above <see cref="OceanLevel"/> do not scan thousands of segments.</summary>
    private const float SpatialCellSize = 256f;

    /// <summary>Not readonly: hot reload can add this field to an existing instance (initializer does not run → null without lazy init).</summary>
    [JsonIgnore]
    private Dictionary<(int cx, int cy), List<uint>> _areaIndexByCell;

    /// <summary>When not equal to <see cref="Areas"/> count, spatial index is rebuilt on next query (ingest updates incrementally; tests/JSON may desync).</summary>
    [JsonIgnore]
    private int _indexedAreaCount;

    // Max height (m) above world.xml sea: Cry Ocean rows with SurfaceHeight in this band are skipped (same open sea as IsWater for Z<=OceanLevel).
    private const float TemplateSeaDuplicateSurfaceMarginMeters = 1f;

    /// <summary>Skip water zones whose XY bbox area is below this (m²).</summary>
    public const float MinWaterBboxAreaSquareMeters = 5000f;

    // Heuristic: some client maps encode lake-like zones with non-zero Speed (often as River/Sector/Ocean).
    // Keep the water slab but zero the flow so ships do not drift in lakes.
    private const float LakeLikeRiverMinContourAreaSquareMeters = 75000f;
    private const float LakeLikeRiverMinHalfWidthMeters = 60f;

    private static float GetContourAreaSqm(IReadOnlyList<Vector3> points)
    {
        if (points is null || points.Count < 3)
            return 0f;

        double sum = 0d;
        for (var i = 0; i < points.Count; i++)
        {
            var j = (i + 1) % points.Count;
            sum += (double)points[i].X * points[j].Y - (double)points[j].X * points[i].Y;
        }

        return (float)Math.Abs(sum * 0.5d);
    }

    private static bool IsLakeLikeFlowZone(ObjectDataType11Water water, Vector3 cellOffset, float riverHalfWidthMeters)
    {
        if (water?.PhysicsContourPointsList is not { Count: >= 3 })
            return false;

        // Fast path: extremely wide "rivers" are almost always lakes.
        if (riverHalfWidthMeters >= LakeLikeRiverMinHalfWidthMeters)
            return true;

        // Area test in world XY using the contour list.
        List<Vector3> world = [];
        foreach (var v in water.PhysicsContourPointsList)
            world.Add(WaterPointToWorld(cellOffset, v, water.SurfaceHeight));

        return GetContourAreaSqm(world) >= LakeLikeRiverMinContourAreaSquareMeters;
    }

    private static bool IsWaterFootprintTooSmall(WaterBodyArea area)
    {
        var bboxArea = area.BoundingBox.Width * area.BoundingBox.Height;
        return bboxArea < MinWaterBboxAreaSquareMeters;
    }

    private void EnsureSpatialIndexUnderLock()
    {
        _areaIndexByCell ??= new();
        if (_indexedAreaCount == Areas.Count)
            return;
        _areaIndexByCell.Clear();
        foreach (var area in Areas)
            SpatialIndexAddUnderLock(area);
        _indexedAreaCount = Areas.Count;
    }

    /// <summary>Caller must hold <see cref="_lock"/>. Registers <paramref name="area"/> in every cell overlapped by its XY bbox.</summary>
    private void SpatialIndexAddUnderLock(WaterBodyArea area)
    {
        _areaIndexByCell ??= new();
        var id = area.Id;
        var bb = area.BoundingBox;
        var minCx = (int)MathF.Floor(bb.Left / SpatialCellSize);
        var maxCx = (int)MathF.Floor((bb.Left + bb.Width) / SpatialCellSize);
        var minCy = (int)MathF.Floor(bb.Top / SpatialCellSize);
        var maxCy = (int)MathF.Floor((bb.Top + bb.Height) / SpatialCellSize);

        for (var cx = minCx; cx <= maxCx; cx++)
        {
            for (var cy = minCy; cy <= maxCy; cy++)
            {
                var key = (cx, cy);
                if (!_areaIndexByCell.TryGetValue(key, out var list))
                {
                    list = [];
                    _areaIndexByCell[key] = list;
                }

                list.Add(id);
            }
        }
    }

    /// <summary>Clears ingested areas and the spatial index (e.g. <see cref="WorldInstance.ReloadWaterFromLoadedCells"/>).</summary>
    internal void ClearIngestedAreas()
    {
        lock (_lock)
        {
            Areas.Clear();
            _areaIndexByCell?.Clear();
            _indexedAreaCount = 0;
        }
    }

    /// <summary>For tests or manual <see cref="Areas"/> edits outside <see cref="AddFromCellData"/>.</summary>
    internal void RebuildSpatialIndex()
    {
        lock (_lock)
        {
            _areaIndexByCell?.Clear();
            _indexedAreaCount = 0;
            EnsureSpatialIndexUnderLock();
        }
    }

    public bool IsWater(Vector3 point, out Vector3 flowDirection)
    {
        flowDirection = Vector3.Zero;

        if (point.Z <= OceanLevel)
            return true;

        lock (_lock)
        {
            EnsureSpatialIndexUnderLock();

            var totalFlow = Vector3.Zero;
            var targets = 0;
            var px = point.X;
            var py = point.Y;
            var cx = (int)MathF.Floor(px / SpatialCellSize);
            var cy = (int)MathF.Floor(py / SpatialCellSize);

            if (_areaIndexByCell == null || !_areaIndexByCell.TryGetValue((cx, cy), out var inCell))
            {
                flowDirection = Vector3.Zero;
                return false;
            }

            foreach (var areaId in inCell)
            {
                var area = Areas[(int)areaId];
                if (!area.BoundingBox.Contains(px, py))
                    continue;

                if (area.GetSurface(point, out var surfacePoint, out var fd) &&
                    point.Z <= surfacePoint.Z &&
                    point.Z >= surfacePoint.Z - area.Depth)
                {
                    totalFlow += fd;
                    targets++;
                }
            }

            if (targets > 0)
            {
                flowDirection = totalFlow / targets;
                return true;
            }
        }

        flowDirection = Vector3.Zero;
        return false;
    }

    public float GetWaterSurface(Vector3 point, out Vector3 flowDirection)
    {
        flowDirection = Vector3.Zero;

        if (point.Z <= OceanLevel)
            return OceanLevel;

        lock (_lock)
        {
            EnsureSpatialIndexUnderLock();

            var closestSurfaceDist = float.PositiveInfinity;
            var chosenZ = OceanLevel;
            var px = point.X;
            var py = point.Y;
            var cx = (int)MathF.Floor(px / SpatialCellSize);
            var cy = (int)MathF.Floor(py / SpatialCellSize);

            if (_areaIndexByCell != null && _areaIndexByCell.TryGetValue((cx, cy), out var inCell))
            {
                foreach (var areaId in inCell)
                {
                    var area = Areas[(int)areaId];
                    if (!area.BoundingBox.Contains(px, py))
                        continue;

                    if (!area.GetSurface(point, out var surfacePoint, out var f))
                        continue;
                    if (point.Z < surfacePoint.Z - area.Depth)
                        continue;

                    var surfaceDistance = MathF.Abs(surfacePoint.Z - point.Z);
                    if (surfaceDistance < closestSurfaceDist)
                    {
                        closestSurfaceDist = surfaceDistance;
                        chosenZ = surfacePoint.Z;
                        flowDirection = f;
                    }
                }
            }

            if (closestSurfaceDist < float.PositiveInfinity)
                return chosenZ;
        }

        return OceanLevel;
    }

    private static Vector3 WaterPointToWorld(Vector3 cellOffset, Vector3 filePoint, float surfaceZ)
    {
        const float localBand = WorldManager.CELL_SIZE * 2f;
        var xyCellLocal = filePoint.X <= localBand && filePoint.Y <= localBand &&
                          filePoint.X >= -512f && filePoint.Y >= -512f;
        var xy = xyCellLocal ? cellOffset + filePoint : filePoint;
        return xy with { Z = surfaceZ };
    }

    public void AddFromCellData(WorldCell worldCell)
    {
        if (worldCell == null)
            return;
        var cellOffset = worldCell.GetCellWorldOffset();

        var prefabIdx = 0;
        if (worldCell.LoadedObjectDat != null)
        {
            foreach (var prefab in worldCell.LoadedObjectDat.PrefabsList)
            {
                prefabIdx++;
                AddObjectDataFromWorldCell(prefab, cellOffset, worldCell, prefabIdx);
            }
        }

        // Gameplay water comes from object.dat only.
    }

    private void AddObjectDataFromWorldCell(ObjectDataBase prefab, Vector3 cellOffset, WorldCell worldCell, int prefabIdx)
    {
        if (prefab is ObjectDataType1Brush)
            return;
        if (prefab is ObjectDataType6Voxel)
            return;

        if (prefab is not ObjectDataType11Water water)
            return;

        if (water.VolumeType == WaterObjectVolumeType.Ocean &&
            water.SurfaceHeight <= worldCell.Template.OceanLevel + TemplateSeaDuplicateSurfaceMarginMeters)
            return;

        var likeRiver =
            water.VolumeType == WaterObjectVolumeType.River;
        var likeArea =
            water.VolumeType == WaterObjectVolumeType.Area ||
            water.VolumeType == WaterObjectVolumeType.Ocean ||
            water.VolumeType == WaterObjectVolumeType.Sector;

        if (likeRiver)
        {
            List<Vector3> riverWorldPoints = null;
            if (water.ShapePointsList.Count >= 2)
            {
                riverWorldPoints = [];
                foreach (var sp in water.ShapePointsList)
                    riverWorldPoints.Add(WaterPointToWorld(cellOffset, sp, water.SurfaceHeight));
            }
            else if (Vector3.Distance(water.StartPos, water.EndPos) > 0.5f)
            {
                riverWorldPoints =
                [
                    WaterPointToWorld(cellOffset, water.StartPos, water.SurfaceHeight),
                    WaterPointToWorld(cellOffset, water.EndPos, water.SurfaceHeight)
                ];
            }

            if (riverWorldPoints is { Count: >= 2 })
            {
                var newRiver = new WaterBodyArea($"Segment_C{worldCell.CellX}-{worldCell.CellY}_{prefabIdx}",
                    WaterBodyAreaType.LineArray);
                newRiver.Depth = water.Depth;
                var maxWidth = Math.Max(4f, water.Depth * 2f);
                foreach (var centerPoint in riverWorldPoints)
                {
                    if (!newRiver.Points.Contains(centerPoint))
                        newRiver.Points.Add(centerPoint);
                }

                for (var i = 0; i + 1 < riverWorldPoints.Count; i++)
                {
                    var segLen = Vector3.Distance(riverWorldPoints[i], riverWorldPoints[i + 1]);
                    maxWidth = Math.Max(maxWidth, segLen * 0.35f);
                }

                newRiver.RiverWidth = maxWidth;
                newRiver.Speed = IsLakeLikeFlowZone(water, cellOffset, maxWidth) ? 0f : water.Speed;
                newRiver.UpdateBounds();
                if (!IsWaterFootprintTooSmall(newRiver))
                {
                    lock (_lock)
                    {
                        newRiver.Id = (uint)Areas.Count;
                        Areas.Add(newRiver);
                        SpatialIndexAddUnderLock(newRiver);
                        _indexedAreaCount = Areas.Count;
                    }
                }
            }
        }

        if (likeRiver && water.PhysicsContourPointsList.Count >= 3)
        {
            AddPolygonFromPhysicsContour(water, cellOffset,
                $"WaterContour_C{worldCell.CellX}-{worldCell.CellY}_{prefabIdx}");
        }
        else if (water.PhysicsContourPointsList.Count >= 2 && likeArea)
        {
            AddPolygonFromPhysicsContour(water, cellOffset,
                $"Water_C{worldCell.CellX}-{worldCell.CellY}_{prefabIdx}");
        }
    }

    private void AddPolygonFromPhysicsContour(ObjectDataType11Water water, Vector3 cellOffset,
        string name)
    {
        var newLake = new WaterBodyArea(name, WaterBodyAreaType.Polygon);
        newLake.Depth = water.Depth;
        foreach (var v3 in water.PhysicsContourPointsList)
        {
            var p = WaterPointToWorld(cellOffset, v3, water.SurfaceHeight);
            if (!newLake.Points.Contains(p))
                newLake.Points.Add(p);
        }

        if (newLake.Points.Count == 0)
            return;

        newLake.Points.Add(newLake.Points[0]);
        newLake.UpdateBounds();
        newLake.Speed = water.Speed;
        if (IsWaterFootprintTooSmall(newLake))
            return;
        lock (_lock)
        {
            newLake.Id = (uint)Areas.Count;
            Areas.Add(newLake);
            SpatialIndexAddUnderLock(newLake);
            _indexedAreaCount = Areas.Count;
        }
    }
}
