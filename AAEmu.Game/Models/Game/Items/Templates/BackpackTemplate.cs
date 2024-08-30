using System;

namespace AAEmu.Game.Models.Game.Items.Templates;

public class BackpackTemplate : ItemTemplate
{
    public override Type ClassType => typeof(Backpack);

    public uint AssetId { get; set; }
    public BackpackType BackpackType { get; set; }
    public uint DeclareSiegeZoneGroupId { get; set; }
    public bool Heavy { get; set; }
    public uint Asset2Id { get; set; }
    public bool NormalSpeciality { get; set; }
    public bool UseAsStat { get; set; }
    public uint SkinKindId { get; set; }
    public uint FreshnessGroupId { get; set; }
    public uint GliderAnimActionId { get; set; }
    public uint GliderFastAnimActionId { get; set; }
    public uint GliderSlidingAnimActionId { get; set; }
    public uint GliderSlowAnimActionId { get; set; }
}
