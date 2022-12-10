using System.Collections.Generic;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.TowerDefs;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCTowerDefListPacket : GamePacket
    {
        private List<TowerDefInfo> _towerDefInfoList;
        
        public SCTowerDefListPacket(List<TowerDefInfo> towerDefInfos) : base(SCOffsets.SCTowerDefListPacket, 1)
        {
            _towerDefInfoList = towerDefInfos;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_towerDefInfoList.Count);
            foreach (var towerDefInfo in _towerDefInfoList)
                towerDefInfo.Write(stream);
            return stream;
        }
    }
}
