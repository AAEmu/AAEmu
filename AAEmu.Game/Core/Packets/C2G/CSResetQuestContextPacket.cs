using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSResetQuestContextPacket : GamePacket
    {
        public CSResetQuestContextPacket() : base(CSOffsets.CSResetQuestContextPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var questId = stream.ReadUInt32();
            _log.Debug("ResetQuestContext, Id: {0}", questId);
        }
    }
}
