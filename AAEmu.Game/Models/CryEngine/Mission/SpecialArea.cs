using System.Numerics;
using AAEmu.Game.Models.CryEngine.Mission;

namespace AAEmuGeoData.Scripts.CryEngine.Mission;

public class SpecialArea(uint zoneId) : AAEmu.Game.Models.CryEngine.Mission.Mission(zoneId)
{
    public MissionType MissionType { get; set; }
    public WaypointConnections WaypointConnections { get; set; } = new();
    public bool Altered { get; set; }
    public double Height { get; set; }
    public double NodeAutoConnectDistance { get; set; }
    public double MinZ { get; set; }
    public double MaxZ { get; set; }
    public int BuildingId { get; set; }
    public AiLightLevel AiLightLevel { get; set; } = AiLightLevel.None;
    public List<Vector3> Points { get; set; } = [];

    public override bool Equals(AAEmu.Game.Models.CryEngine.Mission.Mission other)
    {
        if (this == other)
            return true;

        if (other is not SpecialArea area)
            return false;

        return (ZoneId.Equals(area.ZoneId) &&
                Name.Equals(area.Name) &&
                MissionType.Equals(area.MissionType) &&
                WaypointConnections.Equals(area.WaypointConnections) &&
                (Altered == area.Altered) &&
                Height.Equals(area.Height) &&
                NodeAutoConnectDistance.Equals(area.NodeAutoConnectDistance) &&
                MinZ.Equals(area.MinZ) &&
                MaxZ.Equals(area.MaxZ) &&
                BuildingId.Equals(area.BuildingId) &&
                AiLightLevel.Equals(area.AiLightLevel) &&
                Points.SequenceEqual(area.Points));
    }
}
