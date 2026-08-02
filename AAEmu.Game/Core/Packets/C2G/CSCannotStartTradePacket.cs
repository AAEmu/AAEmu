using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;

namespace AAEmu.Game.Core.Packets.C2G;

/// <remarks>
/// </remarks>
public class CSCannotStartTradePacket() : GamePacket(CSOffsets.CSCannotStartTradePacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var objId = stream.ReadBc();
        var reason = stream.ReadInt32();

        var target = Connection.ActiveChar;
        if (target == null)
            return;

        var owner = WorldManager.Instance.GetCharacterByObjId(objId);
        if (owner == null)
            return;

        TradeManager.Instance.CannotStartTrade(owner, target, reason);
    }
}
