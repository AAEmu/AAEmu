using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Residents;

public class LocalDevelopments : PacketMarshaler
{
    public uint Id { get; set; }
    public uint DoodadAlmightyId { get; set; } // Community Center Workbench
    public uint DoodadPhase0 { get; set; } // фаза для Community Center Workbench
    public uint DoodadPhase1 { get; set; }
    public uint DoodadPhase2 { get; set; }
    public uint DoodadPhase3 { get; set; }
    public ushort ZoneGroupId { get; set; }
    public uint FactionId { get; set; }
    
    public LocalDevelopments()
    {
    }
}
