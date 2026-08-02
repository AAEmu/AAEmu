using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <remarks>
/// </remarks>
public class CSStartTradePacket() : GamePacket(CSOffsets.CSStartTradePacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var objId = stream.ReadBc();

        var owner = WorldManager.Instance.GetCharacterByObjId(objId);
        if (owner == null) return;
        var target = Connection.ActiveChar;
        if (target == null)
            return;

        TradeManager.Instance.StartTrade(owner, target);
    }
}
