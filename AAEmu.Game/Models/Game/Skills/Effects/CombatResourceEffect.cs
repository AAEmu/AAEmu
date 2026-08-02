using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
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

        var before = unit.GetCombatResource(CombatResourceId);
        var after = unit.AddCombatResource(CombatResourceId, amount);

        Logger.Debug($"CombatResourceEffect: resource {CombatResourceId} on {unit.ObjId} {before} -> {after} (amount {amount}, resetRemainTime {ResetRemainTime})");

        // combat_resources.resouece_send_type_id decides the audience: 1 Self, 2 Broadcast.
        var resource = CombatResourceGameData.Instance.Get(CombatResourceId);
        var updateTime = ResetRemainTime ? 0 : (int)(DateTime.UtcNow - time).TotalMilliseconds;
        var packet = new SCCombatResourcePointPacket(unit.ObjId, CombatResourceId, (ulong)after, updateTime);

        if (resource is { SendTypeId: 2 })
            unit.BroadcastPacket(packet, true);
        else
            (unit as Char.Character)?.SendPacket(packet);
    }
}
