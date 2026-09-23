namespace AAEmu.Game.Models.Game.Residents;

/// <summary>
/// Durability for resident point and charge settlement.
/// Character rows are written at settlement time (not on the save tick): a settlement that is not
/// on disk when the process dies is a contribution the resident made twice or never.
/// </summary>
public interface IResidentStateStore
{
    IReadOnlyList<CharacterResidentState> LoadAll();
    bool UpsertCharacterState(CharacterResidentState row);
}
