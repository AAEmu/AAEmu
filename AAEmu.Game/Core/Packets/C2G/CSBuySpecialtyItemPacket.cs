using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Trading;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSBuySpecialtyItemPacket() : GamePacket(CSOffsets.CSBuySpecialtyItemPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        // from SCSpecialtyRatio. The server recomputes that quote before changing stock or money.
        var npcObjId = stream.ReadBc();
        var characterObjId = stream.ReadBc();
        var quote = stream.Read<SpecialtyQuote>();

        SpecialtyManager.Instance.BuySpecialty(Connection.ActiveChar, npcObjId, characterObjId, quote);
    }
}
