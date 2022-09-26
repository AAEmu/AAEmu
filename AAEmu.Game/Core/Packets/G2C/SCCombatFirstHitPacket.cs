using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCCombatFirstHitPacket : GamePacket
    {
        private readonly uint _vuId;
        private readonly uint _huId;
        private readonly uint _htId;
        
        public SCCombatFirstHitPacket(uint vuId, uint huId, uint htId) : base(SCOffsets.SCCombatFirstHitPacket, 5)
        {
            _vuId = vuId;
            _huId = huId;
            _htId = htId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_vuId);
            stream.WriteBc(_huId);
            stream.Write(_htId);
            return stream;
        }
    }
}
