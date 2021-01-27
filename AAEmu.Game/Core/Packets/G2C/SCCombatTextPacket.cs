using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCCombatTextPacket : GamePacket
    {
        private readonly uint _sourceUnitId;
        private readonly uint _targetUnitId;
        private readonly byte _textType;
        
        public SCCombatTextPacket(uint sourceUnitId, uint targetUnitId, byte textType) : base(SCOffsets.SCCombatTextPacket, 5)
        {
            _sourceUnitId = sourceUnitId;
            _targetUnitId = targetUnitId;
            _textType = textType;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_sourceUnitId);
            stream.WriteBc(_targetUnitId);
            stream.Write(_textType);
            return stream;
        }
    }
}
