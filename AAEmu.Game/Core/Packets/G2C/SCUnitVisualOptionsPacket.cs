using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCUnitVisualOptionsPacket : GamePacket
    {
        private readonly uint _id;
        private readonly CharacterVisualOptions _visualOptions;

        public SCUnitVisualOptionsPacket(uint id, CharacterVisualOptions visualOptions) : base(SCOffsets.SCUnitVisualOptionsPacket, 5)
        {
            _id = id;
            _visualOptions = visualOptions;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_id);
            stream.Write(_visualOptions);
            return stream;
        }
    }
}
