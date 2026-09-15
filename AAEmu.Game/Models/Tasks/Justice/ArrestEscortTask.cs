using AAEmu.Game.Core.Managers;

namespace AAEmu.Game.Models.Tasks.Justice;

/// <summary>
/// Finishes an arrest: the arrest-state buff has run out, so the character is escorted to their
/// courthouse and asked to choose between the sentence and a trial.
/// </summary>
public class ArrestEscortTask(uint characterId) : Task
{
    public override void Execute()
    {
        JusticeManager.Instance.CompleteEscort(characterId);
    }
}
