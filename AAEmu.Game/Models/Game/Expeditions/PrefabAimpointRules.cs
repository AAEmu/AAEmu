using System.Globalization;
using System.Numerics;
using System.Xml.Linq;

namespace AAEmu.Game.Models.Game.Expeditions;

/// <summary>
/// Reads the designer <c>aimpoint</c> Comment from a Cry prefab library. Guild worldgate
/// devices place their walkable / FX point there (local to the doodad origin).
/// </summary>
public static class PrefabAimpointRules
{
    public const string AimpointObjectName = "aimpoint";

    /// <summary>
    /// <c>prefab://prefabs/lib.xml/name</c> → relative client path <c>game/prefabs/lib.xml</c> + prefab name.
    /// </summary>
    public static bool TrySplitPrefabUri(string uri, out string libraryClientPath, out string prefabName)
    {
        libraryClientPath = null;
        prefabName = null;
        if (string.IsNullOrWhiteSpace(uri))
            return false;

        const string scheme = "prefab://";
        if (!uri.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
            return false;

        var rest = uri[scheme.Length..].Replace('\\', '/');
        var xmlEnd = rest.IndexOf(".xml/", StringComparison.OrdinalIgnoreCase);
        if (xmlEnd < 0)
            return false;

        var library = rest[..(xmlEnd + 4)].TrimStart('/');
        prefabName = rest[(xmlEnd + 5)..];
        if (string.IsNullOrEmpty(prefabName))
            return false;

        libraryClientPath = library.StartsWith("game/", StringComparison.OrdinalIgnoreCase)
            ? library
            : "game/" + library;
        return true;
    }

    public static bool TryGetLocalOffset(string prefabLibraryXml, string prefabName, out Vector3 offset)
    {
        offset = Vector3.Zero;
        if (string.IsNullOrWhiteSpace(prefabLibraryXml) || string.IsNullOrEmpty(prefabName))
            return false;

        try
        {
            var doc = XDocument.Parse(prefabLibraryXml);
            foreach (var prefab in doc.Descendants("Prefab"))
            {
                if (!string.Equals((string)prefab.Attribute("Name"), prefabName, StringComparison.OrdinalIgnoreCase))
                    continue;

                foreach (var obj in prefab.Descendants("Object"))
                {
                    if (!string.Equals((string)obj.Attribute("Name"), AimpointObjectName, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!TryParsePos((string)obj.Attribute("Pos"), out offset))
                        return false;
                    return float.IsFinite(offset.X) && float.IsFinite(offset.Y) && float.IsFinite(offset.Z);
                }

                return false;
            }
        }
        catch (Exception)
        {
            return false;
        }

        return false;
    }

    public static bool TryParsePos(string value, out Vector3 offset)
    {
        offset = Vector3.Zero;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var parts = value.Split(',');
        if (parts.Length < 3)
            return false;

        if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
            !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) ||
            !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
            return false;

        offset = new Vector3(x, y, z);
        return true;
    }
}

