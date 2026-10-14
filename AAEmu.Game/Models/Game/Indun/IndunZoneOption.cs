using System.Text.Json;

namespace AAEmu.Game.Models.Game.Indun;

/// <summary>
/// The parsed <c>indun_zones.option</c> JSON column: the instance copy's phase budget and, when
/// non-zero, the <c>tower_defs.id</c> that scripts it. Every time is in seconds.
/// </summary>
/// <remarks>
/// The column ships as <c>{"tower_def":0,"ready_time":0,"play_time":0,"end_time":0}</c>. A copy with a
/// tower-def runs a scripted instance (start tower_def, gate/wave ladder). <c>ready_time</c> is the
/// "Wait time" countdown before the copy starts, <c>play_time</c> the main play phase (zone group 130's
/// "Until Dawn"), and <c>end_time</c> the wrap-up window. Keys go missing or carry extra siblings
/// (faction, min_corps_size, matching), so the parse reads each key on its own and defaults the rest.
/// </remarks>
public readonly record struct IndunZoneOption(uint TowerDefId, int ReadySeconds, int PlaySeconds, int EndSeconds)
{
    /// <summary>A copy with no tower-def and no phase budget — a plain instance.</summary>
    public static readonly IndunZoneOption None = new(0, 0, 0, 0);

    /// <summary>True when the copy carries a tower-def and/or a non-zero phase budget.</summary>
    public bool IsScripted => TowerDefId != 0 || ReadySeconds > 0 || PlaySeconds > 0 || EndSeconds > 0;

    /// <summary>Reads the option blob; a blank or malformed value yields <see cref="None"/>.</summary>
    public static IndunZoneOption Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return None;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return None;

            return new IndunZoneOption(
                ReadUInt(root, "tower_def"),
                ReadInt(root, "ready_time"),
                ReadInt(root, "play_time"),
                ReadInt(root, "end_time"));
        }
        catch (JsonException)
        {
            return None;
        }
    }

    private static bool TryGet(JsonElement root, string key, out JsonElement value)
    {
        if (root.TryGetProperty(key, out value) &&
            (value.ValueKind == JsonValueKind.Number || value.ValueKind == JsonValueKind.String))
            return true;

        value = default;
        return false;
    }

    private static int ReadInt(JsonElement root, string key)
    {
        if (!TryGet(root, key, out var value))
            return 0;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            return number;

        return int.TryParse(value.ToString(), out var parsed) ? parsed : 0;
    }

    private static uint ReadUInt(JsonElement root, string key)
    {
        var value = ReadInt(root, key);
        return value > 0 ? (uint)value : 0u;
    }
}
