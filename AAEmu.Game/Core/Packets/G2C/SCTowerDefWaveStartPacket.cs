using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.TowerDefs;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCTowerDefWaveStartPacket : GamePacket
    {
        private TowerDefKey _key;
        private uint _eventZoneId;
        private uint _step;
        
        public SCTowerDefWaveStartPacket(TowerDefKey key, uint eventZoneId, uint step) : base(SCOffsets.SCTowerDefWaveStartPacket, 1)
        {
            _key = key;
            _eventZoneId = eventZoneId;
            _step = step;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_key);
            stream.Write(_eventZoneId);
            stream.Write(_step);
            return stream;
        }
    }
}
