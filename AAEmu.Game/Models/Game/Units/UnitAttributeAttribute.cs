namespace AAEmu.Game.Models.Game.Units;

//Yes the naming looks weird, ignore it please.
[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public class UnitAttributeAttribute(params UnitAttribute[] attributes) : Attribute
{
    public List<UnitAttribute> Attributes { get; set; } = [.. attributes];
}
