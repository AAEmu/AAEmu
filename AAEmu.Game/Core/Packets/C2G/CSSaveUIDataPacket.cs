using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// The client persisting a piece of UI state - window positions, hotbar layout and the like.
/// </summary>
/// <remarks>
/// Guarded because the client keeps sending these while it has no character in the world. Losing a zone
/// drops ActiveChar but leaves the game connection up, and the UI carries on saving into it - which
/// turned one zone crash into a repeating NullReferenceException on every subsequent save.
///
/// The id field is read and discarded: SetOption keys purely on the type, and every shipped save sends
/// the same id for a given type. It still has to come off the stream for the string behind it to align.
/// </remarks>
public class CSSaveUIDataPacket() : GamePacket(CSOffsets.CSSaveUIDataPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var uiDataType = stream.ReadUInt16();
        _ = stream.ReadUInt32();
        var data = stream.ReadString();

        Connection?.ActiveChar?.SetOption(uiDataType, data);
    }
}
