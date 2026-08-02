using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <remarks>
/// </remarks>
public class CSChangeMateTargetPacket() : GamePacket(CSOffsets.CSChangeMateTargetPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var tlId = stream.ReadInt16();
        var targetId = stream.ReadBc();

        Connection.ActiveChar?.ParentWorld?.MateManager.ChangeTargetMate(Connection, tlId, targetId);
    }
}
