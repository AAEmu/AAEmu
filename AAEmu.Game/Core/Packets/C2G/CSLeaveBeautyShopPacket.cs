using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSLeaveBeautyShopPacket : GamePacket
    {
        public CSLeaveBeautyShopPacket() : base(CSOffsets.CSLeaveBeautyShopPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            //_log.Debug("ExitBeautySalon");
            Connection.ActiveChar.SendPacket(new SCToggleBeautyShopResponsePacket(0));
            Connection.ActiveChar.Buffs.RemoveBuff((uint)BuffConstants.InBeautySalon);
        }
    }
}
