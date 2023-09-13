using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSApplyToInstantGamePacket : GamePacket
    {
        public CSApplyToInstantGamePacket() : base(CSOffsets.CSApplyToInstantGamePacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var instanceId = stream.ReadUInt32();
            var corps = stream.ReadByte();

            _log.Warn("ApplyToInstantGame, InstanceId: {0}, Corps: {1}", instanceId, corps);
        }
    }
}
