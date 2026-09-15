using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Crime;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Skills.Effects;

/// <summary>
/// The client's evidence skills (11671 big bloodstain, 11673 small bloodstain, 15070 footprint)
/// carry this effect: using evidence reports the crime recorded on it.
/// </summary>
public class ReportCrimeEffect : EffectTemplate
{
    /// <summary>The client's own interact band, used when the reporting skill carries no range.</summary>
    private const float DefaultReportingRange = 3f;

    public int Value { get; set; }
    public uint CrimeKindId { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        var evidence = ResolveEvidence(caster, targetObj);
        if (evidence == null)
        {
            Logger.Debug($"ReportCrimeEffect: no evidence doodad on the cast (kind {CrimeKindId}, value {Value})");
            return;
        }

        // A cast at a unit is range-checked before its effects run; a doodad target is not, so the
        // report proves here that it comes from the scene the evidence is lying in. Without this a
        // client that knows a remote evidence id moves another player's crime points from anywhere.
        if (!IsInReportingRange(caster, evidence, source?.Skill?.Template?.MaxRange ?? 0))
        {
            Logger.Warn($"ReportCrimeEffect: evidence {evidence.ObjId} reported out of range " +
                        $"(caster {caster?.ObjId ?? 0}, kind {CrimeKindId}, value {Value})");
            return;
        }

        var criminalId = ResolveCriminalObjId(caster?.ObjId ?? 0, evidence.OwnerType, evidence.OwnerId);
        if (criminalId == 0)
        {
            // Covers a stray doodad, a world-owned one and a player poking their own evidence.
            Logger.Debug($"ReportCrimeEffect: evidence {evidence.ObjId} has nobody to report " +
                         $"(ownerType {evidence.OwnerType}, owner {evidence.OwnerId}, caster {caster?.ObjId ?? 0})");
            return;
        }

        Logger.Debug($"ReportCrimeEffect: evidence {evidence.ObjId} reports {criminalId} " +
                     $"(kind {CrimeKindId}, value {Value})");
        CrimeManager.Instance.AddCrimePoints(criminalId, (CrimeKind)CrimeKindId,
            (short)Math.Clamp(Value, short.MinValue, short.MaxValue));
    }

    /// <summary>
    /// The evidence the cast was aimed at. The client targets the evidence doodad, so the doodad's
    /// own object id - not the target unit, which never resolves for a doodad - names the crime scene.
    /// </summary>
    internal static Doodad ResolveEvidence(BaseUnit caster, SkillCastTarget targetObj)
    {
        if (targetObj is not SkillCastDoodadTarget doodadTarget || doodadTarget.ObjId == 0)
            return null;

        return caster?.ParentWorld?.GetDoodad(doodadTarget.ObjId);
    }

    /// <summary>
    /// The character the crime lands on: the character who owns the evidence. Returns 0 when the
    /// evidence has no player owner or when the caster is that owner - the same "reporting your own
    /// crime" case <c>CrimeManager.ReportCrime</c> already refuses.
    /// </summary>
    public static uint ResolveCriminalObjId(uint casterObjId, DoodadOwnerType ownerType, uint ownerId) =>
        ownerType == DoodadOwnerType.Character && ownerId != 0 && ownerId != casterObjId ? ownerId : 0;

    /// <summary>
    /// True when the caster is standing within the reporting skill's reach of the evidence. A skill
    /// that declares no range falls back to the interact band every other doodad use is held to.
    /// </summary>
    internal static bool IsInReportingRange(BaseUnit caster, Doodad evidence, int skillMaxRange)
    {
        if (caster?.Transform?.World == null || evidence?.Transform?.World == null)
            return false;

        var range = skillMaxRange > 0 ? skillMaxRange : DefaultReportingRange;
        return MathUtil.CalculateDistance(
            caster.Transform.World.Position,
            evidence.Transform.World.Position) <= range;
    }
}
