using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSChangeExpeditionSponsorPacket : GamePacket
    {
        public CSChangeExpeditionSponsorPacket() : base(0x005, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var unkId = stream.ReadUInt32();
            var unk2Id = stream.ReadUInt32();

            _log.Debug("ChangeExpeditionSponsor, Id: {0}, Id2: {1}", unkId, unk2Id);
        }
    }
}
