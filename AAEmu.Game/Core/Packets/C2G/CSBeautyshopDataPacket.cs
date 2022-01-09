using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSBeautyshopDataPacket : GamePacket
    {
        public CSBeautyshopDataPacket() : base(CSOffsets.CSBeautyshopDataPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("BeautyshopData");
            Connection.ActiveChar.SendPacket(new SCToggleBeautyshopResponsePacket(0)); // close the Salon
            Connection.ActiveChar.SendMessage("Salon is currently not implemented yet!");
        }
    }
}
