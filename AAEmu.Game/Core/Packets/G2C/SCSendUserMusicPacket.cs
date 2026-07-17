using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCSendUserMusicPacket(uint playerObjId, string author, byte[] midiData)
    : GamePacket(SCOffsets.SCSendUserMusicPacket, 5)
{
    // not sure yet it this is the actual author or the person playing the music

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((uint)midiData.Length); // total size maybe if multiple blocks ?
        stream.WriteBc(playerObjId);
        stream.Write(author);
        stream.Write(midiData, true);
        return stream;
    }
}
