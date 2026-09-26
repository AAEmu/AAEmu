using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Utils.DB;

namespace AAEmu.Game.Models.Game.Skills;

public sealed record SkillContentLink(
    uint SkillId,
    int EquipSlotId,
    string EquipSlotName,
    string EquipSlotCategory);

public sealed record SkillContentFieldAuditReport(
    int TotalSkills,
    int ValidHeightEdgeToEdgeCount,
    int AutoFireCount,
    int SensitiveOperationCount,
    int LinkedSkillCount,
    int NoLinkSkillCount,
    IReadOnlyList<SkillContentLink> Links,
    IReadOnlyList<uint> UnknownLinkSkillIds);

public static class SkillContentFieldAudit
{
    public static SkillContentFieldAuditReport Build(
        IEnumerable<SkillTemplate> skills,
        SkillEquipSlotCatalog equipSlots)
    {
        ArgumentNullException.ThrowIfNull(skills);
        ArgumentNullException.ThrowIfNull(equipSlots);

        var ordered = skills.OrderBy(skill => skill.Id).ToArray();
        var links = new List<SkillContentLink>();
        var unknown = new List<uint>();

        foreach (var skill in ordered)
        {
            if (skill.LinkEquipSlotId == equipSlots.NoLinkSlotId)
                continue;

            if (!equipSlots.TryGet(skill.LinkEquipSlotId, out var slot))
            {
                unknown.Add(skill.Id);
                continue;
            }

            links.Add(new SkillContentLink(
                skill.Id,
                slot.Id,
                slot.Name,
                slot.Category));
        }

        return new SkillContentFieldAuditReport(
            ordered.Length,
            ordered.Count(skill => skill.ValidHeightEdgeToEdge),
            ordered.Count(skill => skill.AutoFire),
            ordered.Count(skill => skill.SensitiveOperation),
            links.Count,
            ordered.Length - links.Count - unknown.Count,
            links,
            unknown);
    }
}

/// <summary>Reads the four GF-C13 skill columns without shipped-value fallbacks.</summary>
public static class SkillContentFieldReader
{
    public static void Apply(SQLiteWrapperReader reader, SkillTemplate template)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(template);

        template.ValidHeightEdgeToEdge = ReadRequiredBoolean(reader, "valid_height_edge_to_edge", template.Id);
        template.LinkEquipSlotId = ReadRequiredInt32(reader, "link_equip_slot_id", template.Id);
        template.AutoFire = ReadRequiredBoolean(reader, "auto_fire", template.Id);
        template.SensitiveOperation = ReadRequiredBoolean(reader, "sensitive_operation", template.Id);
    }

    private static bool ReadRequiredBoolean(SQLiteWrapperReader reader, string column, uint skillId)
    {
        if (reader.IsDBNull(column))
            throw new InvalidDataException($"skill {skillId} has NULL {column}");
        return reader.GetBoolean(column);
    }

    private static int ReadRequiredInt32(SQLiteWrapperReader reader, string column, uint skillId)
    {
        if (reader.IsDBNull(column))
            throw new InvalidDataException($"skill {skillId} has NULL {column}");
        return reader.GetInt32(column);
    }
}
