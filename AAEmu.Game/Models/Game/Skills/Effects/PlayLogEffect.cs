using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

// play_log_effects — a play-log/analytics marker fired by a skill. No server-side state change.
public class PlayLogEffect : EffectTemplate
{
    public string Message { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        // A play-log marker is exactly this: a record that a skill reached this point. There is no server
        // state behind it and nothing goes to the client, so emitting the line is the whole behaviour.
        var who = (caster as Char.Character)?.Name ?? caster?.ObjId.ToString() ?? "unknown";
        Logger.Info($"PlayLog: {Message} (skill {source?.Skill?.Id}, caster {who})");
    }
}
