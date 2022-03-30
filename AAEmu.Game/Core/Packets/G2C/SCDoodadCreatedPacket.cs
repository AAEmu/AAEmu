using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCDoodadCreatedPacket : GamePacket
    {
        private readonly Doodad _doodad;
        
        public SCDoodadCreatedPacket(Doodad doodad) : base(SCOffsets.SCDoodadCreatedPacket, 5)
        {
            _doodad = doodad;
        }

        public override PacketStream Write(PacketStream stream)
        {
            return _doodad.Write(stream);
        }
    }
}
