using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCDoodadSoundPacket : GamePacket
    {
        private readonly Doodad _doodad;
        private readonly uint _soundId;

        public SCDoodadSoundPacket(Doodad doodad, uint soundId) : base(SCOffsets.SCDoodadSoundPacket, 1)
        {
            _doodad = doodad;
            _soundId = soundId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_doodad.ObjId);
            stream.Write(_soundId);

            return stream;
        }
    }
}
