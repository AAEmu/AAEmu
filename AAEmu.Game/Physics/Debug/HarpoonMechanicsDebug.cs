using AAEmu.Game.Models.Game.Skills;
using NLog;

namespace AAEmu.Game.Physics.Debug;

/// <summary>
/// Debug hooks only for ship harpoon skills (Launch Harpoon / Cut Harpoon Rope). Skill IDs from compact.skills + localized_texts (dumpsql).
/// Rope length / controller traffic uses shared packets — see <see cref="AAEmu.Game.Core.Packets.Debug.SkillControllerPacketDebug"/>.
/// </summary>
public static class HarpoonMechanicsDebug
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// When true, emits harpoon-specific NLog Debug (CSStartSkill payload, rope engage/break in <see cref="AAEmu.Game.Models.Game.Skills.SkillControllers.ShipHarpoonRopeController"/>).
    /// Default is off. Shared skill-controller packet lines use <see cref="AAEmu.Game.Core.Packets.Debug.SkillControllerPacketDebug.EnableVerbosePacketLogging"/> (also default off).
    /// </summary>
    public static bool EnableVerboseHarpoonMechanicsLogging;

    /// <summary>Ship-mounted Launch Harpoon (EN localized name).</summary>
    public const uint ShipLaunchHarpoonSkillId = 13749;

    /// <summary>Ship-mounted Cut Harpoon Rope (EN localized name).</summary>
    public const uint ShipCutHarpoonRopeSkillId = 13750;

    public static bool IsShipHarpoonSkill(uint skillId) =>
        skillId is ShipLaunchHarpoonSkillId or ShipCutHarpoonRopeSkillId;

    public static void LogCsStartSkillIfHarpoon(uint skillId, byte flag, int flagType, SkillCaster caster, SkillCastTarget target, SkillObject skillObject)
    {
        if (!EnableVerboseHarpoonMechanicsLogging || !IsShipHarpoonSkill(skillId))
            return;

        // NLog.config: console uses minlevel Debug — Trace would not appear in the terminal.
        Log.Debug("[Harpoon][C2S CSStartSkill 0x052] skillId={0} flag=0x{1:X2} flagTypeSkillObject={2} caster={3} target={4} skillObjectFlag={5}",
            skillId, flag, flagType, FormatCaster(caster), FormatTarget(target), skillObject.Flag);
    }

    private static string FormatCaster(SkillCaster c)
    {
        return c switch
        {
            SkillCasterMount m => $"Mount(objId={m.ObjId}, mountSkillTemplateId={m.MountSkillTemplateId})",
            SkillCasterUnit u => $"Unit(objId={u.ObjId})",
            SkillItem i => $"Item(objId={i.ObjId}, itemTemplateId={i.ItemTemplateId})",
            SkillDoodad d => $"Doodad(objId={d.ObjId})",
            SkillCasterUnk1 u1 => $"Unk1(objId={u1.ObjId})",
            _ => $"{c.Type}(objId={c.ObjId})"
        };
    }

    private static string FormatTarget(SkillCastTarget t)
    {
        return t switch
        {
            SkillCastPositionTarget p => $"Position(x={p.PosX}, y={p.PosY}, z={p.PosZ}, rot={p.PosRot}, objId1={p.ObjId1}, objId2={p.ObjId2})",
            SkillCastPosition2Target p2 => $"Position2(start=({p2.PosX},{p2.PosY},{p2.PosZ}) end=({p2.EndPosX},{p2.EndPosY},{p2.EndPosZ}) norm=({p2.NormX},{p2.NormY},{p2.NormZ}))",
            SkillCastPosition3Target p3 => $"Position3(x={p3.PosX}, y={p3.PosY}, z={p3.PosZ}, pitch={p3.Pitch})",
            SkillCastUnitTarget u => $"Unit(objId={u.ObjId})",
            SkillCastItemTarget i => $"Item(objId={i.ObjId}, id={i.Id})",
            SkillCastDoodadTarget dd => $"Doodad(objId={dd.ObjId})",
            _ => $"{t.Type}(objId={t.ObjId})"
        };
    }
}
