using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Trading;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSBuySpecialtyItemPacket() : GamePacket(CSOffsets.CSBuySpecialtyItemPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var (quote, npcObjId, _) = ReadBody(stream);

        SpecialtyManager.Instance.BuySpecialty(Connection.ActiveChar, npcObjId, quote);
    }

    internal static (SpecialtyQuote Quote, uint NpcObjId, uint Auxiliary) ReadBody(PacketStream stream)
    {
        // The quote came from SCSpecialtyGoods; the server recomputes it before changing state.
        var quote = stream.Read<SpecialtyQuote>();
        var npcObjId = stream.ReadBc();
        var auxiliary = stream.ReadBc();
        return (quote, npcObjId, auxiliary);
    }
}
