using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSExitBeautySalonPacket : GamePacket
    {
        public CSExitBeautySalonPacket() : base(CSOffsets.CSExitBeautySalonPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("ExitBeautySalon");
            Connection.ActiveChar.SendPacket(new SCToggleBeautyshopResponsePacket(0));
        }
    }
}
