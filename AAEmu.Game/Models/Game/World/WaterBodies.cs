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

    [JsonIgnore] public object _lock = new();

    // Max height (m) above world.xml sea: Cry Ocean rows with SurfaceHeight in this band are skipped (same open sea as IsWater for Z<=OceanLevel).
    private const float TemplateSeaDuplicateSurfaceMarginMeters = 1f;

    public bool IsWater(Vector3 point, out Vector3 flowDirection)
    {
        flowDirection = Vector3.Zero;

        if (point.Z <= OceanLevel)
            return true;

        lock (_lock)
        {
            var totalFlow = Vector3.Zero;
            var targets = 0;

            foreach (var area in Areas)
            {
                if (area.GetSurface(point, out var surfacePoint, out flowDirection) &&
                    point.Z <= surfacePoint.Z &&
                    point.Z >= surfacePoint.Z - area.Depth)
                {
                    totalFlow += flowDirection;
                    targets++;
                }
            }

            if (targets > 0)
            {
                flowDirection = totalFlow;
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
            var closestSurfaceDist = float.PositiveInfinity;
            var chosenZ = OceanLevel;
            foreach (var area in Areas)
            {
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

        if (worldCell.LoadedVisAreasDat != null)
        {
            prefabIdx = 1_000_000;
            foreach (var prefab in worldCell.LoadedVisAreasDat.PrefabsList)
            {
                prefabIdx++;
                AddObjectDataFromWorldCell(prefab, cellOffset, worldCell, prefabIdx);
            }
        }
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
            water.VolumeType == WaterObjectVolumeType.River ||
            water.VolumeType == WaterObjectVolumeType.Ocean ||
            water.VolumeType == WaterObjectVolumeType.Sector;
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
                newRiver.Speed = water.Speed;
                newRiver.UpdateBounds();
                lock (_lock)
                {
                    newRiver.Id = (uint)Areas.Count;
                    Areas.Add(newRiver);
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
        lock (_lock)
        {
            newLake.Id = (uint)Areas.Count;
            Areas.Add(newLake);
        }
    }
}
