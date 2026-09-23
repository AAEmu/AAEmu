namespace AAEmu.Game.Models.Game.Residents;

/// <summary>
/// Durability for resident point/charge settlement and the applied local-development state.
/// Character rows are written at settlement time (not on the save tick): a settlement that is not
/// on disk when the process dies is a contribution the resident made twice or never.
/// </summary>
public interface IResidentStateStore
{
    IReadOnlyList<CharacterResidentState> LoadAll();
    bool UpsertCharacterState(CharacterResidentState row);

    IReadOnlyList<LocalDevelopmentState> LoadDevelopmentStates();
    bool UpsertDevelopmentState(LocalDevelopmentState state);
}
