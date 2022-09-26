using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.TowerDefs;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCTowerDefStartPacket : GamePacket
    {
        private TowerDefKey _key;
        private uint _eventZoneId;
        
        public SCTowerDefStartPacket(TowerDefKey key, uint eventZoneId) : base(SCOffsets.SCTowerDefStartPacket, 5)
        {
            _key = key;
            _eventZoneId = eventZoneId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_key);
            stream.Write(_eventZoneId);
            return stream;
        }
    }
}
