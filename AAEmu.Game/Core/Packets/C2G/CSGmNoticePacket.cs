using System.Drawing;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// X2Gm:NoticeEx → World. Wire (10.0.2.13): string message, string color (ARGB hex), u8 type.
/// type matches gm_console combo: 1=chat, 2=center, 3=all.
/// </summary>
public class CSGmNoticePacket() : GamePacket(CSOffsets.CSGmNoticePacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var message = stream.ReadString() ?? "";
        var colorHex = stream.ReadString() ?? "";
        var noticeType = stream.ReadByte();

        Logger.Info("CSGmNotice type={0} color={1} msg={2}", noticeType, colorHex, message);

        if (Connection.GetAttribute("gmFlag") == null)
        {
            Logger.Warn("CSGmNotice rejected — no gmFlag");
            return;
        }

        if (string.IsNullOrWhiteSpace(message))
            return;

        if (noticeType is < 1 or > 3)
            noticeType = 3;

        var color = ParseArgbHex(colorHex);
        var visibleMs = 1000 + message.Length * 50;
        var gmName = Connection.ActiveChar?.Name ?? "";

        WorldManager.Instance.BroadcastPacketToServer(
            new SCNoticeMessagePacket(noticeType, color, visibleMs, message, gmName));
    }

    private static Color ParseArgbHex(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return Color.FromArgb(0xFF, 0xC1, 0x3D, 0x36); // gm_console default red

        hex = hex.Trim().TrimStart('#', '|', 'c', 'C');
        if (hex.StartsWith("c", System.StringComparison.OrdinalIgnoreCase) && hex.Length >= 9)
            hex = hex[1..];

        try
        {
            if (hex.Length >= 8)
            {
                var a = System.Convert.ToByte(hex[..2], 16);
                var r = System.Convert.ToByte(hex[2..4], 16);
                var g = System.Convert.ToByte(hex[4..6], 16);
                var b = System.Convert.ToByte(hex[6..8], 16);
                return Color.FromArgb(a == 0 ? 0xFF : a, r, g, b);
            }

            if (hex.Length >= 6)
            {
                var r = System.Convert.ToByte(hex[..2], 16);
                var g = System.Convert.ToByte(hex[2..4], 16);
                var b = System.Convert.ToByte(hex[4..6], 16);
                return Color.FromArgb(0xFF, r, g, b);
            }
        }
        catch
        {
            // fall through
        }

        return Color.FromArgb(0xFF, 0xC1, 0x3D, 0x36);
    }
}
