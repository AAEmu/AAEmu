using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class WorldMessageEffect : EffectTemplate
{
    public int FactionScopeId { get; set; }
    public string IconKey { get; set; }
    public bool KillHero { get; set; }
    public int KillStreakCount { get; set; }
    public string Message { get; set; }
    public bool ZoneGroupOnly { get; set; }
    public bool ZoneGroupWarState { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        Logger.Trace("WorldMessageEffect {0}", Id);
    }
}
