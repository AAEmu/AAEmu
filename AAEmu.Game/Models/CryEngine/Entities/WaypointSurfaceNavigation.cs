namespace AAEmu.Game.Models.CryEngine.Entities;

public class WaypointSurfaceNavigation(uint zoneId)
{
    public uint ZoneId { get; } = zoneId;
    public List<LinkRecord> LinkedVolumeRecords { get; set; } = [];
    public List<LinkRecord> LinkedFlightRecords { get; set; } = [];

    public bool Equals(WaypointSurfaceNavigation other)
    {
        if (this == other)
            return true;

        if (other == null)
            return false;

        return ZoneId.Equals(other.ZoneId) &&
               LinkedVolumeRecords.SequenceEqual(other.LinkedVolumeRecords) &&
               LinkedFlightRecords.SequenceEqual(other.LinkedFlightRecords);
    }
}
