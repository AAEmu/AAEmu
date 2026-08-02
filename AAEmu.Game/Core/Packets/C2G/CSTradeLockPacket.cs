using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSTradeLockPacket() : GamePacket(CSOffsets.CSTradeLockPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var isLocked = stream.ReadBoolean();
        var character = Connection.ActiveChar;
        if (character == null)
            return;

        TradeManager.Instance.LockTrade(character, isLocked);
    }
}
