namespace AAEmu.Game.Models.CryEngine.Mission;

public abstract class Mission
{
    public uint ZoneId { get; }
    public string Name { get; set; } = string.Empty;

    protected Mission(uint zoneId)
    {
        ZoneId = zoneId;
    }

    public virtual bool Equals(Mission other)
    {
        if (this == other)
            return true;

        if (other == null)
            return false;

        return (ZoneId.Equals(other.ZoneId) &&
                Name.Equals(other.Name));
    }
}
