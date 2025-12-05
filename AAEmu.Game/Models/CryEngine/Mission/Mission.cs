namespace AAEmu.Game.Models.CryEngine.Mission;

public abstract class Mission(uint zoneId)
{
    public uint ZoneId { get; } = zoneId;
    public string Name { get; set; } = string.Empty;

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
