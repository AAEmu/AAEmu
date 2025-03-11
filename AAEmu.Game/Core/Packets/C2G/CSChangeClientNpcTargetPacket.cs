using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSChangeClientNpcTargetPacket : GamePacket
{
    public CSChangeClientNpcTargetPacket() : base(CSOffsets.CSChangeClientNpcTargetPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        Logger.Debug("CSChangeClientNpcTargetPacket");
    }
}
