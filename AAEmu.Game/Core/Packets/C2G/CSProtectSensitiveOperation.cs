using System;
using System.Text;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Chat;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// The client's account-protection request: either "tell me the state" or "protect me".
/// </summary>
/// <remarks>
/// <para>
/// <c>.?AUCSProtectSensitiveOperation@@</c>; the opcode was confirmed by sniff and the body is still not
/// mapped, so what arrives is logged and then read conservatively: an empty body is the state query the
/// client's indicator makes when it opens, and a body with a byte is read as the requested protection flag.
/// Anything longer is treated as the same first byte, with the rest kept in the log line so a later capture
/// can settle the layout.
/// </para>
/// <para>
/// Nothing is answered while feature bit 56 is off — the guard is inert by design and the client does not ask.
/// </para>
/// </remarks>
public class CSProtectSensitiveOperation() : GamePacket(CSOffsets.CSProtectSensitiveOperation, 1)
{
    public override void Read(PacketStream stream)
    {
        var remaining = stream.Count - stream.Pos;
        var dump = DumpHex(stream, remaining, 64);

        var character = Connection?.ActiveChar;
        if (character == null)
        {
            if (remaining > 0)
                stream.ReadBytes(remaining);
            return;
        }

        if (remaining == 0)
        {
            SensitiveOperationGuard.SendState(Connection);
            return;
        }

        var requested = stream.ReadByte();
        if (remaining > 1)
            stream.ReadBytes(remaining - 1);

        Logger.Debug("CSProtectSensitiveOperation len={0} protect={1}: {2}", remaining, requested != 0, dump);

        if (!SensitiveOperationGuard.TrySetProtection(character, requested != 0, out var refusal)
            && !string.IsNullOrEmpty(refusal))
            character.SendMessage(ChatType.System, refusal);
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
