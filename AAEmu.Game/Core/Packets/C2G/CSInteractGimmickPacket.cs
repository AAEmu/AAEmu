using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSInteractGimmickPacket : GamePacket
{
    public CSInteractGimmickPacket() : base(CSOffsets.CSInteractGimmickPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        Logger.Debug("CSInteractGimmickPacket");
    }
}
