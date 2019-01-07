using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCResponseUIDataPacket : GamePacket
    {
        private readonly uint _characterId;
        private readonly string _characterName;
        private readonly string _uiDataKey;
        private readonly string _uiData;
        
        public SCResponseUIDataPacket(uint characterId, string characterName, string uiDataKey, string uiData) : base(0x1c0, 1)
        {
            _characterId = characterId;
            _characterName = characterName;
            _uiDataKey = uiDataKey;
            _uiData = uiData;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_characterId);
            stream.Write(_characterName);
            stream.Write(_uiDataKey);
            stream.Write(_uiData);
            stream.Write(_uiData.Length + 1);
            return stream;
        }
    }
}