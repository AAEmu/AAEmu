using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSStartInteractionPacket : GamePacket
    {
        public CSStartInteractionPacket() : base(CSOffsets.CSStartInteractionPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var npcObjId = stream.ReadBc();
            var objId = stream.ReadBc();
            var extraInfo = stream.ReadInt32();
            var pickId = stream.ReadInt32();
            var mouseButton = stream.ReadByte();
            var modifierKeys = stream.ReadInt32();

            _log.Warn("StartInteraction, NpcObjId: {0} {1} {2} {3} {4} {5}", npcObjId, objId, extraInfo, pickId, mouseButton, modifierKeys);

        }
    }
}
