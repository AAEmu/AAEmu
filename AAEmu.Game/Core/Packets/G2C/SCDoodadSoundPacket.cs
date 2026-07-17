using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCDoodadSoundPacket(Doodad doodad, uint soundId) : GamePacket(SCOffsets.SCDoodadSoundPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(doodad.ObjId);
        stream.Write(soundId);

        return stream;
    }
}
