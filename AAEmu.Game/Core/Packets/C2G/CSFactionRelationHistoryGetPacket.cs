using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// X2Nation:GetRelationHistoryList(true): the history window asks for past agreements. Answered
/// with SCFactionRelationHistory.
/// </summary>
/// <remarks>
/// packet has no body. Every parameterless C2S type folds onto that one function, so the
/// shared address is identical-COMDAT folding, not a base-class fall-through.
/// </remarks>
public class CSFactionRelationHistoryGetPacket() : GamePacket(CSOffsets.CSFactionRelationHistoryGetPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var character = Connection?.ActiveChar;
        if (character == null)
            return;

        FactionDiplomacyManager.Instance.SendHistory(character);
    }
}
