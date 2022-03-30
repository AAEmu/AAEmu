using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSEnterSysInstancePacket : GamePacket
    {
        public CSEnterSysInstancePacket() : base(CSOffsets.CSEnterSysInstancePacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSEnterSysInstancePacket");
        }
    }
}
