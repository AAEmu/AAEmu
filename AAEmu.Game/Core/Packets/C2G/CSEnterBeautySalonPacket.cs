using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSEnterBeautySalonPacket : GamePacket
    {
        public CSEnterBeautySalonPacket() : base(CSOffsets.CSEnterBeautySalonPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("EnterBeautySalon");
            Connection.ActiveChar.SendPacket(new SCToggleBeautyshopResponsePacket(1));
        }
    }
}
