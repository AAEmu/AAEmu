using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSEnprotectStubCallResponsePacket : GamePacket
    {
        public CSEnprotectStubCallResponsePacket() : base(CSOffsets.CSEnprotectStubCallResponsePacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSEnprotectStubCallResponsePacket");
        }
    }
}
