using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSStartQuestContextPacket : GamePacket
    {
        public CSStartQuestContextPacket() : base(0x0d5, 1) //TODO 1.0 opcode: 0x0d1
        {
        }

        public override void Read(PacketStream stream)
        {
            var questId = stream.ReadUInt32(); // qType
            var objId = stream.ReadBc();       // npcId
            var objId2 = stream.ReadBc();      // doodadId
            var type = stream.ReadUInt32();    // sphereType

            if (objId > 0 &&
                Connection.ActiveChar.CurrentTarget != null &&
                Connection.ActiveChar.CurrentTarget.ObjId != objId)
                return;
            Connection.ActiveChar.Quests.Add(questId);
        }
    }
}
