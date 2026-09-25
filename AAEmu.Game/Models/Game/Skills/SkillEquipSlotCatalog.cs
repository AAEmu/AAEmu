using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Utils.DB;
using Microsoft.Data.Sqlite;

namespace AAEmu.Game.Models.Game.Skills;

public sealed record EquipSlotDefinition(int Id, string Name, string Category)
{
    public bool IsNoLink { get; init; }
}

/// <summary>
/// Loads <c>enum_equip_slot</c> for the skill <c>link_equip_slot_id</c> field and validates
/// every skill link against the shipped catalog. The no-link sentinel is resolved from the
/// catalog row named <see cref="NoLinkSlotName"/> rather than being duplicated in code.
/// </summary>
public sealed class SkillEquipSlotCatalog
{
    public const string NoLinkSlotName = "invalid";

    private readonly Dictionary<int, EquipSlotDefinition> _byId = [];
    private readonly Dictionary<string, int> _byName = new(StringComparer.Ordinal);

    public IReadOnlyCollection<EquipSlotDefinition> Definitions => _byId.Values;

    public int NoLinkSlotId => _byName.TryGetValue(NoLinkSlotName, out var id)
        ? id
        : throw new InvalidOperationException($"enum_equip_slot is missing catalog row '{NoLinkSlotName}'");

    public void Load(SqliteConnection connection)
    {
        _byId.Clear();
        _byName.Clear();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, name, category FROM enum_equip_slot";
        command.Prepare();
        using var sqliteReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteReader);
        while (reader.Read())
        {
            var name = reader.GetString("name");
            var definition = new EquipSlotDefinition(
                reader.GetInt32("id"),
                name,
                reader.IsDBNull("category") ? null : reader.GetString("category"))
            {
                IsNoLink = string.Equals(name, NoLinkSlotName, StringComparison.Ordinal)
            };

            if (!_byId.TryAdd(definition.Id, definition))
                throw new InvalidDataException($"enum_equip_slot has duplicate id {definition.Id}");
            if (!_byName.TryAdd(definition.Name, definition.Id))
                throw new InvalidDataException($"enum_equip_slot has duplicate name '{definition.Name}'");
        }

        _ = NoLinkSlotId;
    }

    public bool Contains(int id) => _byId.ContainsKey(id);

    public bool TryGet(int id, out EquipSlotDefinition definition) => _byId.TryGetValue(id, out definition!);

    public EquipSlotDefinition Get(int id) => _byId.TryGetValue(id, out var definition)
        ? definition
        : throw new InvalidDataException($"enum_equip_slot has no row with id {id}");

    public void ValidateSkillLinks(IEnumerable<SkillTemplate> skills)
    {
        var missing = skills
            .Where(skill => !Contains(skill.LinkEquipSlotId))
            .OrderBy(skill => skill.Id)
            .FirstOrDefault();
        if (missing != null)
        {
            throw new InvalidDataException(
                $"skill {missing.Id} links to unknown enum_equip_slot id {missing.LinkEquipSlotId}");
        }
    }
}
