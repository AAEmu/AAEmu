using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Emotion;

public class ExpressText : PacketMarshaler
{
    public uint Id { get; set; }
    public string OtherTarget { get; set; }
    public string OtherMe { get; set; }
    public string Other { get; set; }
    public uint NpcAnimId { get; set; }
    public string MeTarget { get; set; }
    public string Me { get; set; }
    public uint AnimId { get; set; }
}
