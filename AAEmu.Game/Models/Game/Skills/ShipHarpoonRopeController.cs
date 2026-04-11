using System.Numerics;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using NLog;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>Server-side harpoon rope lifecycle (Launch 13749, Cut 13750, CSSkillControllerState).</summary>
public static class ShipHarpoonRopeController
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public static void OnLaunchSucceeded(Slave harpoonSlave, SkillCastTarget target)
    {
        if (!TryGetHookWorld(target, out var hook))
            return;

        harpoonSlave.HarpoonRope.Clear();
        var origin = harpoonSlave.Transform.World.Position;
        var initialLen = Vector3.Distance(origin, hook);

        harpoonSlave.HarpoonRope.IsEngaged = true;
        harpoonSlave.HarpoonRope.HookWorld = hook;
        harpoonSlave.HarpoonRope.RopeLength = initialLen;
        harpoonSlave.HarpoonRope.LastTeared = false;
        harpoonSlave.HarpoonRope.LastCutout = false;

        Log.Debug("Harpoon rope engaged: slaveObjId={0} hook=({1:F1},{2:F1},{3:F1}) initialLen={4:F2}",
            harpoonSlave.ObjId, hook.X, hook.Y, hook.Z, initialLen);
    }

    public static void OnCutRope(Slave harpoonSlave)
    {
        var hadRope = harpoonSlave.HarpoonRope.IsEngaged;
        harpoonSlave.HarpoonRope.Clear();
        if (hadRope)
            Log.Debug("Harpoon rope cleared (cut): slaveObjId={0}", harpoonSlave.ObjId);
    }

    public static void TryApplySkillControllerState(Character character, uint objId, float len, bool teared, bool cutouted)
    {
        if (character?.ParentWorld == null)
            return;

        if (character.ParentWorld.GetUnit(objId) is not Slave slave)
            return;

        if (!IsCharacterAttachedToSlave(character, slave))
            return;

        if (!slave.HarpoonRope.IsEngaged)
            return;

        slave.HarpoonRope.RopeLength = len;
        slave.HarpoonRope.LastTeared = teared;
        slave.HarpoonRope.LastCutout = cutouted;

        if (teared || cutouted)
            slave.HarpoonRope.Clear();
    }

    private static bool IsCharacterAttachedToSlave(Character character, Slave slave)
    {
        foreach (var c in slave.AttachedCharacters.Values)
        {
            if (c != null && c.ObjId == character.ObjId)
                return true;
        }

        return false;
    }

    private static bool TryGetHookWorld(SkillCastTarget target, out Vector3 hook)
    {
        switch (target)
        {
            case SkillCastPositionTarget p:
                hook = new Vector3(p.PosX, p.PosY, p.PosZ);
                return true;
            case SkillCastPosition2Target p2:
                hook = new Vector3(p2.PosX, p2.PosY, p2.PosZ);
                return true;
            case SkillCastPosition3Target p3:
                hook = new Vector3(p3.PosX, p3.PosY, p3.PosZ);
                return true;
            default:
                hook = default;
                return false;
        }
    }
}
