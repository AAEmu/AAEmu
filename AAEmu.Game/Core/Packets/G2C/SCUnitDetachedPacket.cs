using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.DoodadObj.Static;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCUnitDetachedPacket : GamePacket
    {
        private readonly uint _childUnitId;
        private readonly AttachUnitReason _reason;

        public SCUnitDetachedPacket(uint objId, AttachUnitReason reason) : base(SCOffsets.SCUnitDetachedPacket, 1)
        {
            _childUnitId = objId;
            _reason = reason;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_childUnitId);
            stream.Write((byte)_reason);
            return stream;
        }
    }
}
