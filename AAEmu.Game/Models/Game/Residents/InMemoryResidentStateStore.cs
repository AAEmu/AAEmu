namespace AAEmu.Game.Models.Game.Residents;

/// <summary>Process-lifetime store: tests, and a World that has not loaded MySQL yet.</summary>
public sealed class InMemoryResidentStateStore : IResidentStateStore
{
    private readonly Dictionary<(uint Owner, ushort ZoneGroup), CharacterResidentState> _characters = [];

    public IReadOnlyList<CharacterResidentState> LoadAll() => _characters.Values.ToList();

    public bool UpsertCharacterState(CharacterResidentState row)
    {
        if (row == null)
            return false;
        _characters[(row.OwnerId, row.ZoneGroupId)] = row;
        return true;
    }
}
