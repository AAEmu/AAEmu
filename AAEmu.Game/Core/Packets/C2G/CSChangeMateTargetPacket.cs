using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSChangeMateTargetPacket : GamePacket
    {
        public CSChangeMateTargetPacket() : base(CSOffsets.CSChangeMateTargetPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var tlId = stream.ReadUInt16();
            var objId = stream.ReadBc();

            //_log.Warn("ChangeMateTarget, TlId: {0}, ObjId: {1}", tlId, objId);
            MateManager.Instance.ChangeTargetMate(Connection, tlId, objId);
        }
    }
}
