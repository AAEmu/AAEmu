using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSCofferInteractionPacket : GamePacket
    {
        public CSCofferInteractionPacket() : base(CSOffsets.CSCofferInteractionPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var cofferDbDoodadId = stream.ReadBc();
            var start = stream.ReadBoolean();
            
            _log.Warn("CofferInteraction, cofferDbDoodadId: {0}, start: {1}", cofferDbDoodadId, start);
        }
    }
}
