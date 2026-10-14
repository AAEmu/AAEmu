using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

using AAEmu.Game.Core.Managers.World;

namespace AAEmu.Game.Models.Game.World;

/// <summary>
/// A zone's designed arrival point lives in <c>level_design/zone/{zoneKey}/world_server/spawn_point.g</c>.
/// </summary>
/// <remarks>
/// <para>
/// The file is authored in the level editor: one or more <c>object</c> blocks, each an area sphere with a
/// name, a zone-local <c>pos</c>, a <c>zRot</c> in radians and a <c>radius</c> in metres. An ordinary
/// instance carries the single point players arrive at; a battleground carries one per side and a zone with
/// race-provisioned starts carries one per race, which is why the names differ (<c>spawn_</c>,
/// <c>Spawn_elf</c>, <c>Spawn_Nuian</c>) and are deliberately not matched on.
/// </para>
/// <para>
/// The whole file is read rather than one named object: callers that need a specific start (a team's spawn,
/// a race's spawn) are the ones that know which they want, and the single-spawn case is the common one.
/// </para>
/// </remarks>
public static partial class ZoneSpawnPointFileRules
{
    /// <summary>One authored arrival sphere. Coordinates are zone-local, <see cref="Radius"/> is metres.</summary>
    public readonly record struct ZoneSpawn(string Name, float X, float Y, float Z, float ZRotRadians, float Radius);

    /// <summary>The editor stores <c>zRot</c> in radians; the game's spawn tables carry yaw in degrees.</summary>
    public static float YawDegreesFromZRot(float zRotRadians) => zRotRadians * (180f / MathF.PI);

    /// <summary>
    /// Zone-local coordinates (the file's own space) into the continent space the World streams a unit in.
    /// </summary>
    /// <remarks>
    /// The file's <c>pos</c> is inside the zone's cells, and a zone's geometry sits at
    /// <c>originCell × <see cref="WorldManager.CELL_SIZE"/></c> on the continent. Applying the raw point
    /// put the player at a cell the zone does not occupy — empty level, under the map — for every zone
    /// whose origin is not (0,0) (instance_eternity is (1,1), carcass (2,3), golden_plains (8,4)).
    /// </remarks>
    public static Vector3 ToWorldCoordinates(
        float originCellX, float originCellY, float localX, float localY, float localZ) =>
        new(originCellX * WorldManager.CELL_SIZE + localX,
            originCellY * WorldManager.CELL_SIZE + localY,
            localZ);

    /// <summary>Every <c>object</c> block that names a position, in file order.</summary>
    public static List<ZoneSpawn> Parse(string text)
    {
        var list = new List<ZoneSpawn>();
        if (string.IsNullOrWhiteSpace(text))
            return list;

        foreach (var block in ObjectSplit().Split(text))
        {
            if (string.IsNullOrWhiteSpace(block))
                continue;

            var posMatch = PosRegex().Match(block);
            if (!posMatch.Success)
                continue;
            if (!TryF(posMatch.Groups[1].Value, out var x) ||
                !TryF(posMatch.Groups[2].Value, out var y) ||
                !TryF(posMatch.Groups[3].Value, out var z))
                continue;

            var zRot = 0f;
            var rotMatch = ZRotRegex().Match(block);
            if (rotMatch.Success)
                TryF(rotMatch.Groups[1].Value, out zRot);

            var radius = 0f;
            var radiusMatch = RadiusRegex().Match(block);
            if (radiusMatch.Success)
                TryF(radiusMatch.Groups[1].Value, out radius);

            var name = string.Empty;
            var nameMatch = NameRegex().Match(block);
            if (nameMatch.Success)
                name = nameMatch.Groups[1].Value.Trim();

            list.Add(new ZoneSpawn(name, x, y, z, zRot, radius));
        }

        return list;
    }

    /// <summary>The first authored arrival point in the file, which is the single one a dungeon carries.</summary>
    public static bool TryParseFirst(string text, out ZoneSpawn spawn)
    {
        var all = Parse(text);
        if (all.Count == 0)
        {
            spawn = default;
            return false;
        }

        spawn = all[0];
        return true;
    }

    private static bool TryF(string s, out float v) =>
        float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    [GeneratedRegex(@"^\s*object\s*$", RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex ObjectSplit();

    [GeneratedRegex("name\\s+\"?([^\"\\r\\n]+)\"?", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex NameRegex();

    [GeneratedRegex(@"pos\s*\(\s*x\s+([^,\s]+)\s*,\s*y\s+([^,\s]+)\s*,\s*z\s+([^)\s]+)\s*\)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex PosRegex();

    [GeneratedRegex(@"zRot\s+([^\s]+)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ZRotRegex();

    [GeneratedRegex(@"radius\s+([^\s]+)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex RadiusRegex();
}
