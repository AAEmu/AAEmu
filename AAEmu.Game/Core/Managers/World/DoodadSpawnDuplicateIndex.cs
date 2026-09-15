namespace AAEmu.Game.Core.Managers.World;

internal sealed class DoodadSpawnDuplicateIndex
{
    internal const float CoordinateTolerance = 0.01f;

    private readonly Dictionary<uint, Dictionary<Cell, List<Point>>> _cellsByUnit = [];
    private readonly Dictionary<uint, List<Point>> _extremePointsByUnit = [];

    public bool Contains(uint unitId, float x, float y, float z)
    {
        var point = new Point(x, y, z);
        if (!point.IsFinite)
            return false;

        if (!TryGetCell(point, out var cell))
            return _extremePointsByUnit.TryGetValue(unitId, out var extremePoints) &&
                   extremePoints.Any(existing => IsDuplicate(existing, point));

        if (!_cellsByUnit.TryGetValue(unitId, out var cells))
            return false;

        for (var xOffset = -1; xOffset <= 1; xOffset++)
        for (var yOffset = -1; yOffset <= 1; yOffset++)
        for (var zOffset = -1; zOffset <= 1; zOffset++)
        {
            if (!TryOffset(cell, xOffset, yOffset, zOffset, out var neighbor) ||
                !cells.TryGetValue(neighbor, out var points))
                continue;
            if (points.Any(existing => IsDuplicate(existing, point)))
                return true;
        }

        return false;
    }

    public void Add(uint unitId, float x, float y, float z)
    {
        var point = new Point(x, y, z);
        if (!point.IsFinite)
            return;
        if (!TryGetCell(point, out var cell))
        {
            if (!_extremePointsByUnit.TryGetValue(unitId, out var extremePoints))
                _extremePointsByUnit[unitId] = extremePoints = [];
            extremePoints.Add(point);
            return;
        }

        if (!_cellsByUnit.TryGetValue(unitId, out var cells))
            _cellsByUnit[unitId] = cells = [];
        if (!cells.TryGetValue(cell, out var points))
            cells[cell] = points = [];
        points.Add(point);
    }

    private static bool IsDuplicate(Point left, Point right) =>
        Math.Abs(left.X - right.X) < CoordinateTolerance &&
        Math.Abs(left.Y - right.Y) < CoordinateTolerance &&
        Math.Abs(left.Z - right.Z) < CoordinateTolerance;

    private static bool TryGetCell(Point point, out Cell cell)
    {
        var x = Math.Floor((double)point.X / CoordinateTolerance);
        var y = Math.Floor((double)point.Y / CoordinateTolerance);
        var z = Math.Floor((double)point.Z / CoordinateTolerance);
        // The double representation of long.MaxValue rounds up to 2^63, so keep boundary values in
        // the safe fallback instead of relying on an implementation-defined floating-point cast.
        if (x <= long.MinValue || x >= long.MaxValue || y <= long.MinValue || y >= long.MaxValue ||
            z <= long.MinValue || z >= long.MaxValue)
        {
            cell = default;
            return false;
        }
        cell = new Cell((long)x, (long)y, (long)z);
        return true;
    }

    private static bool TryOffset(Cell cell, int x, int y, int z, out Cell result)
    {
        if ((x < 0 && cell.X == long.MinValue) || (x > 0 && cell.X == long.MaxValue) ||
            (y < 0 && cell.Y == long.MinValue) || (y > 0 && cell.Y == long.MaxValue) ||
            (z < 0 && cell.Z == long.MinValue) || (z > 0 && cell.Z == long.MaxValue))
        {
            result = default;
            return false;
        }
        result = new Cell(cell.X + x, cell.Y + y, cell.Z + z);
        return true;
    }

    private readonly record struct Point(float X, float Y, float Z)
    {
        public bool IsFinite => float.IsFinite(X) && float.IsFinite(Y) && float.IsFinite(Z);
    }

    private readonly record struct Cell(long X, long Y, long Z);
}
