using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// The court's ruling: how many jurors have voted, out of how many, the verdict when the vote is
/// complete, and the sentence the verdict carries. While <paramref name="count"/> is below
/// <paramref name="total"/> the client shows the vote in progress; once they are equal it shows the
/// result.
/// </summary>
/// <remarks>
/// Body: count (i32), total (i32), verdict (u8), sentence (u32, milliseconds). The verdict byte is the
/// client's own sentence choice: 1 is not guilty, anything else is a guilty verdict carrying the
/// sentence in the time field.
/// </remarks>
public class SCRulingStatusPacket(int count, int total, byte verdict, uint sentenceMilliseconds)
    : GamePacket(SCOffsets.SCRulingStatusPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(count);
        stream.Write(total);
        stream.Write(verdict);
        stream.Write(sentenceMilliseconds);
        return stream;
    }
}
