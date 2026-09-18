using System;
using System.Text;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// The client's account-protection state request (CS 0x19A): "what is my protection window?".
/// </summary>
/// <remarks>
/// <para>
/// The 10.0.2.13 client sends this opcode with no body, so there is no command in it to carry out. The answer
/// is <c>SCProtectSensitiveOperationResultPacket</c> (SC 0x28E), which is what the client's indicator draws
/// its countdown from.
/// </para>
/// <para>
/// A packet that does carry a body therefore did not come from the client, and none of it is read as a
/// request: a crafted byte must not be able to open a window, least of all to drop one without the
/// verification that is supposed to lift it. Only an accepted second password or the <c>/sensitive</c> GM
/// command moves a window — see <see cref="SensitiveOperationGuard"/>.
/// </para>
/// <para>
/// Nothing is answered while feature bit 56 is off: the guard is inert by design and the client does not ask.
/// </para>
/// </remarks>
public class CSProtectSensitiveOperation() : GamePacket(CSOffsets.CSProtectSensitiveOperation, 1)
{
    public override void Read(PacketStream stream)
    {
        var trailing = stream.LeftBytes;
        if (trailing > 0)
        {
            // Recorded rather than obeyed, so a capture can settle whether some other client build writes a
            // body here. The stream is drained either way.
            Logger.Warn(
                "CSProtectSensitiveOperation carried {0} byte(s) after the header, which the 10.0.2.13 client never sends: {1}",
                trailing, DumpHex(stream, trailing, 64));
            stream.ReadBytes(trailing);
        }

        SensitiveOperationGuard.SendState(Connection);
    }

    private static string DumpHex(PacketStream stream, int length, int maxBytes)
    {
        if (length <= 0)
            return string.Empty;

        var n = Math.Min(length, maxBytes);
        var sb = new StringBuilder(n * 3);
        for (var i = 0; i < n; i++)
            sb.AppendFormat("{0:x2} ", stream.Buffer[stream.Pos + i]);
        if (length > maxBytes)
            sb.Append($"...(+{length - maxBytes}B)");
        return sb.ToString();
    }
}
