using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class ReportCrimeEffect : EffectTemplate
{
    public int Value { get; set; }
    public uint CrimeKindId { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        // The victim reports the caster, so the crime lands on whoever cast it.
        if (caster is not Char.Character criminal)
            return;

        var before = criminal.CrimePoint;
        // CrimePoint is a short and the client shows it out of 50; keep it in range so a repeated report
        // cannot wrap it negative and clear the player's record.
        criminal.CrimePoint = (short)Math.Clamp(criminal.CrimePoint + Value, 0, short.MaxValue);

        Logger.Debug($"ReportCrimeEffect: {criminal.Name} crime {before} -> {criminal.CrimePoint} (kind {CrimeKindId}, value {Value})");

        criminal.SendPacket(new Core.Packets.G2C.SCUnitStatePacket(criminal));
    }
}
