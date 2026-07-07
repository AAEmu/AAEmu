using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Managers;

public interface ITrialManager
{
    void UpdateTick();
    void UpdateJuryQueue();
    bool ProcessTrialInviteReply(Character player, bool accept, uint trialId);
}
