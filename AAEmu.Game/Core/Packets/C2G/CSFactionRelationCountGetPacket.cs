using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// X2Nation:CanGetRelationCount: the request window asks for the viewer's diplomacy counters
/// before it lists heroes. Answered with SCFactionRelationCount.
/// </summary>
/// <remarks>
/// packet has no body. Every parameterless C2S type folds onto that one function, so the
/// shared address is identical-COMDAT folding, not a base-class fall-through.
/// </remarks>
public class CSFactionRelationCountGetPacket() : GamePacket(CSOffsets.CSFactionRelationCountGetPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var character = Connection?.ActiveChar;
        if (character == null)
            return;

        FactionDiplomacyManager.Instance.SendCounts(character);
    }
}
