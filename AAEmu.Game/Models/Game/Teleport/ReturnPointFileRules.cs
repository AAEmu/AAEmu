using System.Globalization;
using System.Text.RegularExpressions;

namespace AAEmu.Game.Models.Game.Teleport;

/// <summary>
/// Level-pack return points live in <c>level_design/zone/{zoneKey}/world_server/return_point.g</c>.
/// Object names are <c>ReturnPoint_{editor_name}</c>; compact <c>return_points.id</c> is the
/// SpecialEffect Return value. JSON worldgates and the Memory Tome book (built from the same
/// <c>.g</c> + subzone containment) win when they already have that id.
/// </summary>
public static partial class ReturnPointFileRules
{
    public const string NamePrefix = "ReturnPoint_";

    public readonly record struct LocalPoint(string EditorName, uint ZoneKey, float X, float Y, float Z, float ZRotRadians);

    public static bool ShouldUseLevelFile(bool haveWorldgate, bool haveRecall) =>
        !haveWorldgate && !haveRecall;

    public static bool TryEditorNameFromObject(string objectName, out string editorName)
    {
        editorName = null;
        if (string.IsNullOrWhiteSpace(objectName))
            return false;

        var name = objectName.Trim().Trim('"');
        if (!name.StartsWith(NamePrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var rest = name[NamePrefix.Length..].Trim();
        if (rest.Length == 0)
            return false;

        editorName = rest;
        return true;
    }

    public static bool TryGetReturnPointId(IReadOnlyDictionary<string, uint> editorNameToId, string editorName, out uint id)
    {
        id = 0;
        if (editorNameToId == null || string.IsNullOrWhiteSpace(editorName))
            return false;
        return editorNameToId.TryGetValue(editorName, out id) && id != 0;
    }

    public static float YawDegreesFromZRot(float zRotRadians) =>
        zRotRadians * (180f / MathF.PI);

    public static (float X, float Y, float Z) ToWorld(float originCellX, float originCellY, float localX, float localY, float localZ) =>
        (originCellX * 1024f + localX, originCellY * 1024f + localY, localZ);

    public static List<LocalPoint> Parse(string text, uint zoneKey)
    {
        var list = new List<LocalPoint>();
        if (string.IsNullOrWhiteSpace(text) || zoneKey == 0)
            return list;

        foreach (var block in ObjectSplit().Split(text))
        {
            if (string.IsNullOrWhiteSpace(block))
                continue;

            var nameMatch = NameRegex().Match(block);
            var posMatch = PosRegex().Match(block);
            if (!nameMatch.Success || !posMatch.Success)
                continue;
            if (!TryEditorNameFromObject(nameMatch.Groups[1].Value, out var editorName))
                continue;
            if (!TryF(posMatch.Groups[1].Value, out var x) ||
                !TryF(posMatch.Groups[2].Value, out var y) ||
                !TryF(posMatch.Groups[3].Value, out var z))
                continue;

            var zRot = 0f;
            var rotMatch = ZRotRegex().Match(block);
            if (rotMatch.Success)
                TryF(rotMatch.Groups[1].Value, out zRot);

            list.Add(new LocalPoint(editorName, zoneKey, x, y, z, zRot));
        }

        return list;
    }

    private static bool TryF(string s, out float v) =>
        float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    [GeneratedRegex(@"^\s*object\s*$", RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex ObjectSplit();

    [GeneratedRegex(@"name\s+""?([^""\r\n]+)""?", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex NameRegex();

    [GeneratedRegex(@"pos\s*\(\s*x\s+([^,\s]+)\s*,\s*y\s+([^,\s]+)\s*,\s*z\s+([^)\s]+)\s*\)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex PosRegex();

    [GeneratedRegex(@"zRot\s+([^\s]+)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ZRotRegex();
}
