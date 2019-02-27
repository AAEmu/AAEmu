using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSRequestHouseTaxPacket : GamePacket
    {
        public CSRequestHouseTaxPacket() : base(0x05c, 1) //TODO 1.0 opcode: 0x05a
        {
        }

        public override void Read(PacketStream stream)
        {
            var tl = stream.ReadUInt16();

            _log.Debug("RequestHouseTax, Tl: {0}", tl);
        }
    }
}
