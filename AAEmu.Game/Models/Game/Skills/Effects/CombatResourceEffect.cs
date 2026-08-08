using AAEmu.Game.Core.Packets;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

// combat_resource_effects — grants/consumes a v10 combat resource (combo-point style resource) on the unit.
public class CombatResourceEffect : EffectTemplate
{
    public int MinCombatResource { get; set; }
    public int MaxCombatResource { get; set; }
    public int CombatResourceId { get; set; }
    public int Chance { get; set; }
    public bool ResetRemainTime { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        if (target is not Unit unit)
            return;

        var resourceId = ResolveCombatResourceId(source);
        if (resourceId == 0)
            return;

        // chance 0 is "always" throughout the shipped rows, not "never" — every combat_resource_effects row
        // carries 0 and these are the builders an ability relies on, so a literal reading would make the whole
        // system dead. Anything above zero rolls out of 100.
        if (Chance > 0 && Random.Shared.Next(100) >= Chance)
            return;

        var amount = MinCombatResource == MaxCombatResource
            ? MinCombatResource
            : Random.Shared.Next(Math.Min(MinCombatResource, MaxCombatResource), Math.Max(MinCombatResource, MaxCombatResource) + 1);

        if (amount == 0)
            return;

        var before = unit.GetCombatResource(resourceId);
        var after = unit.AddCombatResource(resourceId, amount, ResetRemainTime);

        Logger.Debug($"CombatResourceEffect: resource {resourceId} on {unit.ObjId} {before} -> {after} (amount {amount}, resetRemainTime {ResetRemainTime})");

        // combat_resources.resouece_send_type_id decides the audience: 1 Self, 2 Broadcast.
        var resource = CombatResourceGameData.Instance.Get(resourceId);
        var updateTime = ResetRemainTime ? 0 : (int)(DateTime.UtcNow - time).TotalMilliseconds;
        unit.BroadcastCombatResource(resource, after, updateTime);
    }

    /// <summary>
    /// Resolves which pool this effect feeds.
    /// </summary>
    /// <remarks>
    /// <c>combat_resource_effects.combat_resource_id</c> is 0 on 51 plot effects and 20 skill effects, and
    /// combat_resources has no id 0 — those rows mean "the resource the casting skill owns", named by
    /// <c>skills.combat_resource_id</c>. Their amounts confirm it: the id-0 rows carry ±300…±600, which fits
    /// no pool with a ceiling of 3–60 but matches 근성 (id 3, max 5000) exactly, and 근성 is what all 12
    /// skills carrying a non-zero skills.combat_resource_id point at. Writing them to bucket 0 instead put
    /// the whole grant somewhere nothing ever reads.
    /// </remarks>
    private int ResolveCombatResourceId(EffectSource source)
    {
        if (CombatResourceId != 0)
            return CombatResourceId;

        var fromSkill = source?.Skill?.Template?.CombatResourceId ?? 0;
        if (fromSkill != 0)
            return fromSkill;

        Logger.Debug("CombatResourceEffect: combat_resource_id 0 and skill {0} names no resource — skipped",
            source?.Skill?.Template?.Id ?? 0);
        return 0;
    }
}
