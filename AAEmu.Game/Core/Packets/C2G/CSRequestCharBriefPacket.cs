using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSRequestCharBriefPacket : GamePacket
    {
        public CSRequestCharBriefPacket() : base(0x02b, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var id = stream.ReadUInt32();

            _log.Debug("RequestCharBrief, Id: {0}", id);
        }
    }
}
