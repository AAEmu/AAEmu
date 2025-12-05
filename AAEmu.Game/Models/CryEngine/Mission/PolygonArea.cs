using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AAEmu.Game.Models.CryEngine.Mission;

namespace AAEmuGeoData.Scripts.CryEngine.Mission;

public class PolygonArea : AAEmu.Game.Models.CryEngine.Mission.Mission
{
    public List<Vector3> Points { get; set; } = [];
    public NavigationType NavigationType { get; set; } = NavigationType.Unset;
    public int Type { get; set; }
    public double Height { get; set; }
    public AiLightLevel AiLightLevel { get; set; } = AiLightLevel.None;

    public PolygonArea(uint zoneId) : base(zoneId)
    {
    }

    public override bool Equals(AAEmu.Game.Models.CryEngine.Mission.Mission other)
    {
        if (this == other) 
            return true;

        if (other is not PolygonArea poly)
            return false;

        return (ZoneId.Equals(poly.ZoneId) &&
                Name.Equals(poly.Name) &&
                Points.SequenceEqual(poly.Points) &&
                NavigationType.Equals(poly.NavigationType) &&
                Type.Equals(poly.Type) &&
                Height.Equals(poly.Height) &&
                AiLightLevel.Equals(poly.NavigationType));
    }
}
