using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCDoodadsCreatedPacket : GamePacket
    {
        private readonly Doodad[] _doodads;
        public const int MaxCountPerPacket = 30; // Suggested Maximum Size

        public SCDoodadsCreatedPacket(Doodad[] doodads) : base(SCOffsets.SCDoodadsCreatedPacket, 1)
        {
            _doodads = doodads;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((byte)_doodads.Length);
            foreach (var doodad in _doodads)
                doodad.Write(stream);

            return stream;
        }
    }
}
