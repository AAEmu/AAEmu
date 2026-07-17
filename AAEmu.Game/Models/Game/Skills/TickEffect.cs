namespace AAEmu.Game.Models.Game.Skills;

public class TickEffect
{
    public uint EffectId { get; set; }

public bool CheckTargetTagSrc { get; set; }

public bool CheckNoTargetTagSrc { get; set; }
    public uint TargetBuffTagId { get; set; }
    public uint TargetNoBuffTagId { get; set; }
}