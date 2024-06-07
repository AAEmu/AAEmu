using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSDominionUpdateNationalTaxRatePacket : GamePacket
{
    public CSDominionUpdateNationalTaxRatePacket() : base(CSOffsets.CSDominionUpdateNationalTaxRatePacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var id = stream.ReadUInt16();
        var taxRate = stream.ReadInt32();

        Logger.Debug($"DominionUpdateNationalTaxRate, Id: {id}, TaxRate: {taxRate}");
    }
}
