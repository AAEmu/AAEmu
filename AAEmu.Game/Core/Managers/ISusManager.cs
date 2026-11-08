using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using Jitter2.LinearMath;

namespace AAEmu.Game.Core.Managers;

public interface ISusManager
{
    bool LogActivity(string category, uint accountId, uint playerId, uint zoneGroup, JVector position, string description);
    bool LogActivity(string category, Character player, string description);
    bool LogActivity(string category, string description);
    void AnalyzePlayerDeltaMovement(Character player, float deltaTime);
    void ResetAnalyzePlayerDeltaMovement(uint playerId);
    void AnalyzeMountDeltaMovement(Mate pet, float deltaTime);
    void ResetAnalyzeMountDeltaMovement(uint petId);
}
