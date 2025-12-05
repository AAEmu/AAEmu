using AAEmu.Game.Models.CryEngine.Entities;

namespace AAEmu.Game.Models.CryEngine.Mission;

public class FlightNavRegion : Mission
{
    public int HeightFieldOriginX { get; set; }

    public int HeightFieldOriginY { get; set; }

    /// <summary>
    /// The size of the coarse grid of cells.
    /// </summary>
    public int HeightFieldDimX { get; set; }

    /// <summary>
    /// The size of the coarse grid of cells.
    /// </summary>
    public int HeightFieldDimY { get; set; }

    /// <summary>
    /// If the cell is defined as hires (child idx > 0), the cell is divided based on this variable.
    /// </summary>
    public int ChildSubDiv { get; set; }

    /// <summary>
    /// The down sample ratio from the terrain.
    /// </summary>
    public int TerrainDownSample { get; set; }

    public List<Spans> SpanList { get; set; } = [];

    public List<FlightLinkDescriptor> FlightLinkDescriptorList { get; set; } = [];

    public FlightNavRegion(uint zoneId) : base(zoneId)
    {
    }

    public override bool Equals(Mission other)
    {
        if (this == other)
            return true;

        if (other is not FlightNavRegion nav)
            return false;

        return ZoneId.Equals(nav.ZoneId) &&
               Name.Equals(nav.Name) &&
               HeightFieldOriginX.Equals(nav.HeightFieldOriginX) &&
               HeightFieldOriginY.Equals(nav.HeightFieldOriginY) &&
               HeightFieldDimX.Equals(nav.HeightFieldDimX) &&
               HeightFieldDimY.Equals(nav.HeightFieldDimY) &&
               ChildSubDiv.Equals(nav.ChildSubDiv) &&
               TerrainDownSample.Equals(nav.TerrainDownSample) &&
               SpanList.SequenceEqual(nav.SpanList) &&
               FlightLinkDescriptorList.SequenceEqual(nav.FlightLinkDescriptorList);
    }
}
