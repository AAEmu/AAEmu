using System.Numerics;

using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Managers;

public interface ISusManager
{
    bool LogActivity(string category, ulong accountId, uint playerId, uint zoneGroup, Vector3 position, string description);
    bool LogActivity(string category, Character player, string description);
    bool LogActivity(string category, string description);
    void AnalyzePlayerDeltaMovement(Character player, float deltaTime);
    void ResetAnalyzePlayerDeltaMovement(uint playerId);
    void AnalyzeMountDeltaMovement(Mate pet, float deltaTime);
    void ResetAnalyzeMountDeltaMovement(uint petId);
}
