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

    // New contract: gameplay flow is heuristic-only, fixed speed.
    private const float HeuristicRiverFlowSpeed = 1f;

    /// <summary>Multiply <see cref="WaterBodyArea.FlowSpeedSigned"/> after terrain/geometry (+1 or −1). Set −1 if drift is opposite to client polyline/PCA.</summary>
    private const float HeuristicFlowSignHack = -1f;

    // River-like thresholds (meters)
    private const float RiverLikeMinLengthMeters = 120f;
    private const float RiverLikeMinAspectRatio = 2.5f;
    private const float RiverLikeMaxHalfWidthMeters = 130f;

    internal static bool TryGetRiverLikePolygonMetrics(List<Vector3> points, out float lengthMeters,
        out float maxHalfWidthMeters, out float meanFullWidthMeters, out float areaSqm, out float aspect,
        out Vector2 principalAxisUnit)
    {
        lengthMeters = 0f;
        maxHalfWidthMeters = 0f;
        meanFullWidthMeters = 0f;
        areaSqm = 0f;
        aspect = 0f;
        principalAxisUnit = Vector2.UnitX;
        if (points is null || points.Count < 3)
            return false;

        // Ensure closed ring for metrics that need edges.
        var n = points.Count;
        if (n >= 2 && points[0] != points[^1])
        {
            points = new List<Vector3>(points);
            points.Add(points[0]);
            n = points.Count;
        }

        // Many callers already pass closed rings (last == first). Avoid double-counting the closing vertex in PCA metrics.
        var pcaCount = n >= 2 && points[0] == points[^1] ? n - 1 : n;

        // Area (shoelace) in XY.
        double sum = 0d;
        for (var i = 0; i + 1 < n; i++)
            sum += (double)points[i].X * points[i + 1].Y - (double)points[i + 1].X * points[i].Y;
        areaSqm = (float)Math.Abs(sum * 0.5d);
        if (areaSqm <= 1f)
            return false;

        // PCA axis in XY from covariance.
        var mean = GetMeanXY(points, pcaCount);
        GetCovarianceXY(points, pcaCount, mean, out var cxx, out var cxy, out var cyy);
        if (!float.IsFinite(cxx) || !float.IsFinite(cxy) || !float.IsFinite(cyy))
            return false;

        // principal eigenvector for 2x2 covariance
        var trace = cxx + cyy;
        var det = cxx * cyy - cxy * cxy;
        var disc = trace * trace - 4f * det;
        if (disc < 0f)
            disc = 0f;
        var s = MathF.Sqrt(disc);
        var lambda1 = 0.5f * (trace + s);
        var vx = cxy;
        var vy = lambda1 - cxx;
        if (MathF.Abs(vx) + MathF.Abs(vy) < 1e-12f)
        {
            vx = 1f;
            vy = 0f;
        }
        var v = Vector2.Normalize(new Vector2(vx, vy));
        principalAxisUnit = v;

        // Project points onto axis and its perpendicular to estimate length/width.
        var perp = new Vector2(-v.Y, v.X);
        var minT = float.PositiveInfinity;
        var maxT = float.NegativeInfinity;
        var minP = float.PositiveInfinity;
        var maxP = float.NegativeInfinity;
        for (var i = 0; i < points.Count; i++)
        {
            var d = new Vector2(points[i].X - mean.X, points[i].Y - mean.Y);
            var t = Vector2.Dot(d, v);
            var p = Vector2.Dot(d, perp);
            if (t < minT) minT = t;
            if (t > maxT) maxT = t;
            if (p < minP) minP = p;
            if (p > maxP) maxP = p;
        }

        lengthMeters = Math.Max(0f, maxT - minT);
        var fullWidth = Math.Max(0f, maxP - minP);
        maxHalfWidthMeters = fullWidth * 0.5f;
        meanFullWidthMeters = areaSqm / Math.Max(1e-3f, lengthMeters);
        aspect = lengthMeters / Math.Max(1e-3f, meanFullWidthMeters);
        return true;
    }

    private static bool IsRiverLikePolygon(List<Vector3> points, out Vector2 axisUnit)
    {
        axisUnit = Vector2.UnitX;
        if (!TryGetRiverLikePolygonMetrics(points, out var length, out var maxHalfWidth, out var meanFullWidth, out _,
                out var aspect, out var axis))
            return false;
        if (length < RiverLikeMinLengthMeters)
            return false;
        if (maxHalfWidth > RiverLikeMaxHalfWidthMeters)
            return false;
        if (aspect < RiverLikeMinAspectRatio)
            return false;

        axisUnit = axis;
        return true;
    }

    private static Vector2 GetMeanXY(List<Vector3> points)
    {
        var n = points.Count;
        if (n >= 2 && points[0] == points[^1])
            n -= 1;
        return GetMeanXY(points, n);
    }

    private static Vector2 GetMeanXY(List<Vector3> points, int n)
    {
        double sx = 0d;
        double sy = 0d;
        for (var i = 0; i < n; i++)
        {
            sx += points[i].X;
            sy += points[i].Y;
        }

        var inv = 1f / n;
        return new Vector2((float)(sx * inv), (float)(sy * inv));
    }

    private static void GetCovarianceXY(List<Vector3> points, Vector2 mean, out float cxx, out float cxy, out float cyy)
    {
        var n = points.Count;
        if (n >= 2 && points[0] == points[^1])
            n -= 1;
        GetCovarianceXY(points, n, mean, out cxx, out cxy, out cyy);
    }

    private static void GetCovarianceXY(List<Vector3> points, int n, Vector2 mean, out float cxx, out float cxy, out float cyy)
    {
        double sxx = 0d;
        double sxy = 0d;
        double syy = 0d;
        for (var i = 0; i < n; i++)
        {
            var dx = points[i].X - mean.X;
            var dy = points[i].Y - mean.Y;
            sxx += dx * dx;
            sxy += dx * dy;
            syy += dy * dy;
        }

        var inv = 1f / Math.Max(1, n);
        cxx = (float)(sxx * inv);
        cxy = (float)(sxy * inv);
        cyy = (float)(syy * inv);
    }

    private static bool TryGetHeightNoLoad(WorldTemplate template, Vector2 p, out float h)
    {
        h = 0f;
        if (template == null)
            return false;
        var cellX = (int)p.X / WorldManager.CELL_SIZE;
        var cellY = (int)p.Y / WorldManager.CELL_SIZE;
        var cell = template.GetCell(cellX, cellY);
        if (cell == null || cell.HeightMap == null || !cell.Loaded)
            return false;
        h = cell.GetHeight((int)p.X, (int)p.Y);
        return true;
    }

    private static float GetSignedSpeedFromTerrainNoLoad(WorldTemplate template, Vector2 a, Vector2 b, float speedAbs)
    {
        if (speedAbs <= 0f)
            return 0f;
        if (!TryGetHeightNoLoad(template, a, out var ha) || !TryGetHeightNoLoad(template, b, out var hb))
            return speedAbs; // unknown; keep geometric sign
        return hb > ha ? -speedAbs : speedAbs;
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

    /// <summary>
    /// Returns a stable snapshot of <see cref="Areas"/> for debug/commands without exposing the internal lock.
    /// </summary>
    public List<WaterBodyArea> GetAreasSnapshot()
    {
        lock (_lock)
            return [.. Areas];
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

        // Surface polygon (always present for contour volumes)
        if (water.PhysicsContourPointsList.Count >= 3 && (likeArea || likeRiver))
            AddPolygonFromPhysicsContour(worldCell.Template, water, cellOffset,
                likeRiver
                    ? $"WaterContour_C{worldCell.CellX}-{worldCell.CellY}_{prefabIdx}"
                    : $"Water_C{worldCell.CellX}-{worldCell.CellY}_{prefabIdx}");

        // River corridor (only for explicit river volumes)
        if (!likeRiver)
            return;

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

        if (riverWorldPoints is not { Count: >= 2 })
            return;

        var newRiver = new WaterBodyArea($"Segment_C{worldCell.CellX}-{worldCell.CellY}_{prefabIdx}",
            WaterBodyAreaType.LineArray);
        newRiver.Depth = water.Depth;
        newRiver.RiverWidth = Math.Clamp(water.Depth * 2f, 4f, 40f);
        foreach (var centerPoint in riverWorldPoints)
        {
            if (!newRiver.Points.Contains(centerPoint))
                newRiver.Points.Add(centerPoint);
        }

        var a = new Vector2(newRiver.Points[0].X, newRiver.Points[0].Y);
        var b = new Vector2(newRiver.Points[^1].X, newRiver.Points[^1].Y);
        var dir = b - a;
        newRiver.FlowAxis = dir.LengthSquared() > 1e-12f ? Vector2.Normalize(dir) : Vector2.UnitX;
        newRiver.FlowSpeedAbs = HeuristicRiverFlowSpeed;
        newRiver.FlowSpeedSigned = GetSignedSpeedFromTerrainNoLoad(worldCell.Template, a, b, HeuristicRiverFlowSpeed) * HeuristicFlowSignHack;
        newRiver.Speed = newRiver.FlowSpeedSigned;
        newRiver.UpdateBounds();
        if (IsWaterFootprintTooSmall(newRiver))
            return;
        lock (_lock)
        {
            newRiver.Id = (uint)Areas.Count;
            Areas.Add(newRiver);
            SpatialIndexAddUnderLock(newRiver);
            _indexedAreaCount = Areas.Count;
        }
    }

    private void AddPolygonFromPhysicsContour(WorldTemplate template, ObjectDataType11Water water, Vector3 cellOffset,
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

        newLake.FlowSpeedAbs = 0f;
        newLake.FlowSpeedSigned = 0f;
        newLake.Speed = 0f;
        if (IsRiverLikePolygon(newLake.Points, out var axis))
        {
            newLake.FlowAxis = axis;
            newLake.FlowSpeedAbs = HeuristicRiverFlowSpeed;

            var mean = GetMeanXY(newLake.Points);
            var halfLen = Math.Max(10f, newLake.BoundingBox.Width + newLake.BoundingBox.Height) * 0.25f;
            var p0 = mean - axis * halfLen;
            var p1 = mean + axis * halfLen;
            newLake.FlowSpeedSigned = GetSignedSpeedFromTerrainNoLoad(template, p0, p1, HeuristicRiverFlowSpeed) * HeuristicFlowSignHack;
            newLake.Speed = newLake.FlowSpeedSigned;
        }

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
