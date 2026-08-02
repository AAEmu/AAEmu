using System;
using System.Text;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// <c>.?AUCSProtectSensitiveOperation@@</c>; the opcode was already confirmed by sniff.
/// Body layout is not yet mapped, so the payload is consumed and logged.
/// </summary>
public class CSProtectSensitiveOperation() : GamePacket(CSOffsets.CSProtectSensitiveOperation, 1)
{
    public override void Read(PacketStream stream)
    {
        var remaining = stream.Count - stream.Pos;
        var dump = DumpHex(stream, remaining, 64);
        Logger.Warn("CSProtectSensitiveOperation len={0}: {1}", remaining, dump);
        if (remaining > 0)
            stream.ReadBytes(remaining);
    }

    private static string DumpHex(PacketStream stream, int length, int maxBytes)
    {
        var n = Math.Min(length, maxBytes);
        var sb = new StringBuilder(n * 3);
        for (var i = 0; i < n; i++)
            sb.AppendFormat("{0:x2} ", stream.Buffer[stream.Pos + i]);
        if (length > maxBytes)
            sb.Append($"...(+{length - maxBytes}B)");
        return sb.ToString();
    }
}
