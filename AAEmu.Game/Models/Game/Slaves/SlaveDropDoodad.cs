namespace AAEmu.Game.Models.Game.Slaves;

public class SlaveDropDoodad
{
    public uint Id { get; set; }
    public uint OwnerId { get; set; }
    public string OwnerType { get; set; }
    public uint DoodadId { get; set; }
    public uint Count { get; set; }
    public float Radius { get; set; }
    public bool OnWater { get; set; }
}
