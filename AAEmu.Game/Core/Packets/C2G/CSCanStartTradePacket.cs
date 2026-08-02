using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <remarks>
/// </remarks>
public class CSCanStartTradePacket() : GamePacket(CSOffsets.CSCanStartTradePacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var objId = stream.ReadBc();

        var target = WorldManager.Instance.GetCharacterByObjId(objId);
        if (target == null) return;
        var owner = Connection.ActiveChar;
        if (owner == null)
            return;

        TradeManager.Instance.CanStartTrade(owner, target);
    }
}
