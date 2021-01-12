using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCResponseUIDataPacket : GamePacket
    {
        private readonly uint _characterId;
        private readonly ushort _uiDataType;
        private readonly string _uiData;

        public SCResponseUIDataPacket(uint characterId, ushort uiDataType, string uiData) : base(SCOffsets.SCResponseUIDataPacket, 5)
        {
            _characterId = characterId;
            _uiDataType = uiDataType;
            _uiData = uiData;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_characterId);
            stream.Write(_uiDataType);
            stream.Write(_uiData);
            stream.Write(_uiData.Length + 1);
            return stream;
        }
    }
}
