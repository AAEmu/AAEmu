using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSSpecialtyPacket() : GamePacket(CSOffsets.CSSpecialtyPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        SpecialtyManager.Instance.SetTradeInfoSubscription(Connection.ActiveChar, stream.ReadBoolean());
    }
}
