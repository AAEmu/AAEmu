using AAEmu.Game.Core.Managers;

namespace AAEmu.Game.Models.Tasks.Duels;

/// <summary>
/// Drops a duel invitation that was never answered.
/// </summary>
/// <remarks>
/// A request reserves both players the moment it is sent, but nothing released that reservation
/// unless the target explicitly accepted or declined. The client offers no way out either - its
/// challenge handler (RVA 0x106690) only builds the dialog and stores the challenger id; there is no
/// timer behind it, so an ignored popup simply stays on screen. Without this task both players stayed
/// registered forever and every later duel was refused with "already in a duel".
/// </remarks>
public class DuelRequestTimeoutTask(uint challengerId) : Task
{
    public override void Execute()
    {
        DuelManager.Instance.DuelRequestExpired(challengerId);
    }
}
