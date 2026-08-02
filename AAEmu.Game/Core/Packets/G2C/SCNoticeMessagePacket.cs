using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using System.Drawing;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
///   u8 noticeType, string colorStr, u32 visibleTime, string message, string name.
/// </summary>
/// <remarks>
/// The bulk layout extraction reports four fields here and stops at the message. The summary above comes
/// writes; a named address beats a count taken from the shared serializer table.
/// </remarks>
public class SCNoticeMessagePacket : GamePacket
{
    private readonly string _message = "";
    private readonly byte _type = 3;
    private readonly string _colorStr = "FF80FF80";
    private readonly int _vistime = 1000;
    private readonly string _name = "";

    public SCNoticeMessagePacket(byte type, Color ARGBColor, int vistime, string message)
        : this(type, ARGBColor, vistime, message, "")
    {
    }

    public SCNoticeMessagePacket(byte type, Color ARGBColor, int vistime, string message, string name)
        : base(SCOffsets.SCNoticeMessagePacket, 1)
    {
        if (ARGBColor.A <= 0)
            ARGBColor = Color.FromArgb(0xFF, ARGBColor.R, ARGBColor.G, ARGBColor.B);
        if (vistime <= 0)
            vistime = 1000 + (message?.Length ?? 0) * 50;

        _type = type;
        _colorStr = $"{ARGBColor.A:X2}{ARGBColor.R:X2}{ARGBColor.G:X2}{ARGBColor.B:X2}";
        _vistime = vistime;
        _message = message ?? "";
        _name = name ?? "";
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_type);
        stream.Write(_colorStr);
        stream.Write(_vistime);
        stream.Write(_message);
        stream.Write(_name);
        return stream;
    }
}
