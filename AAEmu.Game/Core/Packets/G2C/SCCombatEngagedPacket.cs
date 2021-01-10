using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCCombatEngagedPacket : GamePacket
    {
        private readonly uint _objId;

        public SCCombatEngagedPacket(uint objId) : base(SCOffsets.SCCombatEngagedPacket, 5)
        {
            _objId = objId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_objId);
            return stream;
        }
    }
}
