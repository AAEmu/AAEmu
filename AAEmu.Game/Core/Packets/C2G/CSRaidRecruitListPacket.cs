using System;
using System.Text;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// <c>.?AUCSRaidRecruitListPacket@@</c> — a request for the raid recruitment listing.
/// The response side is not implemented, so the request is consumed and logged.
/// </summary>
public class CSRaidRecruitListPacket() : GamePacket(CSOffsets.CSRaidRecruitListPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var remaining = stream.Count - stream.Pos;
        if (remaining > 0)
        {
            var n = Math.Min(remaining, 32);
            var sb = new StringBuilder(n * 3);
            for (var i = 0; i < n; i++)
                sb.AppendFormat("{0:x2} ", stream.Buffer[stream.Pos + i]);
            Logger.Warn("CSRaidRecruitList len={0}: {1}", remaining, sb);
            stream.ReadBytes(remaining);
        }
        else
        {
            Logger.Debug("CSRaidRecruitList empty");
        }
    }
}
