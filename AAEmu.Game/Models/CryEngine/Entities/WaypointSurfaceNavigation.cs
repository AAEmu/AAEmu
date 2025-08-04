namespace AAEmu.Game.Models.CryEngine.Entities;

public class WaypointSurfaceNavigation
{
    public uint ZoneId { get; }
    public List<LinkRecord> LinkedVolumeRecords { get; set; } = new();
    public List<LinkRecord> LinkedFlightRecords { get; set; } = new();

    public WaypointSurfaceNavigation(uint zoneId)
    {
        ZoneId = zoneId;
    }

    public bool Equals(WaypointSurfaceNavigation other)
    {
        if (this == other)
            return true;

        if (other == null)
            return false;

        return (ZoneId.Equals(other.ZoneId) &&
                LinkedVolumeRecords.SequenceEqual(other.LinkedVolumeRecords) &&
                LinkedFlightRecords.SequenceEqual(other.LinkedFlightRecords));
    }
}
