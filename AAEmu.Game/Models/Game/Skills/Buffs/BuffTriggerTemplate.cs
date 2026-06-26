using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.Skills.Buffs;

public class BuffTriggerTemplate
{
    public uint Id { get; set; }
    public BuffEventTriggerKind Kind { get; set; }
    public EffectTemplate Effect { get; set; }
    public bool UseDamageAmount { get; set; }
    public uint TargetBuffTagId { get; set; }
    public uint TargetNoBuffTagId { get; set; }
}
