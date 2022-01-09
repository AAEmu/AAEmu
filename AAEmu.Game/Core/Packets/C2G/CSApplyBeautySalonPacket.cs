using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSApplyBeautySalonPacket : GamePacket
    {
        public CSApplyBeautySalonPacket() : base(CSOffsets.CSApplyBeautySalonPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("ApplyBeautySalon");
            Connection.ActiveChar.SendPacket(new SCToggleBeautyshopResponsePacket(0)); // close the Salon
            Connection.ActiveChar.SendMessage("Salon is currently not working yet!");
        }
    }
}
