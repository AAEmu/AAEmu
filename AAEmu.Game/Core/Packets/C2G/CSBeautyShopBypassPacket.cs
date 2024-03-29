using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSBeautyShopBypassPacket : GamePacket
{
    public CSBeautyShopBypassPacket() : base(CSOffsets.CSBeautyShopBypassPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        //Logger.Debug("EnterBeautySalon");
        Connection.ActiveChar.SendPacket(new SCToggleBeautyshopResponsePacket(1));
        Connection.ActiveChar.Buffs.AddBuff((uint)BuffConstants.InBeautySalon,Connection.ActiveChar);
    }
}