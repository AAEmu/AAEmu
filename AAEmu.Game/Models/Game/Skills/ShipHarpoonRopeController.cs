using System.Collections.Generic;
using System.Numerics;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using NLog;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>Server-side harpoon rope lifecycle (Launch 13749, Cut 13750, CSSkillControllerState).</summary>
public static class ShipHarpoonRopeController
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public static void OnLaunchSucceeded(Slave harpoonSlave, SkillCastTarget target, Character? operatorChar)
    {
        if (!TryGetHookWorld(target, out var hook))
            return;

        if (harpoonSlave.HarpoonRope.IsEngaged)
            BreakRopeForClients(harpoonSlave, cutouted: false, operatorChar);
        else
            harpoonSlave.HarpoonRope.Clear();

        var launchTemplate = SkillManager.Instance.GetSkillTemplate(HarpoonMechanicsDebug.ShipLaunchHarpoonSkillId);
        var maxRange = launchTemplate != null ? Math.Max(0f, launchTemplate.MaxRange) : 0f;

        var origin = harpoonSlave.Transform.World.Position;
        var initialLen = Vector3.Distance(origin, hook);

        harpoonSlave.HarpoonRope.IsEngaged = true;
        harpoonSlave.HarpoonRope.HookWorld = hook;
        harpoonSlave.HarpoonRope.RopeLength = initialLen;
        harpoonSlave.HarpoonRope.MaxLaunchRange = maxRange;
        harpoonSlave.HarpoonRope.LastTeared = false;
        harpoonSlave.HarpoonRope.LastCutout = false;

        Log.Debug("Harpoon rope engaged: slaveObjId={0} hook=({1:F1},{2:F1},{3:F1}) initialLen={4:F2} maxRange={5:F1}",
            harpoonSlave.ObjId, hook.X, hook.Y, hook.Z, initialLen, maxRange);
    }

    public static void OnCutRope(Slave harpoonSlave, Character? operatorChar)
    {
        BreakRopeForClients(harpoonSlave, cutouted: true, operatorChar);
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

        if (TryBreakRopeIfHookOutOfRange(slave, character))
            return;

        if (teared || cutouted)
            slave.HarpoonRope.Clear();
    }

    /// <summary>When the operator leaves this slave seat (harpoon station), drop the line per game design.</summary>
    public static void OnOperatorLeftSlave(Slave slave, Character? leavingOperator)
    {
        BreakRopeForClients(slave, cutouted: false, leavingOperator);
    }

    /// <summary>Clears server rope state and mirrors break to clients (skill controller UI).</summary>
    public static void BreakRopeForClients(Slave slave, bool cutouted, Character? alsoNotify = null)
    {
        if (slave?.HarpoonRope.IsEngaged != true)
            return;

        var len = slave.HarpoonRope.RopeLength;
        var objId = slave.ObjId;
        slave.HarpoonRope.Clear();

        var pkt = new SCSkillControllerStatePacket(objId, 0, len, teared: true, cutouted);
        var sent = new HashSet<uint>();
        foreach (var ch in slave.AttachedCharacters.Values)
        {
            if (ch?.Connection == null)
                continue;
            if (!sent.Add(ch.ObjId))
                continue;
            ch.SendPacket(pkt);
        }

        if (slave.Summoner?.Connection != null && sent.Add(slave.Summoner.ObjId))
            slave.Summoner.SendPacket(pkt);

        if (alsoNotify?.Connection != null && sent.Add(alsoNotify.ObjId))
            alsoNotify.SendPacket(pkt);

        Log.Debug("Harpoon rope server break + SCSkillControllerState: slaveObjId={0} len={1:F2} cutouted={2}",
            objId, len, cutouted);
    }

    private static bool TryBreakRopeIfHookOutOfRange(Slave slave, Character? alsoNotify)
    {
        if (!slave.HarpoonRope.IsEngaged || slave.HarpoonRope.MaxLaunchRange <= 0f)
            return false;

        var dist = Vector3.Distance(slave.Transform.World.Position, slave.HarpoonRope.HookWorld);
        const float margin = 1.5f;
        if (dist <= slave.HarpoonRope.MaxLaunchRange + margin)
            return false;

        var maxSaved = slave.HarpoonRope.MaxLaunchRange;
        Log.Debug("Harpoon rope auto-break (hook beyond range): slaveObjId={0} dist={1:F2} max={2:F2}",
            slave.ObjId, dist, maxSaved);
        BreakRopeForClients(slave, cutouted: false, alsoNotify);
        return true;
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
