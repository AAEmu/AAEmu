using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.TowerDefs;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCTowerDefEndPacket : GamePacket
    {
        private TowerDefKey _key;
        private uint _eventZoneId;
        
        public SCTowerDefEndPacket(TowerDefKey key, uint eventZoneId) : base(SCOffsets.SCTowerDefEndPacket, 1)
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
