namespace AAEmu.Game.Models.CryEngine.Entities;

public class Navigation
{
    public Navigation(uint zoneId)
    {
        ZoneId = zoneId;
    }

    public uint ZoneId { get; }
    public BBox BBox { get; set; } = new();
    public List<NodeDescriptor> DescriptorList { get; set; } = [];
    public List<LinkDescriptor> LinkDescriptorList { get; set; } = [];

    public bool Equals(Navigation other)
    {
        if (this == other)
            return true;

        if (other == null)
            return false;

        return ZoneId == other.ZoneId &&
               BBox.Equals(other.BBox) &&
               DescriptorList.SequenceEqual(other.DescriptorList) &&
               LinkDescriptorList.SequenceEqual(other.LinkDescriptorList);
    }
}
