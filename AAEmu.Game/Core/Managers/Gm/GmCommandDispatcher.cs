using System.Drawing;
using System.Globalization;
using System.Linq;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Teleport;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Static;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Utils;
using NLog;

using WorldIntegration = AAEmu.Game.WorldIntegration;

namespace AAEmu.Game.Core.Managers.Gm;

/// <summary>
/// Dispatches retail <c>CSGmCommand</c> (X2Gm / gm_console).
/// Wire: bc unitId = resolved target/self; params = CRLF-separated Data blob (no target name).
/// Zone-native cmds are relayed; everything else is handled on World.
/// </summary>
public static class GmCommandDispatcher
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>Dedicate OnGmCommand switch — only these do anything on Zone.</summary>
    private static readonly HashSet<ushort> ZoneNative =
    [
        (ushort)GmCommandId.SetAttribute,
        (ushort)GmCommandId.ClearAttribute,
        (ushort)GmCommandId.ChangeMode,
        (ushort)GmCommandId.UseSkill,
        (ushort)GmCommandId.SearchNpc,
        (ushort)GmCommandId.ReturnNpc,
        (ushort)GmCommandId.MoveNpc,
    ];

    public static void Handle(Character me, uint unitId, ushort cmd, string parameters)
    {
        var args = SplitParams(parameters);
        var name = ((GmCommandId)cmd).ToString();
        if (!Enum.IsDefined(typeof(GmCommandId), cmd))
            name = $"cmd{cmd}";

        Logger.Info("GM {0} → {1}({2}) unit={3} args=[{4}]",
            me.Name, name, cmd, unitId, string.Join("|", args));

        try
        {
            var feedback = Dispatch(me, unitId, cmd, args, parameters);
            me.SendPacket(new SCGmCommandPacket(unitId, (byte)cmd, 100, parameters, feedback));
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "GM {0} cmd={1} failed", me.Name, cmd);
            me.SendPacket(new SCGmCommandPacket(unitId, (byte)cmd, 100, parameters, $"error: {ex.Message}"));
        }
    }

    private static string Dispatch(Character me, uint unitId, ushort cmd, string[] args, string raw)
    {
        // Already handled specially by packet for Notice / GoTo / Summon historically —
        // keep them here so one path owns everything.
        return cmd switch
        {
            (ushort)GmCommandId.SetHealth => SetHealth(me, unitId, args),
            (ushort)GmCommandId.SetMana => SetMana(me, unitId, args),
            (ushort)GmCommandId.ResetSkillCooldown => ResetCd(me, unitId),
            (ushort)GmCommandId.ChangeFaction => ChangeFaction(me, unitId, args),
            (ushort)GmCommandId.SpawnNpc => SpawnNpc(me, args),
            (ushort)GmCommandId.SpawnSlave => SpawnSlave(me, args),
            (ushort)GmCommandId.SpawnMate => SpawnMate(me, args),
            (ushort)GmCommandId.SpawnDoodad => SpawnDoodad(me, args),
            (ushort)GmCommandId.SpawnGimmick => SpawnGimmick(me, args),
            (ushort)GmCommandId.DumpChar => DumpChar(me, args),
            (ushort)GmCommandId.DumpCharBag => DumpBag(me, args, bank: false),
            (ushort)GmCommandId.DumpCharBank => DumpBag(me, args, bank: true),
            (ushort)GmCommandId.DumpCharEquipment => DumpEquipment(me, args),
            (ushort)GmCommandId.DumpCharParty => DumpParty(me, args),
            (ushort)GmCommandId.DumpCharQuests => DumpQuests(me, args),
            (ushort)GmCommandId.GoTo => GoTo(me, args),
            (ushort)GmCommandId.AddLaborPower => AddLabor(me, unitId, args),
            (ushort)GmCommandId.AddExp => AddExp(me, unitId, args),
            (ushort)GmCommandId.SetExpFactor => SetExpFactor(me, args),
            (ushort)GmCommandId.AddMoney => AddMoney(me, unitId, args),
            (ushort)GmCommandId.GiveNewItem => GiveItem(me, unitId, args),
            (ushort)GmCommandId.SetBuff => SetBuff(me, unitId, args),
            (ushort)GmCommandId.ClearBuff => ClearBuff(me, unitId, args),
            (ushort)GmCommandId.ClearBuffs => ClearBuffs(me, unitId),
            (ushort)GmCommandId.SetInvisible => SetInvisible(me, unitId, args),
            (ushort)GmCommandId.Summon => Summon(me, args),
            (ushort)GmCommandId.Notice => Notice(me, raw),
            (ushort)GmCommandId.Kick => Kick(me, args),
            (ushort)GmCommandId.Resurrect => Resurrect(me, unitId),
            (ushort)GmCommandId.RemoveAllItems => RemoveAllItems(me, unitId),
            (ushort)GmCommandId.Freeze => Freeze(me, args, true),
            (ushort)GmCommandId.UnFreeze => Freeze(me, args, false),
            (ushort)GmCommandId.RemoveSlave => RemoveSlave(me, unitId),
            (ushort)GmCommandId.RemoveMate => RemoveMate(me, unitId),
            (ushort)GmCommandId.AddCash => AddCash(me, unitId, args),
            (ushort)GmCommandId.CheckZone => $"zone={me.Transform.ZoneId} world={me.ParentWorld?.Template?.Name}",
            (ushort)GmCommandId.Return => ReturnHome(me, unitId),
            (ushort)GmCommandId.SetCrimeValue => SetCrime(me, unitId, args),
            (ushort)GmCommandId.TowerDef => TowerDef(me, args),
            // Runs the Hero Qualification Evaluation now instead of waiting for the next 12-hour boundary.
            (ushort)GmCommandId.DailyResetReputation => ReputationManager.Instance.Evaluate(),
            (ushort)GmCommandId.Attach or (ushort)GmCommandId.Detach
                => $"ok: {((GmCommandId)cmd)} (client/vehicle attach — no World action)",
            (ushort)GmCommandId.PlaySequence
                => $"ok: PlaySequence {args.ElementAtOrDefault(0)} (cutscene client-side)",
            (ushort)GmCommandId.DumpInstantGame or (ushort)GmCommandId.DumpGameRule
                or (ushort)GmCommandId.DumpIndun or (ushort)GmCommandId.DebugLooting
                => DumpChar(me, args),
            (ushort)GmCommandId.RecoverHouses or (ushort)GmCommandId.ResetHouses
                or (ushort)GmCommandId.DemolishHouse or (ushort)GmCommandId.SetHousePermission
                or (ushort)GmCommandId.ResendHouseTaxMail or (ushort)GmCommandId.DelayTaxDueDate
                => $"stub: {((GmCommandId)cmd)} — housing GM pending",
            (ushort)GmCommandId.ScheduleSiege or (ushort)GmCommandId.EnablePirates
                or (ushort)GmCommandId.DeleteDominion or (ushort)GmCommandId.EndInstantGameJoined
                or (ushort)GmCommandId.SetCongestion or (ushort)GmCommandId.SetInstantGmEventMode
                or (ushort)GmCommandId.SetTradeStatus or (ushort)GmCommandId.GmOneAndOneChat
                or (ushort)GmCommandId.CheckBotPlayer or (ushort)GmCommandId.UpdateLeadership
                or (ushort)GmCommandId.UpdateHeroScore
                or (ushort)GmCommandId.AddActionPoint or (ushort)GmCommandId.SetDoodadGrowth
                or (ushort)GmCommandId.RecoverDoodad or (ushort)GmCommandId.MoveCharRezPoint
                => $"stub: {((GmCommandId)cmd)} — acknowledged, not simulated yet",
            // Zone-native: also mirror World-side side effects where useful
            (ushort)GmCommandId.ChangeMode => ChangeMode(me, unitId, args, raw),
            (ushort)GmCommandId.SetAttribute or (ushort)GmCommandId.ClearAttribute
                or (ushort)GmCommandId.UseSkill or (ushort)GmCommandId.SearchNpc
                or (ushort)GmCommandId.ReturnNpc or (ushort)GmCommandId.MoveNpc
                => RelayZone(unitId, cmd, raw),
            _ => UnimplementedOrRelay(me, unitId, cmd, raw),
        };
    }

    private static string SetCrime(Character me, uint unitId, string[] args)
    {
        var ch = ResolveChar(me, unitId, []);
        if (!TryInt(args, 0, out var crime))
            return "need crime value";
        ch.CrimePoint = (short)Math.Clamp(crime, short.MinValue, short.MaxValue);
        return $"{ch.Name} crime → {ch.CrimePoint}";
    }

    private static string TowerDef(Character me, string[] args)
    {
        // Shared cmd id for list/reload/start/stop — params distinguish; chat-confirm for now.
        me.SendMessage($"[GM] TowerDef args=[{string.Join(",", args)}] — use /towerdef for full control");
        return $"TowerDef noted ({args.Length} args)";
    }

    private static string UnimplementedOrRelay(Character me, uint unitId, ushort cmd, string raw)
    {
        // Never blind-relay unknown cmds — Zone ignores them and it hides bugs.
        var name = Enum.IsDefined(typeof(GmCommandId), cmd) ? ((GmCommandId)cmd).ToString() : $"cmd{cmd}";
        Logger.Warn("GM {0}: {1} not implemented yet", me.Name, name);
        return $"unimplemented: {name}";
    }

    private static string RelayZone(uint unitId, ushort cmd, string raw)
    {
        var ok = WorldIntegration.RelayGmCommandToZone?.Invoke(unitId, cmd, raw) ?? false;
        return ok ? "relayed to zone" : "zone relay failed (no zone for unit?)";
    }

    #region helpers

    private static string[] SplitParams(string parameters)
    {
        if (string.IsNullOrWhiteSpace(parameters))
            return [];
        return parameters
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static Character ResolveChar(Character me, uint unitId, string[] args, int nameArgIndex = -1)
    {
        if (nameArgIndex >= 0 && nameArgIndex < args.Length && !string.IsNullOrWhiteSpace(args[nameArgIndex]))
        {
            var byName = WorldManager.Instance.GetCharacter(args[nameArgIndex]);
            if (byName != null)
                return byName;
        }

        var byObj = WorldManager.Instance.GetCharacterByObjId(unitId);
        if (byObj != null)
            return byObj;

        if (WorldIntegration.FindUnitAcrossWorlds(unitId) is Character c)
            return c;

        return me;
    }

    private static Unit ResolveUnit(Character me, uint unitId)
    {
        if (WorldIntegration.FindUnitAcrossWorlds(unitId) is Unit u)
            return u;
        return me;
    }

    private static bool TryUInt(string[] args, int i, out uint v)
    {
        v = 0;
        return i < args.Length && uint.TryParse(args[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
    }

    private static bool TryInt(string[] args, int i, out int v)
    {
        v = 0;
        return i < args.Length && int.TryParse(args[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
    }

    private static bool TryBool(string s, out bool v)
    {
        if (bool.TryParse(s, out v))
            return true;
        if (s is "1" or "true" or "True" or "yes")
        {
            v = true;
            return true;
        }

        if (s is "0" or "false" or "False" or "no")
        {
            v = false;
            return true;
        }

        v = false;
        return false;
    }

    #endregion

    #region combat / vitals

    private static string SetHealth(Character me, uint unitId, string[] args)
    {
        var unit = ResolveUnit(me, unitId);
        if (unit.Hp <= 0)
            return "target is dead — use Resurrect";
        if (!TryInt(args, 0, out var hp))
            return "need health value";
        if (hp <= 0)
            hp = unit.MaxHp;
        var old = unit.Hp;
        unit.Hp = Math.Clamp(hp, 1, unit.MaxHp);
        unit.BroadcastPacket(new SCUnitPointsPacket(unit.ObjId, unit.Hp, unit.Mp), true);
        unit.PostUpdateCurrentHp(unit, old, unit.Hp, KillReason.Unknown);
        return $"{unit.Name} HP {unit.Hp}/{unit.MaxHp}";
    }

    private static string SetMana(Character me, uint unitId, string[] args)
    {
        var unit = ResolveUnit(me, unitId);
        if (!TryInt(args, 0, out var mp))
            return "need mana value";
        if (mp <= 0)
            mp = unit.MaxMp;
        unit.Mp = Math.Clamp(mp, 0, unit.MaxMp);
        unit.BroadcastPacket(new SCUnitPointsPacket(unit.ObjId, unit.Hp, unit.Mp), true);
        return $"{unit.Name} MP {unit.Mp}/{unit.MaxMp}";
    }

    private static string ResetCd(Character me, uint unitId)
    {
        var ch = ResolveChar(me, unitId, []);
        ch.ResetAllSkillCooldowns(false);
        return $"reset cooldowns for {ch.Name}";
    }

    private static string ChangeFaction(Character me, uint unitId, string[] args)
    {
        var unit = ResolveUnit(me, unitId);
        if (!TryUInt(args, 0, out var factionId) || !Enum.IsDefined(typeof(FactionsEnum), (byte)factionId))
            return "need faction id";
        var faction = (FactionsEnum)factionId;
        if (FactionManager.Instance.GetFaction(faction) == null)
            return $"unknown faction {factionId}";
        unit.SetFaction(faction);
        return $"{unit.Name} faction → {faction} ({factionId})";
    }

    private static string Resurrect(Character me, uint unitId)
    {
        var ch = ResolveChar(me, unitId, []);
        if (ch.Hp > 0)
            return $"{ch.Name} already alive";
        ch.Hp = ch.MaxHp;
        ch.Mp = ch.MaxMp;
        ch.BroadcastPacket(new SCCharacterResurrectedPacket(ch.ObjId,
            ch.Transform.World.Position.X, ch.Transform.World.Position.Y, ch.Transform.World.Position.Z,
            ch.Transform.World.Rotation.Z), true);
        ch.BroadcastPacket(new SCUnitPointsPacket(ch.ObjId, ch.Hp, ch.Mp), true);
        ch.PostUpdateCurrentHp(ch, 0, ch.Hp, KillReason.Unknown);
        if (WorldIntegration.ZoneAuthority)
        {
            var p = ch.Transform.World.Position;
            WorldIntegration.RelayUnitResurrectionToZone?.Invoke(
                ch.ObjId, p.X, p.Y, p.Z, ch.Transform.World.Rotation.Z);
            WorldIntegration.RelayUnitPointsToZone?.Invoke(ch.ObjId, ch.Hp, ch.Mp);
        }
        return $"resurrected {ch.Name}";
    }

    #endregion

    #region spawn

    private static string SpawnNpc(Character me, string[] args)
    {
        if (!TryUInt(args, 0, out var npcType) || !NpcManager.Instance.Exist(npcType))
            return $"bad npcType {args.ElementAtOrDefault(0)}";
        using var charPos = me.Transform.CloneDetached();
        charPos.Local.AddDistanceToFront(3f);
        var angle = ((float)MathUtil.CalculateAngleFrom(charPos, me.Transform)).DegToRad();

        if (WorldIntegration.ZoneAuthority)
        {
            var npc = NpcManager.Instance.Create(me.ParentWorld, 0, npcType);
            if (npc == null)
                return $"failed to create npc {npcType}";

            npc.Transform = charPos.CloneDetached(npc);
            npc.Transform.Local.SetZRotation(angle);
            npc.IsZoneMirror = true;
            npc.Spawn();
            if (WorldIntegration.PublishNpcSpawn(npc))
                return $"spawned npc {npcType}";

            WorldIntegration.DeleteNpcMirror(npc, false);
            return $"no loaded Zone owns the spawn position for npc {npcType}";
        }

        var spawner = new NpcSpawner
        {
            ParentWorld = me.ParentWorld,
            Id = 0,
            UnitId = npcType,
            Position = charPos.CloneAsSpawnPosition()
        };
        spawner.Position.Yaw = angle;
        me.ParentWorld.SpawnManager.AddNpcSpawner(spawner);
        spawner.SpawnAll();
        return $"spawned npc {npcType}";
    }

    private static string SpawnDoodad(Character me, string[] args)
    {
        if (!TryUInt(args, 0, out var doodadType) || !DoodadManager.Instance.Exist(doodadType))
            return $"bad doodadType {args.ElementAtOrDefault(0)}";
        using var charPos = me.Transform.CloneDetached();
        charPos.Local.AddDistanceToFront(3f);
        var spawner = new DoodadSpawner
        {
            ParentWorld = me.ParentWorld,
            Id = 0,
            UnitId = doodadType,
            Position = charPos.CloneAsSpawnPosition()
        };
        var angle = ((float)MathUtil.CalculateAngleFrom(charPos, me.Transform)).DegToRad();
        spawner.Position.Yaw = angle;
        spawner.Spawn(0, 0, me.ObjId);
        return $"spawned doodad {doodadType}";
    }

    private static string SpawnSlave(Character me, string[] args)
    {
        if (!TryUInt(args, 0, out var slaveType) || !SlaveGameData.Instance.Exist(slaveType))
            return $"bad slaveType {args.ElementAtOrDefault(0)}";
        me.ParentWorld.SlaveManager.Create(me, null, slaveType);
        return $"spawned slave {slaveType}";
    }

    private static string SpawnMate(Character me, string[] args)
    {
        // Mate spawn needs an item template path; treat as NPC pet template for GM convenience.
        if (!TryUInt(args, 0, out var npcType))
            return "need mate/npc template id";
        return SpawnNpc(me, [npcType.ToString()]);
    }

    private static string SpawnGimmick(Character me, string[] args)
    {
        if (!TryUInt(args, 0, out var gimmickType))
            return "need gimmickType";
        // World owns gimmick tick; use existing spawn effect path when available.
        var ok = me.ParentWorld?.GimmickManager != null;
        if (!ok)
            return "no gimmick manager";
        try
        {
            // Fall back to logging + chat — full gimmick spawn needs SpawnGimmickEffect.
            me.SendMessage($"[GM] SpawnGimmick {gimmickType} — use /gimmick if available");
            return $"gimmick {gimmickType}: use /gimmick spawn (full path pending)";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private static string RemoveSlave(Character me, uint unitId)
    {
        var target = ResolveUnit(me, unitId);
        me.ParentWorld?.SlaveManager?.RemoveActiveSlave(me, 0, false);
        return $"remove slave near {target.Name}";
    }

    private static string RemoveMate(Character me, uint unitId)
    {
        var ch = ResolveChar(me, unitId, []);
        ch.ParentWorld?.MateManager?.RemoveAndDespawnAllActiveOwnedMates(ch);
        return $"removed mates for {ch.Name}";
    }

    #endregion

    #region economy / progress

    private static string GiveItem(Character me, uint unitId, string[] args)
    {
        var ch = ResolveChar(me, unitId, []);
        if (!TryUInt(args, 0, out var itemType))
            return "need itemType";
        var count = TryInt(args, 1, out var c) ? Math.Max(1, c) : 1;
        if (!ch.Inventory.Bag.AcquireDefaultItem(ItemTaskType.Gm, itemType, count))
            return $"failed to give item {itemType} x{count} (full bag?)";
        return $"gave {itemType} x{count} to {ch.Name}";
    }

    private static string RemoveAllItems(Character me, uint unitId)
    {
        var ch = ResolveChar(me, unitId, []);
        ch.Inventory.Bag.Wipe();
        return $"cleared bag for {ch.Name}";
    }

    private static string AddMoney(Character me, uint unitId, string[] args)
    {
        var ch = ResolveChar(me, unitId, []);
        if (!TryInt(args, 0, out var money))
            return "need money (copper)";
        ch.Money += money;
        ch.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.Gm, [new MoneyChange(money)], []));
        return $"{ch.Name} money Δ{money} → {ch.Money}";
    }

    private static string AddExp(Character me, uint unitId, string[] args)
    {
        var ch = ResolveChar(me, unitId, []);
        if (!TryInt(args, 0, out var exp))
            return "need exp";
        ch.AddExp(exp, true);
        return $"{ch.Name} +{exp} exp (lvl {ch.Level})";
    }

    private static string AddLabor(Character me, uint unitId, string[] args)
    {
        var ch = ResolveChar(me, unitId, []);
        if (!TryInt(args, 0, out var labor))
            return "need labor";
        ch.ChangeLabor((short)Math.Clamp(labor, short.MinValue, short.MaxValue), 0);
        return $"{ch.Name} labor Δ{labor}";
    }

    private static string SetExpFactor(Character me, string[] args)
    {
        if (!TryInt(args, 0, out var factor))
            return "need factor";
        me.SetExpRate(factor);
        return $"ExpRate → {factor}";
    }

    private static string AddCash(Character me, uint unitId, string[] args)
    {
        var ch = ResolveChar(me, unitId, []);
        // AddCash(a,b) — credits amount is first int in retail dumps
        if (!TryInt(args, 0, out var amount))
            return "need cash amount";
        AccountManager.Instance.AddCredits(ch.AccountId, amount);
        return $"{ch.Name} credits Δ{amount}";
    }

    #endregion

    #region buffs / modes

    private static string SetBuff(Character me, uint unitId, string[] args)
    {
        var unit = ResolveUnit(me, unitId);
        if (!TryUInt(args, 0, out var buffType))
            return "need buffType";
        unit.Buffs.AddBuff(buffType, me);
        return $"{unit.Name} +buff {buffType}";
    }

    private static string ClearBuff(Character me, uint unitId, string[] args)
    {
        var unit = ResolveUnit(me, unitId);
        if (!TryUInt(args, 0, out var buffType))
            return "need buffType";
        unit.Buffs.RemoveBuff(buffType);
        return $"{unit.Name} -buff {buffType}";
    }

    private static string ClearBuffs(Character me, uint unitId)
    {
        var unit = ResolveUnit(me, unitId);
        unit.Buffs.RemoveAllEffects();
        return $"{unit.Name} cleared buffs";
    }

    private static string SetInvisible(Character me, uint unitId, string[] args)
    {
        var ch = ResolveChar(me, unitId, []);
        var on = args.Length == 0 || !TryBool(args[0], out var b) || b;
        ch.SetInvisible(on);
        return $"{ch.Name} invisible={on}";
    }

    private static string ChangeMode(Character me, uint unitId, string[] args, string raw)
    {
        // params: modeName\r\nval  — also mirror invincible/almighty on World
        var mode = args.ElementAtOrDefault(0)?.ToLowerInvariant() ?? "";
        var val = args.Length > 1 && TryBool(args[1], out var b) ? b : args.Length > 1 && args[1] == "1";
        var ch = ResolveChar(me, unitId, []);
        switch (mode)
        {
            case "invincible":
                ch.SetGodMode(val);
                break;
            case "almighty":
                ch.SetGodMode(val);
                ch.SetInvisible(val);
                break;
        }

        var zone = RelayZone(unitId, (ushort)GmCommandId.ChangeMode, raw);
        return $"{ch.Name} mode {mode}={val}; {zone}";
    }

    private static string Freeze(Character me, string[] args, bool freeze)
    {
        var name = args.ElementAtOrDefault(0);
        var ch = string.IsNullOrWhiteSpace(name) ? me : WorldManager.Instance.GetCharacter(name);
        if (ch == null)
            return $"not online: {name}";
        ch.DisabledSetPosition = freeze;
        return $"{ch.Name} freeze={freeze}";
    }

    #endregion

    #region teleport / social

    private static string GoTo(Character me, string[] args)
    {
        var name = args.ElementAtOrDefault(0);
        if (string.IsNullOrWhiteSpace(name))
            return "need character name";
        var target = WorldManager.Instance.GetCharacter(name);
        if (target is not { IsOnline: true })
            return $"target '{name}' not online";
        var pos = target.Transform.World.Position;
        me.ForceDismount();
        me.DisabledSetPosition = true;
        me.SendPacket(new SCTeleportUnitPacket(TeleportReason.Gm, 0, pos.X, pos.Y, pos.Z + 2f, 0f));
        return $"goto {target.Name}";
    }

    private static string Summon(Character me, string[] args)
    {
        var name = args.ElementAtOrDefault(0);
        if (string.IsNullOrWhiteSpace(name))
            return "need character name";
        var target = WorldManager.Instance.GetCharacter(name);
        if (target is not { IsOnline: true })
            return $"target '{name}' not online";
        var pos = me.Transform.World.Position;
        target.ForceDismount();
        target.DisabledSetPosition = true;
        target.SendPacket(new SCTeleportUnitPacket(TeleportReason.Gm, 0, pos.X, pos.Y, pos.Z + 2f, 0f));
        target.SendMessage($"[GM] {me.Name} summoned you.");
        return $"summoned {target.Name}";
    }

    private static string Notice(Character me, string raw)
    {
        var text = (raw ?? "").Trim().TrimEnd('\r', '\n').Trim();
        if (string.IsNullOrEmpty(text))
            return "empty notice";
        var visibleMs = 1000 + text.Length * 50;
        WorldManager.Instance.BroadcastPacketToServer(
            new SCNoticeMessagePacket(3, Color.FromArgb(0xFF, 0xC1, 0x3D, 0x36), visibleMs, text, me.Name));
        return "notice broadcast";
    }

    private static string Kick(Character me, string[] args)
    {
        var name = args.ElementAtOrDefault(0);
        if (string.IsNullOrWhiteSpace(name))
            return "need character name";
        var target = WorldManager.Instance.GetCharacter(name);
        if (target == null)
            return $"not found: {name}";
        target.SendPacket(new SCKickedPacket(KickedReason.KickByGm, $"Kicked by {me.Name}"));
        return $"kicked {target.Name}";
    }

    private static string ReturnHome(Character me, uint unitId)
    {
        var ch = ResolveChar(me, unitId, []);
        // Bind/return-district teleport is portal-book driven; nudge GM to use Return district UI for now.
        ch.SendMessage($"[GM] Return district id={ch.ReturnDistrictId} — open portal book or /teleport");
        return $"return district={ch.ReturnDistrictId} (manual teleport)";
    }

    #endregion

    #region dumps (chat summary — full SCGmDump* bodies still RE-incomplete)

    private static Character DumpTarget(Character me, string[] args)
    {
        var name = args.ElementAtOrDefault(0);
        if (!string.IsNullOrWhiteSpace(name))
            return WorldManager.Instance.GetCharacter(name) ?? me;
        return me;
    }

    private static string DumpChar(Character me, string[] args)
    {
        var t = DumpTarget(me, args);
        me.SendMessage(
            $"[DumpChar] {t.Name} id={t.Id} lvl={t.Level} hp={t.Hp}/{t.MaxHp} mp={t.Mp}/{t.MaxMp} " +
            $"abil={t.Ability1}/{t.Ability2}/{t.Ability3} money={t.Money} labor={t.LaborPower} " +
            $"pos=({t.Transform.World.Position.X:F1},{t.Transform.World.Position.Y:F1},{t.Transform.World.Position.Z:F1}) zone={t.Transform.ZoneId}");
        return $"dumped {t.Name} to chat";
    }

    private static string DumpBag(Character me, string[] args, bool bank)
    {
        var t = DumpTarget(me, args);
        var bag = bank ? t.Inventory.Warehouse : t.Inventory.Bag;
        var items = bag?.Items?.Take(40).Select(i => $"{i.TemplateId}x{i.Count}") ?? [];
        me.SendMessage($"[Dump{(bank ? "Bank" : "Bag")}] {t.Name}: {string.Join(", ", items)}");
        return $"dumped {(bank ? "bank" : "bag")} for {t.Name}";
    }

    private static string DumpEquipment(Character me, string[] args)
    {
        var t = DumpTarget(me, args);
        var items = t.Inventory.Equipment?.Items?.Select(i => $"{i.Slot}:{i.TemplateId}") ?? [];
        me.SendMessage($"[DumpEquip] {t.Name}: {string.Join(", ", items)}");
        return $"dumped equipment for {t.Name}";
    }

    private static string DumpParty(Character me, string[] args)
    {
        var t = DumpTarget(me, args);
        me.SendMessage($"[DumpParty] {t.Name} party={(t.InParty ? "yes" : "no")} team={t.ParentWorld?.Id}");
        return $"dumped party for {t.Name}";
    }

    private static string DumpQuests(Character me, string[] args)
    {
        var t = DumpTarget(me, args);
        var active = t.Quests?.ActiveQuests?.Keys?.Take(30) ?? [];
        me.SendMessage($"[DumpQuests] {t.Name}: {string.Join(", ", active)}");
        return $"dumped quests for {t.Name}";
    }

    #endregion
}
