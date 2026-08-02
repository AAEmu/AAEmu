using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class CinemalEffect : EffectTemplate
{
    public uint CinemaId { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        if (caster is not Char.Character character)
            return;

        Logger.Debug($"CinemaEffect: cinema {CinemaId} for {character.Name}");

        // the CS side reporting back (CSStartedCinema 0x111, CSCompletedCinema 0x110). The client reads the
        // cutscene to play from its own copy of the skill's effects, so the server's part is to record which
        // one is running — CSStartedCinema and CSCompletedCinema both read CurrentlyPlayingCinemaId back out
        // when they raise their events, and without this they only ever saw zero.
        character.CurrentlyPlayingCinemaId = CinemaId;
    }
}
