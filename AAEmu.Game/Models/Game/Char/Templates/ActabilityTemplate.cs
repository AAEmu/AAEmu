namespace AAEmu.Game.Models.Game.Char.Templates;

public class ActabilityTemplate
{
    public uint Id { get; set; }
    public string Name { get; set; }
    public int UnitAttributeId { get; set; }
    public uint ViewGroupId { get; set; }
    public bool CountsTowardExpertLimit { get; set; }
}
