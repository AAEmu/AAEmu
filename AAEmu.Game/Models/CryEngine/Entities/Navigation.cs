namespace AAEmu.Game.Models.CryEngine.Entities;

internal class Navigation
{
    public Navigation(uint zoneId)
    {
        ZoneId = zoneId;
    }

    public uint ZoneId { get; }
    public BBox BBox { get; set; } = new();
    public List<NodeDescriptor> DescriptorList { get; set; } = new();
    public List<LinkDescriptor> LinkDescriptorList { get; set; } = new();

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
