using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Char;

public class CharacterVisualOptions : PacketMarshaler
{
    private byte _flag;
    public byte[] Stp;
    public bool Helmet;
    public bool BackHoldable;
    public bool Cosplay;
    public bool CosplayBackpack;
    public byte CosplayVisual;
    public bool Ipnir;

    // Visual-option flags + conditional fields (x2game-dev_dedicate sub_39D1C0B0/sub_39C1EA20):
    // voptflag u8, then per set bit: 0x01 stp[6], 0x02 helmet, 0x04 back_holdable, 0x08 cosplay,
    // 0x10 cosplay_backpack, 0x20 cosplay_visual (u8), 0x40 ipnir. cosplay_visual is written before ipnir.
    public override void Read(PacketStream stream)
    {
        _flag = stream.ReadByte();
        if ((_flag & 1) == 1)
            Stp = stream.ReadBytes(6);
        if ((_flag & 2) == 2)
            Helmet = stream.ReadBoolean();
        if ((_flag & 4) == 4)
            BackHoldable = stream.ReadBoolean();
        if ((_flag & 8) == 8)
            Cosplay = stream.ReadBoolean();
        if ((_flag & 16) == 16)
            CosplayBackpack = stream.ReadBoolean();
        if ((_flag & 32) == 32)
            CosplayVisual = stream.ReadByte();
        if ((_flag & 64) == 64)
            Ipnir = stream.ReadBoolean();
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_flag);
        return Write(stream, _flag);
    }

    public PacketStream Write(PacketStream stream, byte flag)
    {
        if ((flag & 1) == 1)
            stream.Write(Stp);
        if ((flag & 2) == 2)
            stream.Write(Helmet);
        if ((flag & 4) == 4)
            stream.Write(BackHoldable);
        if ((flag & 8) == 8)
            stream.Write(Cosplay);
        if ((flag & 16) == 16)
            stream.Write(CosplayBackpack);
        if ((flag & 32) == 32)
            stream.Write(CosplayVisual);
        if ((flag & 64) == 64)
            stream.Write(Ipnir);
        return stream;
    }
    // 10.0.2.13 PremiumVisual block (binary UnitState_SerializePremiumVisual 0x3937E700): 6 stp bytes, one
    // bitpacked flags byte (bit0 helmet, bit1 back_holdable, bit2 cosplay, bit3 cosplay_backpack, bit4 ipnir),
    // then cosplay_visual. 8 bytes total — output inside SCUnitStatePacket's character tail.
    public PacketStream WriteOptions(PacketStream stream)
    {
        stream.Write(Stp is { Length: 6 } ? Stp : new byte[6]); // stp (exactly 6 bytes)
        var flags = (byte)((Helmet ? 1 : 0)
                           | (BackHoldable ? 1 : 0) << 1
                           | (Cosplay ? 1 : 0) << 2
                           | (CosplayBackpack ? 1 : 0) << 3
                           | (Ipnir ? 1 : 0) << 4);
        stream.Write(flags);           // 5 packed bools
        stream.Write(CosplayVisual);   // cosplay_visual
        return stream;
    }
}
