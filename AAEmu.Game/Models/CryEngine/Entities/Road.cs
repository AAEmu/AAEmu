namespace AAEmu.Game.Models.CryEngine.Entities;

public class Road
{
    public uint ZoneId { get; }
    public string Name { get; set; } = string.Empty;
    public List<RoadNode> RoadNodeList { get; set; } = [];

    public Road(uint zoneId)
    {
        ZoneId = zoneId;
    }

    public bool Equals(Road other)
    {
        if (this == other)
            return true;

        if (other == null)
            return false;

        return ((ZoneId == other.ZoneId) &&
                Name.Equals(other.Name) &&
                RoadNodeList.SequenceEqual(other.RoadNodeList)
            );
    }
}
