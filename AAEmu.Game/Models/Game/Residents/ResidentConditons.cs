using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Residents;

public class ResidentConditions : PacketMarshaler
{
    public uint Id { get; set; }
    public uint CategoryId { get; set; }
    
    public ResidentConditions()
    {
    }
}
