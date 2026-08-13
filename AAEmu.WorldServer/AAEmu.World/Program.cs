using System.Reflection;

using AAEmu.Commons.IO;
using AAEmu.Commons.Network;
using AAEmu.Game;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.World.Core.Network;
using AAEmu.World.Core.Packets.Wz;
using AAEmu.World.Core.Relay;
using AAEmu.World.Core.Zone;
using AAEmu.World.Models;

using Microsoft.Extensions.Configuration;

using NLog;
using NLog.Config;

namespace AAEmu.World;

/// <summary>
/// Commercial World: zone is sim authority (WZ/ZW :1240); CS/SC on :1239 via Game stack as lobby glue.
/// Zone down → clients disconnect. Do not treat Game Spawn as a parallel live world.
/// </summary>
public static class Program
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public static async Task<int> Main(string[] args)
    {
        LogManager.ThrowConfigExceptions = false;
        var worldBin = FileManager.AppPath;
        var worldNlog = Path.Combine(worldBin, "NLog.config");
        if (File.Exists(worldNlog))
            LogManager.Configuration = new XmlLoggingConfiguration(worldNlog);

        var name = Assembly.GetExecutingAssembly().GetName().Name ?? "AAEmu.World";
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "???";
        Logger.Info("{0} version {1} — commercial World (zone authority + CS/SC broker)", name, version);

        var configuration = new ConfigurationBuilder()
            .SetBasePath(worldBin)
            .AddJsonFile("Config.json", optional: true, reloadOnChange: true)
            .AddJsonFile("Config.Local.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();

        var appConfig = new WorldAppConfiguration();
        configuration.Bind(appConfig);
        WorldRuntime.Config = appConfig;
        if (!string.IsNullOrWhiteSpace(appConfig.ZoneGameDataRoot) &&
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AAEMU_ZONE_GAME_DATA_ROOT")))
        {
            Environment.SetEnvironmentVariable("AAEMU_ZONE_GAME_DATA_ROOT", appConfig.ZoneGameDataRoot);
        }

        ZoneNetwork.Instance.Start(appConfig.ZoneNetwork);
        if (appConfig.GameBridge.Enabled)
            GameBridgeNetwork.Instance.Start(appConfig.GameBridge);

        var enter = new PlayerEnterService();
        var movement = new MovementRelay();
        WorldIntegration.ZoneAuthority = true;
        WorldIntegration.TryEnterZone = (bcId, body) => enter.EnterZone(bcId, body);
        WorldIntegration.IsZoneLoaded = zoneId =>
            ZoneSession.Instance.GetByZoneId(zoneId) != null;
        WorldIntegration.GetZoneConnectionStatus = () => ZoneSession.Instance.All
            .Select(zone => new WorldZoneConnectionSnapshot(
                zone.Id,
                zone.ZoneId,
                zone.InstanceId,
                zone.State.ToString(),
                zone.Ip,
                zone.Units.Count))
            .OrderBy(zone => zone.ZoneId)
            .ThenBy(zone => zone.SessionId)
            .ToArray();
        // Shared day → main_world dedicades only (instances own a local noon-start clock).
        WorldIntegration.RelayTimeOfDayToZones = hour =>
        {
            foreach (var zone in PlayerEnterService.AllLoadedZones())
            {
                if (!TimeManager.ZoneUsesSharedGameDay(zone.ZoneId))
                    continue;
                zone.SendPacket(new WZTimeOfDayPacket(hour));
                zone.SendPacket(new WZDetailedTimeOfDayPacket(
                    hour, TimeManager.DefaultGameHourSpeed, 0f, 24f));
            }
        };
        // Type-2 ZW ToD: clients in that zone only — never rebases shared TimeManager.
        WorldIntegration.OnZoneTimeOfDay = (zoneId, time, speed, start, end, detailed) =>
        {
            if (TimeManager.ZoneUsesSharedGameDay(zoneId))
                return;
            WorldIntegration.ForEachReadyConnection((connection, character) =>
            {
                if (character.Transform.ZoneId != zoneId)
                    return;
                connection.SendPacket(detailed
                    ? new SCDetailedTimeOfDayPacket(time, speed, start, end)
                    : new SCTimeOfDayPacket(time));
            });
        };
        // Shared World hour crosses drive Game-Time tower arms (seamless has no ZW ToD).
        WorldIntegration.OnGameTimeAdvanced = TowerDefScheduler.OnGameTimeAdvanced;
        WorldIntegration.RelayUnitStateToZone = (zoneId, body) =>
        {
            var zone = PlayerEnterService.ForZoneId(zoneId)
                       ?? (Environment.GetEnvironmentVariable("AAEMU_ZONE_PRIMARY_FALLBACK") == "1"
                           ? PlayerEnterService.PrimaryZone() : null);
            if (zone == null || body == null || body.Length == 0)
                return;
            zone.SendPacket(new WZUnitStatePacket(body));
            Logger.Info("WZUnitState (non-player) → zoneId={0} bodyLen={1}", zone.ZoneId, body.Length);
        };
        WorldIntegration.OnPlayerLeave = bcId => enter.LeaveZone(bcId);
        WorldIntegration.OnZoneNpcSpawn = WorldIntegration.MirrorZoneNpcSpawn;
        WorldIntegration.OnZoneNpcRemove = WorldIntegration.MirrorZoneNpcRemove;
        WorldIntegration.OnZoneNpcKilled = bcId =>
        {
            // Capture template before the death path may remove the mirror.
            uint tpl = 0;
            if (WorldIntegration.FindUnitAcrossWorlds(bcId) is AAEmu.Game.Models.Game.NPChar.Npc npc)
                tpl = npc.TemplateId;
            WorldIntegration.MirrorZoneNpcKilled(bcId);
            if (tpl != 0)
                TowerDefScheduler.OnNpcKilled(tpl);
        };
        WorldIntegration.OnWorldNpcKilled = tpl => TowerDefScheduler.OnNpcKilled(tpl);
        WorldIntegration.OnWorldInstanceRemoved = ZoneNpcSpawnerCatalog.RemoveInstance;
        WorldIntegration.TriggerTowerDef = (action, towerDefId, step) => action switch
        {
            "start" => TowerDefScheduler.ForceStart(towerDefId)
                ? $"towerDef {towerDefId} started"
                : $"towerDef {towerDefId} not found",
            "end" => TowerDefScheduler.ForceEnd(towerDefId)
                ? $"towerDef {towerDefId} ended"
                : $"towerDef {towerDefId} was not running",
            "wave" => TowerDefScheduler.ForceWave(towerDefId, step)
                ? $"towerDef {towerDefId} advanced to step {step}"
                : $"towerDef {towerDefId} not found",
            _ => $"unknown action {action}"
        };
        WorldIntegration.DescribeTowerDefs = TowerDefScheduler.Describe;
        WorldIntegration.SyncTowerDefsToCharacter = TowerDefScheduler.SyncToCharacter;
        WorldIntegration.OnTowerDefEventNpcMirrored = TowerDefScheduler.OnEventNpcMirrored;
        WorldIntegration.OnMainWorldReady = () =>
        {
            // Arm the schedule gate before remirroring, so the pass that re-accepts already
            // tracked units applies the same window rules as a live announcement.
            NpcScheduleGate.Start();
            NpcSpawnRelay.RemirrorAllZones();
        };
        WorldIntegration.RelayMoveToZone = (bcId, moveBody) =>
        {
            var zone = PlayerEnterService.ForUnit(bcId);
            if (zone == null)
            {
                Logger.Warn("RelayMoveToZone: no ZoneLoaded (bcId={0})", bcId);
                return;
            }

            movement.RelayClientMoveToZone(zone, bcId, moveBody);
        };
        WorldIntegration.RelayCreateSkillControllerToZone = (objId, scType, fallDamageImmune) =>
        {
            var zone = PlayerEnterService.ForUnit(objId);
            if (zone == null)
            {
                Logger.Warn("RelayCreateSkillControllerToZone: no ZoneLoaded (objId={0})", objId);
                return;
            }

            zone.SendPacket(new WZCreateSkillControllerPacket(objId, scType, fallDamageImmune));
        };
        WorldIntegration.RelaySkillControllerStateToZone = (objId, scType, length, teared, cutouted) =>
        {
            var zone = PlayerEnterService.ForUnit(objId);
            if (zone == null)
            {
                Logger.Warn("RelaySkillControllerStateToZone: no ZoneLoaded (objId={0})", objId);
                return;
            }

            zone.SendPacket(new WZSkillControllerStatePacket(objId, scType, length, teared, cutouted));
        };
        WorldIntegration.RelaySkillStartedToZone = (skillId, tl, caster, target, ct, skillObject) =>
        {
            if (Environment.GetEnvironmentVariable("AAEMU_DISABLE_ZONE_COMBAT_RELAY") == "1")
                return false;

            var zone = PlayerEnterService.ForUnit(caster?.ObjId ?? 0);
            if (zone == null)
            {
                Logger.Warn("RelaySkillStartedToZone: no ZoneLoaded (skillId={0} caster={1})", skillId, caster?.ObjId ?? 0);
                return false;
            }

            zone.SendPacket(new WZSkillStartedPacket(skillId, tl, caster, target, ct, skillObject));
            // Hex for crash bisect — body only (no frame length/opcode).
            var body = new PacketStream();
            body.Write(skillId);
            body.Write(tl);
            body.Write(caster);
            body.Write(target);
            body.Write(ct);
            body.WriteWzSkillObject(skillObject);
            var hex = Convert.ToHexString(body.GetBytes());
            Logger.Info("WZSkillStarted → zone skillId={0} tl={1} caster={2} target={3} bodyLen={4} hex={5}",
                skillId, tl, caster?.ObjId ?? 0, target?.ObjId ?? 0, body.Count, hex.Length > 96 ? hex[..96] + "..." : hex);
            return true;
        };
        WorldIntegration.RelaySkillFiredToZone = (skillId, tl, caster, target, skillObject) =>
        {
            if (Environment.GetEnvironmentVariable("AAEMU_DISABLE_ZONE_COMBAT_RELAY") == "1")
                return false;

            var zone = PlayerEnterService.ForUnit(caster?.ObjId ?? 0);
            if (zone == null)
            {
                Logger.Warn("RelaySkillFiredToZone: no ZoneLoaded (skillId={0} caster={1})", skillId, caster?.ObjId ?? 0);
                return false;
            }

            var skillObj = skillObject ?? new SkillObject();
            zone.SendPacket(new WZSkillFiredPacket(skillId, tl, caster, target, skillObj));
            var body = new PacketStream();
            body.Write(skillId);
            body.Write(tl);
            body.Write(caster);
            body.Write(target);
            body.WriteWzSkillObject(skillObj);
            body.Write(false);
            var hex = Convert.ToHexString(body.GetBytes());
            Logger.Info("WZSkillFired → zone skillId={0} tl={1} caster={2} target={3} bodyLen={4} hex={5}",
                skillId, tl, caster?.ObjId ?? 0, target?.ObjId ?? 0, body.Count,
                hex.Length > 96 ? hex[..96] + "..." : hex);
            return true;
        };
        WorldIntegration.RelaySkillEndedToZone = (tl, caster) =>
        {
            if (Environment.GetEnvironmentVariable("AAEMU_DISABLE_ZONE_COMBAT_RELAY") == "1")
                return false;

            var zone = PlayerEnterService.ForUnit(caster?.ObjId ?? 0);
            if (zone == null)
            {
                Logger.Warn("RelaySkillEndedToZone: no ZoneLoaded (tl={0} caster={1})", tl, caster?.ObjId ?? 0);
                return false;
            }

            zone.SendPacket(new WZSkillEndedPacket(tl, caster));
            Logger.Info("WZSkillEnded → zone tl={0} caster={1}", tl, caster?.ObjId ?? 0);
            return true;
        };
        WorldIntegration.RelayGmCommandToZone = (unitId, cmd, parameters) =>
        {
            var zone = PlayerEnterService.ForUnit(unitId);
            if (zone == null)
            {
                Logger.Warn("RelayGmCommandToZone: no ZoneLoaded for unit={0} cmd={1}", unitId, cmd);
                return false;
            }

            if (cmd > byte.MaxValue)
            {
                // no representation on the link and truncating it would run a different command.
                Logger.Warn("RelayGmCommandToZone: cmd={0} exceeds the zone's byte-wide selector", cmd);
                return false;
            }

            zone.SendPacket(new WZGmCommandPacket(unitId, (byte)cmd, parameters ?? ""));
            Logger.Info("WZGmCommand → zoneId={0} unit={1} cmd={2} params={3}", zone.ZoneId, unitId, cmd, parameters);
            return true;
        };
        WorldIntegration.RelayUnitDamagedToZone = (skillId, tl, caster, target, damage, absorbed, casterId, targetId) =>
        {
            if (Environment.GetEnvironmentVariable("AAEMU_DISABLE_ZONE_COMBAT_RELAY") == "1")
                return;
            if (Environment.GetEnvironmentVariable("AAEMU_WZ_UNIT_DAMAGED") == "0")
                return;

            var zone = PlayerEnterService.ForUnit(targetId != 0 ? targetId : casterId);
            if (zone == null)
                return;

            var castAction = new CastSkill(skillId, tl);
            zone.SendPacket(new WZUnitDamagedPacket(
                castAction,
                caster,
                casterId,
                targetId,
                damage,
                absorbed));
            Logger.Info("WZUnitDamaged → zone skill={0} tl={1} caster={2} target={3} dmg={4} abs={5}",
                skillId, tl, casterId, targetId, damage, absorbed);

            // Publishing UpdateAggro alone leaves AggroCount>0 with no target; Zone then
            // ProcessAggroCancel → ZWClearCombat / skill 11503 Return (mid-fight leash).
            // Opt-out: AAEMU_WZ_UPDATE_AGGRO=0 (isolates the whole handoff if a zone drops the link).
            if (Environment.GetEnvironmentVariable("AAEMU_WZ_UPDATE_AGGRO") == "0")
                return;
            if (casterId == 0 || targetId == 0 || casterId == targetId)
                return;

            var aggro = (uint)Math.Max(1, damage + absorbed);
            var world = WorldManager.Instance.GetWorld(WorldManager.DefaultInstanceId);
            // Aggro tables and combat engagement belong to NPC targets, not player victims.
            var damagedNpc = world?.GetNpc(targetId);
            if (damagedNpc == null)
                return;

            var abuser = world.GetUnit(casterId);
            if (abuser != null)
                damagedNpc.CurrentTarget = abuser;
            zone.SendPacket(new WZTargetChangedPacket(targetId, casterId, forceByWorld: true));
            Logger.Info("WZTargetChanged → zone npc={0} target={1} (damage handoff)", targetId, casterId);

            zone.SendPacket(new WZUpdateAggroPacket(
                targetId,
                casterId,
                casterId,
                aggro,
                true,
                castAction));
            Logger.Info("WZUpdateAggro → zone npc={0} target={1} aggro={2}", targetId, casterId, aggro);

            zone.SendPacket(new WZCombatEngagedPacket(targetId));
            Logger.Info("WZCombatEngaged → zone npc={0} (damage handoff)", targetId);
        };
        WorldIntegration.RelayUnitPointsToZone = (objId, hp, mp) =>
        {
            if (Environment.GetEnvironmentVariable("AAEMU_DISABLE_ZONE_COMBAT_RELAY") == "1")
                return;
            if (Environment.GetEnvironmentVariable("AAEMU_WZ_UNIT_POINTS") == "0")
                return;

            var zone = PlayerEnterService.ForUnit(objId);
            if (zone == null)
                return;

            zone.SendPacket(new WZUnitPointsPacket(objId, hp, mp));
            Logger.Info("WZUnitPoints → zone objId={0} hp={1} mp={2}", objId, hp, mp);
        };
        WorldIntegration.RelayUnitDeathToZone = bcId =>
        {
            if (Environment.GetEnvironmentVariable("AAEMU_DISABLE_ZONE_COMBAT_RELAY") == "1")
                return;

            var zone = PlayerEnterService.ForUnit(bcId);
            if (zone == null)
                return;

            ZoneNpcSpawnerCatalog.MarkUnitDead(zone, bcId);
            zone.SendPacket(new WZUnitDeathPacket(bcId));
            Logger.Info("WZUnitDeath → zone bcId={0}", bcId);
        };
        WorldIntegration.RelayNpcStartDespawnToZone = bcId =>
        {
            if (Environment.GetEnvironmentVariable("AAEMU_DISABLE_ZONE_COMBAT_RELAY") == "1")
                return;

            var zone = PlayerEnterService.ForUnit(bcId);
            if (zone == null)
                return;

            zone.SendPacket(new WZNpcStartDespawnPacket(bcId));
            Logger.Info("WZNpcStartDespawn → zone bcId={0}", bcId);
        };
        WorldIntegration.RelayNpcSpawnerEventToZone = request =>
        {
            var zone = request.CreatorObjId != 0
                ? PlayerEnterService.ForUnit(request.CreatorObjId)
                : null;
            if (zone == null)
                return false;

            zone.SendPacket(new WZNpcSpawnerEventPacket(request));
            Logger.Info(
                "WZNpcSpawnerEvent -> zoneId={0} spawnerId={1} event={2} creator={3} lifeTime={4}",
                zone.ZoneId,
                request.SpawnerId,
                request.Event,
                request.CreatorObjId,
                request.LifeTime);
            return true;
        };
        WorldIntegration.RelayNpcSpawnToZone = request =>
        {
            var zone = PlayerEnterService.ForZoneId(request.ZoneId);
            if (zone == null || request.Body is not { Length: > 0 })
                return false;

            zone.SendPacket(new WZNpcStatePacket(request.Body));
            zone.Units.RegisterWithId(request.ObjId, request.Body);
            Logger.Info(
                "WZNpcState -> zoneId={0} World-authored npc={1} bodyLen={2}",
                zone.ZoneId,
                request.ObjId,
                request.Body.Length);
            return true;
        };
        WorldIntegration.RelayNpcAggroToZone = request =>
        {
            var zone = PlayerEnterService.ForUnit(request.SkillTargetObjId);
            if (zone == null)
                return;

            zone.SendPacket(new WZUpdateAggroPacket(
                request.SkillTargetObjId,
                request.SourceObjId,
                request.UnitInChargeObjId,
                request.Aggro,
                request.Hostile,
                request.CastAction));
        };
        WorldIntegration.RelayAggroResetToZone = (
            unitId,
            damageSelector,
            healSelector,
            directSelector,
            applyValue) =>
        {
            var zone = PlayerEnterService.ForUnit(unitId);
            if (zone == null)
                return;

            zone.SendPacket(new WZAggroResetPacket(
                unitId,
                damageSelector,
                healSelector,
                directSelector,
                applyValue));
        };
        WorldIntegration.RelayAggroCopyToZone = (sourceUnitId, destinationUnitId) =>
        {
            // The destination is the unit whose authoritative AI state is replaced.
            var zone = PlayerEnterService.ForUnit(destinationUnitId);
            if (zone == null)
                return;

            zone.SendPacket(new WZAggroCopyPacket(sourceUnitId, destinationUnitId));
        };
        WorldIntegration.RelayFakeDeathToZone = unitId =>
        {
            var zone = PlayerEnterService.ForUnit(unitId);
            if (zone == null)
                return;

            zone.SendPacket(new WZFakeDeathPacket(unitId));
        };
        WorldIntegration.RelayUnitRemovedToZone = bcId =>
        {
            if (Environment.GetEnvironmentVariable("AAEMU_DISABLE_ZONE_COMBAT_RELAY") == "1")
                return;

            var zone = PlayerEnterService.ForUnit(bcId);
            if (zone == null)
                return;

            zone.SendPacket(new WZUnitRemovedPacket(bcId));

            // A forced removal has no completion callback, so release its spawn marker here.
            NpcSpawnRelay.ForgetNpcState(zone.ZoneId, bcId);
            Logger.Info("WZUnitRemoved → zone bcId={0} (forced teardown, Create marker dropped)", bcId);
        };
        WorldIntegration.RelayUnitRemovedToZoneId = (zoneId, bcId) =>
        {
            var zone = PlayerEnterService.ForZoneId(zoneId);
            if (zone == null)
                return;

            zone.SendPacket(new WZUnitRemovedPacket(bcId));
            Logger.Info("WZUnitRemoved → zoneId={0} bcId={1}", zoneId, bcId);
        };
        WorldIntegration.RelayPlotEventToZone = (tl, eventId, skillId, caster, target, itemId, objId, castTimeMs, channelingMs, conditionOk, last, targetUnitIds) =>
        {
            if (Environment.GetEnvironmentVariable("AAEMU_DISABLE_ZONE_COMBAT_RELAY") == "1")
                return;
            if (Environment.GetEnvironmentVariable("AAEMU_DISABLE_WZ_PLOT_EVENT") == "1")
                return;

            var zone = PlayerEnterService.ForUnit(caster?.UnitId ?? target?.UnitId ?? objId);
            if (zone == null)
                return;

            zone.SendPacket(new WZPlotEventPacket(tl, eventId, skillId, caster, target, itemId, objId, castTimeMs, channelingMs, conditionOk, last, targetUnitIds));
            Logger.Debug("WZPlotEvent → zone tl={0} event={1} skill={2}", tl, eventId, skillId);
        };

        WorldIntegration.RelayCreateDoodadToZone = doodadObj =>
        {
            if (Environment.GetEnvironmentVariable("AAEMU_WZ_DOODAD") == "0")
                return;
            if (doodadObj is not AAEmu.Game.Models.Game.DoodadObj.Doodad doodad)
                return;
            // Only after ZoneLoaded — Joined-only was letting phase/create race join gate.
            var zoneId = doodad.Transform?.ZoneId ?? 0;
            var zone = PlayerEnterService.ForZoneId(zoneId)
                       ?? (Environment.GetEnvironmentVariable("AAEMU_ZONE_PRIMARY_FALLBACK") == "1"
                           ? PlayerEnterService.PrimaryZone() : null);
            if (zone == null)
                return;
            if (!ShouldSendWzCreateDoodad(doodad, out var modelId))
                return;
            zone.SendPacket(new WZCreateDoodadPacket(doodad));
            Logger.Debug("WZCreateDoodad → zone obj={0} tpl={1} modelId={2}", doodad.ObjId, doodad.TemplateId, modelId);
        };
        WorldIntegration.RelayRemoveDoodadToZone = objId =>
        {
            if (Environment.GetEnvironmentVariable("AAEMU_WZ_DOODAD") == "0")
                return;
            PlayerEnterService.ForUnit(objId)?.SendPacket(new WZRemoveDoodadPacket(objId));
        };
        WorldIntegration.RelayDoodadPhaseToZone = (objId, funcGroupId, data) =>
        {
            if (Environment.GetEnvironmentVariable("AAEMU_WZ_DOODAD") == "0")
                return;
            // Timers fire constantly; never send before ZoneLoaded (Join race → opcode 0x75 crash).
            var zone = PlayerEnterService.ForUnit(objId);
            if (zone == null)
                return;
            zone.SendPacket(new WZDoodadChangePhasePacket(objId, funcGroupId));
        };
        // After ZoneLoaded: push doodads that spawned while Zone was down (batched).
        WorldIntegration.NotifyZoneReadyForDoodads = zoneId =>
        {
            if (Environment.GetEnvironmentVariable("AAEMU_WZ_DOODAD") == "0")
                return;
            _ = FlushWorldDoodadsToZoneAsync(zoneId);
        };
        WorldIntegration.NotifyZoneReadyForHousing = zoneId =>
            AAEmu.Game.Core.Managers.HousingManager.Instance.RelayAllToZone(zoneId);
        WorldIntegration.NotifyZoneReadyForGimmicks = zoneId =>
            FlushWorldGimmicksToZone(zoneId);
        WorldIntegration.RelayEquipmentChangedToZone = (unitId, body) =>
        {
            // Opcode 0x01E. Empty EquipView type sentinel is 0 (not FFFFFFFF). Kill-switch: AAEMU_WZ_EQUIP=0.
            if (Environment.GetEnvironmentVariable("AAEMU_WZ_EQUIP") == "0")
                return;
            var zone = PlayerEnterService.ForUnit(unitId);
            if (zone == null || body == null)
                return;
            zone.SendPacket(new WZUnitEquipmentChangedPacket(unitId, body));
            Logger.Debug(
                "WZUnitEquipmentChanged → zone bc={0} bodyLen={1} bodyHex={2}",
                unitId, body.Length, Convert.ToHexString(body));
        };
        WorldIntegration.RelayBuffCreatedToZone = (targetId, body) =>
        {
            if (Environment.GetEnvironmentVariable("AAEMU_WZ_BUFF") == "0")
                return;
            var zone = PlayerEnterService.ForUnit(targetId);
            if (zone == null || body == null)
                return;
            zone.SendPacket(new WZBuffCreatedPacket(body));
        };
        WorldIntegration.RelayBuffRemovedToZone = (targetId, buffId) =>
        {
            if (Environment.GetEnvironmentVariable("AAEMU_WZ_BUFF") == "0")
                return;
            var zone = PlayerEnterService.ForUnit(targetId);
            zone?.SendPacket(new WZBuffRemovedPacket(targetId, buffId));
        };
        WorldIntegration.RelayInteractNpcToZone = (playerId, npcId, ending) =>
        {
            var zone = PlayerEnterService.ForUnit(playerId);
            if (zone == null)
                return;
            if (ending)
                zone.SendPacket(new WZInteractNpcEndPacket(playerId, npcId));
            else
                zone.SendPacket(new WZInteractNpcPacket(playerId, npcId));
        };
        WorldIntegration.RelayImpulseToZone = (targetId, caster, vel, angVel, impulse, angImpulse) =>
        {
            var zone = PlayerEnterService.ForUnit(targetId);
            if (zone == null || caster == null)
                return;

            zone.SendPacket(new WZImpulseUnitPacket(
                targetId, caster,
                vel[0], vel[1], vel[2],
                angVel[0], angVel[1], angVel[2],
                impulse[0], impulse[1], impulse[2],
                angImpulse[0], angImpulse[1], angImpulse[2]));
            Logger.Info("WZImpulseUnit → zone target={0} vel=({1:0.0},{2:0.0},{3:0.0})",
                targetId, vel[0], vel[1], vel[2]);
        };
        WorldIntegration.RelayUnitHealedToZone = (castAction, caster, targetId, healType, healHitType, value, unitInCharge, critical) =>
        {
            if (Environment.GetEnvironmentVariable("AAEMU_DISABLE_ZONE_COMBAT_RELAY") == "1")
                return;
            if (Environment.GetEnvironmentVariable("AAEMU_WZ_UNIT_HEALED") == "0")
                return;

            var zone = PlayerEnterService.ForUnit(targetId != 0 ? targetId : unitInCharge);
            if (zone == null)
                return;

            zone.SendPacket(new WZUnitHealedPacket(
                castAction, caster, targetId, healType, healHitType, value, unitInCharge, critical));
            Logger.Info("WZUnitHealed → zone target={0} type={1} value={2} inCharge={3}",
                targetId, healType, value, unitInCharge);
        };
        WorldIntegration.RelayKnockBackToZone = (unitId, x, y, z) =>
        {
            if (Environment.GetEnvironmentVariable("AAEMU_DISABLE_ZONE_COMBAT_RELAY") == "1")
                return;

            var zone = PlayerEnterService.ForUnit(unitId);
            if (zone == null)
                return;

            zone.SendPacket(new WZKnockBackUnitPacket(unitId, x, y, z));
            Logger.Info("WZKnockBackUnit → zone unit={0} pos=({1:F1},{2:F1},{3:F1})", unitId, x, y, z);
        };
        WorldIntegration.RelayBlinkToZone = (unitId, baseUnitId, move3D, x, y, z) =>
        {
            if (Environment.GetEnvironmentVariable("AAEMU_DISABLE_ZONE_COMBAT_RELAY") == "1")
                return;

            var zone = PlayerEnterService.ForUnit(unitId);
            if (zone == null)
                return;

            zone.SendPacket(new WZBlinkUnitPacket(
                unitId,
                baseUnitId,
                move3D,
                (ulong)AAEmu.Commons.Utils.Helpers.ConvertLongX(x),
                (ulong)AAEmu.Commons.Utils.Helpers.ConvertLongY(y),
                z));
            Logger.Info("WZBlinkUnit → zone unit={0} pos=({1:F1},{2:F1},{3:F1}) move3D={4}",
                unitId, x, y, z, move3D);
        };
        WorldIntegration.RelayCombatEngagedToZone = unitId =>
        {
            if (Environment.GetEnvironmentVariable("AAEMU_DISABLE_ZONE_COMBAT_RELAY") == "1")
                return;

            var zone = PlayerEnterService.ForUnit(unitId);
            if (zone == null)
                return;

            zone.SendPacket(new WZCombatEngagedPacket(unitId));
            Logger.Debug("WZCombatEngaged → zone unit={0}", unitId);
        };
        WorldIntegration.RelayCombatClearedToZone = unitId =>
        {
            if (Environment.GetEnvironmentVariable("AAEMU_DISABLE_ZONE_COMBAT_RELAY") == "1")
                return;

            var zone = PlayerEnterService.ForUnit(unitId);
            if (zone == null)
                return;

            zone.SendPacket(new WZCombatClearedPacket(unitId));
            Logger.Debug("WZCombatCleared → zone unit={0}", unitId);
        };
        WorldIntegration.RelayUnitDuelStateToZone = (unitId, duelStateObjId, duelTeamType) =>
        {
            var zone = PlayerEnterService.ForUnit(unitId);
            if (zone == null)
                return;

            zone.SendPacket(new WZUnitDuelStatePacket(unitId, duelStateObjId, duelTeamType));
            Logger.Debug(
                "WZUnitDuelState -> zone unit={0} duelObject={1} team={2}",
                unitId,
                duelStateObjId,
                unchecked((sbyte)duelTeamType));
        };
        WorldIntegration.RelayTargetChangedToZone = (unitId, targetId, forceByWorld) =>
        {
            if (Environment.GetEnvironmentVariable("AAEMU_DISABLE_ZONE_COMBAT_RELAY") == "1")
                return;

            var zone = PlayerEnterService.ForUnit(unitId);
            if (zone == null)
                return;

            zone.SendPacket(new WZTargetChangedPacket(unitId, targetId, forceByWorld));
            Logger.Debug("WZTargetChanged → zone unit={0} target={1}", unitId, targetId);
        };
        WorldIntegration.RelayForceAttackToZone = (unitId, on) =>
        {
            if (Environment.GetEnvironmentVariable("AAEMU_DISABLE_ZONE_COMBAT_RELAY") == "1")
                return;

            var zone = PlayerEnterService.ForUnit(unitId);
            if (zone == null)
                return;

            zone.SendPacket(new WZForceAttackSetPacket(unitId, on));
            Logger.Debug("WZForceAttackSet → zone unit={0} on={1}", unitId, on);
        };
        WorldIntegration.RelayLevelChangedToZone = (unitId, level) =>
        {
            if (Environment.GetEnvironmentVariable("AAEMU_DISABLE_ZONE_COMBAT_RELAY") == "1")
                return;

            var zone = PlayerEnterService.ForUnit(unitId);
            if (zone == null)
                return;

            zone.SendPacket(new WZLevelChangedPacket(unitId, level));
            Logger.Info("WZLevelChanged → zone unit={0} level={1}", unitId, level);
        };
        WorldIntegration.RelayUnitResurrectionToZone = (unitId, x, y, z, zRot) =>
        {
            if (Environment.GetEnvironmentVariable("AAEMU_DISABLE_ZONE_COMBAT_RELAY") == "1")
                return;

            var zone = PlayerEnterService.ForUnit(unitId);
            if (zone == null)
                return;

            zone.SendPacket(new WZUnitResurrectionPacket(
                unitId,
                (ulong)AAEmu.Commons.Utils.Helpers.ConvertLongX(x),
                (ulong)AAEmu.Commons.Utils.Helpers.ConvertLongY(y),
                z,
                zRot));
            Logger.Info("WZUnitResurrection → zone unit={0} pos=({1:F1},{2:F1},{3:F1})", unitId, x, y, z);
        };
        WorldIntegration.RelaySkillStoppedToZone = (unitId, skillId) =>
        {
            if (Environment.GetEnvironmentVariable("AAEMU_DISABLE_ZONE_COMBAT_RELAY") == "1")
                return;

            var zone = PlayerEnterService.ForUnit(unitId);
            if (zone == null)
                return;

            zone.SendPacket(new WZSkillStoppedPacket(unitId, skillId));
            Logger.Debug("WZSkillStopped → zone unit={0} skill={1}", unitId, skillId);
        };
        WorldIntegration.RelayCastingStoppedToZone = (unitId, tl, typeValue, duration) =>
        {
            if (Environment.GetEnvironmentVariable("AAEMU_DISABLE_ZONE_COMBAT_RELAY") == "1")
                return;

            var zone = PlayerEnterService.ForUnit(unitId);
            if (zone == null)
                return;

            zone.SendPacket(new WZCastingStoppedPacket(unitId, tl, typeValue, duration));
            Logger.Debug("WZCastingStopped → zone unit={0} tl={1}", unitId, tl);
        };
        WorldIntegration.RelayUnitFactionChangedToZone = (unitId, oldFaction, newFaction, temp) =>
        {
            if (Environment.GetEnvironmentVariable("AAEMU_DISABLE_ZONE_COMBAT_RELAY") == "1")
                return;
            var zone = PlayerEnterService.ForUnit(unitId);
            if (zone == null)
                return;
            zone.SendPacket(new WZUnitFactionChangedPacket(unitId, oldFaction, newFaction, temp));
            Logger.Info("WZUnitFactionChanged → zone unit={0} {1}→{2}", unitId, oldFaction, newFaction);
        };
        WorldIntegration.RelayUnitExpeditionChangedToZone = (unitId, oldExpedition, newExpedition) =>
        {
            var zone = PlayerEnterService.ForUnit(unitId);
            if (zone == null)
                return;
            zone.SendPacket(new WZUnitExpeditionChangedPacket(unitId, oldExpedition, newExpedition));
            Logger.Info("WZUnitExpeditionChanged zone unit={0} old={1} new={2}", unitId, oldExpedition, newExpedition);
        };
        WorldIntegration.RelayEscapeSlaveToZone = (slaveId, x, y, z, rot) =>
        {
            var zone = PlayerEnterService.ForUnit(slaveId);
            if (zone == null)
                return;
            zone.SendPacket(new WZEscapeSlavePacket(
                slaveId,
                (ulong)AAEmu.Commons.Utils.Helpers.ConvertLongX(x),
                (ulong)AAEmu.Commons.Utils.Helpers.ConvertLongY(y),
                z,
                rot));
            Logger.Info("WZEscapeSlave → zone slave={0} pos=({1:F1},{2:F1},{3:F1})", slaveId, x, y, z);
        };
        WorldIntegration.RelayShipControlChangeToZone = (slaveId, control) =>
        {
            var zone = PlayerEnterService.ForUnit(slaveId);
            if (zone == null)
                return;
            zone.SendPacket(new WZShipControlChangePacket(slaveId, control));
            Logger.Info("WZShipControlChange → zone slave={0} control={1}", slaveId, control);
        };
        WorldIntegration.RelayQuestNpcAiToZone = (kind, npcId, playerId, pathName, pathType, commandSetId) =>
        {
            var zone = PlayerEnterService.ForUnit(npcId != 0 ? npcId : playerId);
            if (zone == null)
                return;
            switch (kind)
            {
                case 0:
                    zone.SendPacket(new WZAttackOnQuestPacket(npcId, playerId));
                    break;
                case 1:
                    zone.SendPacket(new WZFollowUnitOnQuestPacket(npcId, playerId));
                    break;
                case 2:
                    zone.SendPacket(new WZFollowPathOnQuestPacket(npcId, playerId, pathName ?? "", pathType));
                    break;
                case 3:
                    zone.SendPacket(new WZRunCommandSetOnQuestPacket(npcId, playerId, commandSetId));
                    break;
                default:
                    return;
            }
            Logger.Info("WZQuestNpcAi kind={0} npc={1} player={2}", kind, npcId, playerId);
        };
        WorldIntegration.RelayBuffUpdatedToZone = (unitId, buffIndex, stack, charged, elapsedMs, reason) =>
        {
            if (Environment.GetEnvironmentVariable("AAEMU_WZ_BUFF") == "0")
                return;
            var zone = PlayerEnterService.ForUnit(unitId);
            if (zone == null)
                return;
            zone.SendPacket(new WZBuffUpdatedPacket(unitId, buffIndex, stack, charged, elapsedMs, reason));
            Logger.Debug("WZBuffUpdated → zone unit={0} idx={1} stack={2} charge={3}", unitId, buffIndex, stack, charged);
        };
        WorldIntegration.RelayRequestCombatUnitsToZone = (unitId, aroundId) =>
        {
            if (Environment.GetEnvironmentVariable("AAEMU_DISABLE_ZONE_COMBAT_RELAY") == "1")
                return;
            var zone = PlayerEnterService.ForUnit(unitId);
            if (zone == null)
                return;
            zone.SendPacket(new WZRequestCombatUnitsPacket(unitId, aroundId));
            Logger.Debug("WZRequestCombatUnits → zone unit={0} around={1}", unitId, aroundId);
        };
        WorldIntegration.RelayDropBackpackToZone = (unitId, item, doodadTpl, zoneId, x, y, z, removeItem, hackAttempt, userDrop) =>
        {
            var itemUid = item.Id;
            var zone = PlayerEnterService.ForUnit(unitId) ?? PlayerEnterService.ForZoneId(zoneId);
            if (zone == null)
                return;
            zone.SendPacket(new WZDropBackpackPacket(
                item, zoneId, doodadTpl, zone.InstanceId, removeItem, hackAttempt, userDrop, x, y, z));
            Logger.Info("WZDropBackpack → zone unit={0} item={1} doodadTpl={2}", unitId, itemUid, doodadTpl);
        };
        WorldIntegration.OnZoneBackpackDropped = body =>
        {
            // Zone notifies World a backpack hit the ground; World already owns SC doodad spawn
            // for player-initiated drops. Log for now so dual-spawn is avoided until body RE
            // confirms Zone-authored packs need World persistence.
            Logger.Info("ZWBackpackDropped len={0}", body?.Length ?? 0);
        };
        WorldIntegration.RelayUnitAttachToZone = (unitId, targetId, attachPoint, attached) =>
        {
            var zone = PlayerEnterService.ForUnit(unitId);
            if (zone == null)
                return;
            if (attached)
            {
                zone.SendPacket(new WZUnitAttachedPacket(unitId, targetId, attachPoint));
                Logger.Info("WZUnitAttached → zone unit={0} target={1} point={2}", unitId, targetId, attachPoint);
            }
            else
            {
                zone.SendPacket(new WZUnitDetachedPacket(unitId));
                Logger.Info("WZUnitDetached → zone unit={0}", unitId);
            }
        };
        WorldIntegration.RelayBondDoodadToZone = (unitId, bonding, bond) =>
        {
            var zone = PlayerEnterService.ForUnit(unitId);
            if (zone == null)
                return;
            if (bond && bonding != null)
            {
                // Passenger fields require zone unit ids; doodad ObjIds are invalid as the bonding unit.
                if (!ObjectIdManager.IsZoneUnitId(unitId))
                {
                    Logger.Warn("Not relaying WZUnitBondToDoodad: unit={0} is not a zone unit id", unitId);
                    return;
                }

                var doodad = bonding.GetOwner();
                if (doodad == null || bonding.ObjId == 0)
                {
                    Logger.Warn("Not relaying WZUnitBondToDoodad: unit={0} has no seat doodad", unitId);
                    return;
                }

                var rootObjId = BondDoodad.ResolveZoneRootUnitId(doodad);
                zone.SendPacket(new WZUnitBondToDoodadPacket(unitId, bonding, rootObjId));
                Logger.Info("WZUnitBondToDoodad → zone unit={0} doodad={1} root={2} point={3} kind={4}",
                    unitId, bonding.ObjId, rootObjId, (byte)bonding.AttachPoint, (uint)bonding.Kind);
            }
            else
            {
                zone.SendPacket(new WZUnitUnbondFromDoodadPacket(unitId));
                Logger.Info("WZUnitUnbondFromDoodad → zone unit={0}", unitId);
            }
        };
        WorldIntegration.RelayHouseStateToZone = (zoneId, unitStateBody, houseStateBody) =>
        {
            if (Environment.GetEnvironmentVariable("AAEMU_WZ_HOUSE") == "0")
                return;
            var zone = PlayerEnterService.ForZoneId(zoneId)
                       ?? (Environment.GetEnvironmentVariable("AAEMU_ZONE_PRIMARY_FALLBACK") == "1"
                            ? PlayerEnterService.PrimaryZone() ?? PlayerEnterService.AnyJoinedZone() : null);
            if (zone == null || unitStateBody == null || houseStateBody == null)
                return;
            zone.SendPacket(new WZUnitStatePacket(unitStateBody));
            zone.SendPacket(new WZHouseStatePacket(houseStateBody));
        };
        WorldIntegration.RelayHouseBuildProgressToZone = (zoneId, tl, type, allStep, curStep) =>
        {
            if (Environment.GetEnvironmentVariable("AAEMU_WZ_HOUSE") == "0")
                return;
            var zone = PlayerEnterService.ForZoneId(zoneId)
                       ?? (Environment.GetEnvironmentVariable("AAEMU_ZONE_PRIMARY_FALLBACK") == "1"
                           ? PlayerEnterService.PrimaryZone() : null);
            zone?.SendPacket(new WZHouseBuildProgressPacket(tl, type, allStep, curStep));
        };
        WorldIntegration.RelayHouseBuildDoneToZone = (zoneId, tl) =>
        {
            if (Environment.GetEnvironmentVariable("AAEMU_WZ_HOUSE") == "0")
                return;
            var zone = PlayerEnterService.ForZoneId(zoneId)
                       ?? (Environment.GetEnvironmentVariable("AAEMU_ZONE_PRIMARY_FALLBACK") == "1"
                           ? PlayerEnterService.PrimaryZone() : null);
            zone?.SendPacket(new WZHouseBuildDonePacket(tl));
        };
        WorldIntegration.RelayGimmickCreatedToZone = (data, ownerZoneId) =>
        {
            var zone = ownerZoneId >= 0 ? ZoneSession.Instance.GetJoinedByZoneId((uint)ownerZoneId) : null;
            zone ??= Environment.GetEnvironmentVariable("AAEMU_ZONE_PRIMARY_FALLBACK") == "1"
                ? PlayerEnterService.PrimaryZone() ?? PlayerEnterService.AnyJoinedZone()
                : null;
            if (zone == null)
                return;
            zone.SendPacket(new WZGimmickCreatedPacket(data, ownerZoneId));
            Logger.Debug(
                "WZGimmickCreated → zone={0} id={1} type={2}",
                ownerZoneId, data.Id, data.Type);
        };
        WorldIntegration.OnZoneRequestStaticGimmick = (requestZoneId, data) =>
        {
            var x = AAEmu.Commons.Utils.Helpers.ConvertLongX(data.X);
            var y = AAEmu.Commons.Utils.Helpers.ConvertLongY(data.Y);
            ZoneStaticGimmickAuthority.Register(data.Id, data.StaticZoneId, x, y, data.Z);
            WorldIntegration.RelayGimmickCreatedToZone?.Invoke(data, (int)requestZoneId);
        };
        WorldIntegration.RelayGimmickRemovedToZone = id =>
        {
            foreach (var zone in PlayerEnterService.AllLoadedZones())
                zone.SendPacket(new WZGimmickRemovedPacket(id));
            Logger.Debug("WZGimmickRemoved → all zones id={0}", id);
        };
        WorldIntegration.RelayGimmickGraspedToZone = (ownerZoneId, id, grasperUnitId, grasped) =>
        {
            var zone = PlayerEnterService.ForZoneId(ownerZoneId)
                       ?? (Environment.GetEnvironmentVariable("AAEMU_ZONE_PRIMARY_FALLBACK") == "1"
                           ? PlayerEnterService.PrimaryZone() ?? PlayerEnterService.AnyJoinedZone() : null);
            if (zone == null)
                return;
            zone.SendPacket(new WZGimmickGraspedPacket((int)id, (int)grasperUnitId, grasped));
            Logger.Debug(
                "WZGimmickGrasped to zone={0} id={1} grasper={2} grasped={3}",
                ownerZoneId, id, grasperUnitId, grasped);
        };
        WorldIntegration.TryInteractZoneGimmick = ZoneStaticGimmickAuthority.Interact;
        WorldIntegration.ReleaseZoneGimmickGrasps = ZoneStaticGimmickAuthority.Release;
        WorldIntegration.RelayZoneCommand = (unitId, command) =>
        {
            var zone = PlayerEnterService.ForUnit(unitId);
            zone?.SendPacket(new WZRunCommandPacket(unitId, command));
        };
        WorldIntegration.RelayZoneRayCasting = (
            unitObjId, playerId, x, y, z, dirX, dirY, dirZ, id, isWaterLevelCasting, isTextInfo) =>
        {
            var zone = PlayerEnterService.ForUnit(unitObjId);
            zone?.SendPacket(new WZRayCastingPacket(
                playerId, x, y, z, dirX, dirY, dirZ, id, isWaterLevelCasting, isTextInfo));
        };
        WorldIntegration.OnZoneRayCastingResult = (playerId, id, x, y, z, text) =>
        {
            if (playerId > uint.MaxValue)
                return;
            var character = WorldManager.Instance.GetCharacterById((uint)playerId);
            character?.SendPacket(new SCWorldRayCastingResultPacket(id, x, y, z, text));
        };
        WorldIntegration.RelaySlaveMasterChangedToZone = (slaveId, masterId, masterWorldId) =>
        {
            var zone = PlayerEnterService.ForUnit(slaveId);
            zone?.SendPacket(new WZSlaveMasterChangedPacket(slaveId, masterId, masterWorldId));
        };
        WorldIntegration.RelaySiegeStateToZone = body =>
        {
            if (body == null)
                return;
            foreach (var zone in PlayerEnterService.AllLoadedZones())
                zone.SendPacket(new WZSiegeStatePacket(body));
        };
        WorldIntegration.RelayMoleCheckToZone = (miner, body) =>
        {
            if (body == null)
                return;
            // Mole packets are not unit-scoped on this hook — fan-out to all loaded zones.
            foreach (var zone in PlayerEnterService.AllLoadedZones())
            {
                if (miner)
                    zone.SendPacket(new WZCheckMoleMinerPacket(body));
                else
                    zone.SendPacket(new WZCheckMoleTraderPacket(body));
            }
        };


        WorldIntegration.RelayCharacterZoneHandoff = (bcId, oldZoneId, newZoneId, unitStateBody) =>
        {
            return PlayerEnterService.HandoffOnZoneChange(bcId, oldZoneId, newZoneId, unitStateBody);
        };

        WorldIntegration.OnZoneEnterArea = (unitId, areaId, v1, v2) =>
        {
            ZoneQuestAreaBridge.OnEnter(unitId, areaId, v1, v2);
        };
        WorldIntegration.OnZoneLeaveArea = (unitId, areaId, v1, v2) =>
        {
            ZoneQuestAreaBridge.OnLeave(unitId, areaId, v1, v2);
        };
        WorldIntegration.IsWorldOwnedGimmick = objId =>
            AAEmu.Game.Core.Managers.World.WorldManager.Instance.GetWorlds()
                .Any(w => w.GimmickManager?.OwnsGimmick(objId) == true);
        WorldIntegration.OnZoneRemoveHouse = tl =>
        {
            // housing timeline id and must not delete the persistent World-owned house.
            Logger.Info("Zone removed its housing simulation entry tl={0}", tl);
        };

        Logger.Info(
            "ZoneAuthority ON | NPCs+move+doodad/housing/gimmick/quest-area relays | CS/SC glue | zone :{0} | localWire={1}",
            appConfig.ZoneNetwork.Port,
            AAEmu.Game.Core.Managers.World.ZoneCoordBoundary.UseLocalOnZoneWire);

        var gameContentRoot = ResolveGameContentRoot(appConfig);
        Logger.Info("CS/SC lobby glue from {0}", gameContentRoot);

        FileManager.SetAppPath(gameContentRoot);
        Directory.SetCurrentDirectory(gameContentRoot);

        try
        {
            return await global::AAEmu.Game.Program.Main(args);
        }
        finally
        {
            WorldIntegration.ZoneAuthority = false;
            WorldIntegration.TryEnterZone = null;
            WorldIntegration.GetZoneConnectionStatus = null;
            WorldIntegration.RelayTimeOfDayToZones = null;
            WorldIntegration.OnZoneTimeOfDay = null;
            WorldIntegration.OnGameTimeAdvanced = null;
            WorldIntegration.RelayUnitStateToZone = null;
            WorldIntegration.OnPlayerLeave = null;
            WorldIntegration.RelayMoveToZone = null;
            WorldIntegration.RelayCreateSkillControllerToZone = null;
            WorldIntegration.RelaySkillControllerStateToZone = null;
            WorldIntegration.RelaySkillStartedToZone = null;
            WorldIntegration.RelaySkillFiredToZone = null;
            WorldIntegration.RelaySkillEndedToZone = null;
            WorldIntegration.RelayUnitDamagedToZone = null;
            WorldIntegration.RelayUnitPointsToZone = null;
            WorldIntegration.RelayUnitHealedToZone = null;
            WorldIntegration.RelayKnockBackToZone = null;
            WorldIntegration.RelayBlinkToZone = null;
            WorldIntegration.RelayCombatEngagedToZone = null;
            WorldIntegration.RelayCombatClearedToZone = null;
            WorldIntegration.RelayUnitDuelStateToZone = null;
            WorldIntegration.RelayTargetChangedToZone = null;
            WorldIntegration.RelayForceAttackToZone = null;
            WorldIntegration.RelayLevelChangedToZone = null;
            WorldIntegration.RelayUnitResurrectionToZone = null;
            WorldIntegration.RelaySkillStoppedToZone = null;
            WorldIntegration.RelayCastingStoppedToZone = null;
            WorldIntegration.RelayUnitFactionChangedToZone = null;
            WorldIntegration.RelayUnitExpeditionChangedToZone = null;
            WorldIntegration.RelayEscapeSlaveToZone = null;
            WorldIntegration.RelayShipControlChangeToZone = null;
            WorldIntegration.RelayQuestNpcAiToZone = null;
            WorldIntegration.RelayBuffUpdatedToZone = null;
            WorldIntegration.RelayRequestCombatUnitsToZone = null;
            WorldIntegration.RelayDropBackpackToZone = null;
            WorldIntegration.RelayUnitDeathToZone = null;
            WorldIntegration.RelayNpcStartDespawnToZone = null;
            WorldIntegration.RelayNpcSpawnerEventToZone = null;
            WorldIntegration.RelayNpcSpawnToZone = null;
            WorldIntegration.RelayNpcAggroToZone = null;
            WorldIntegration.RelayAggroResetToZone = null;
            WorldIntegration.RelayAggroCopyToZone = null;
            WorldIntegration.RelayFakeDeathToZone = null;
            WorldIntegration.RelayUnitRemovedToZone = null;
            WorldIntegration.RelayUnitRemovedToZoneId = null;
            WorldIntegration.RelayPlotEventToZone = null;
            WorldIntegration.RelayGmCommandToZone = null;
            WorldIntegration.RelayCreateDoodadToZone = null;
            WorldIntegration.NotifyZoneReadyForDoodads = null;
            WorldIntegration.NotifyZoneReadyForHousing = null;
            WorldIntegration.NotifyZoneReadyForGimmicks = null;
            WorldIntegration.RelayCharacterZoneHandoff = null;
            WorldIntegration.RelayRemoveDoodadToZone = null;
            WorldIntegration.RelayDoodadPhaseToZone = null;
            WorldIntegration.RelayEquipmentChangedToZone = null;
            WorldIntegration.RelayBuffCreatedToZone = null;
            WorldIntegration.RelayBuffRemovedToZone = null;
            WorldIntegration.RelayInteractNpcToZone = null;
            WorldIntegration.RelayUnitAttachToZone = null;
            WorldIntegration.RelayBondDoodadToZone = null;
            WorldIntegration.RelayHouseStateToZone = null;
            WorldIntegration.RelayHouseBuildProgressToZone = null;
            WorldIntegration.RelayHouseBuildDoneToZone = null;
            WorldIntegration.RelayGimmickCreatedToZone = null;
            WorldIntegration.RelayGimmickRemovedToZone = null;
            WorldIntegration.RelayGimmickGraspedToZone = null;
            WorldIntegration.TryInteractZoneGimmick = null;
            WorldIntegration.ReleaseZoneGimmickGrasps = null;
            ZoneStaticGimmickAuthority.Clear();
            WorldIntegration.RelayZoneCommand = null;
            WorldIntegration.RelayZoneRayCasting = null;
            WorldIntegration.OnZoneRayCastingResult = null;
            WorldIntegration.RelaySlaveMasterChangedToZone = null;
            WorldIntegration.RelaySiegeStateToZone = null;
            WorldIntegration.RelayMoleCheckToZone = null;
            WorldIntegration.OnZoneEnterArea = null;
            WorldIntegration.OnZoneLeaveArea = null;
            WorldIntegration.OnZoneRemoveHouse = null;
            WorldIntegration.IsWorldOwnedGimmick = null;
            WorldIntegration.OnZoneRequestStaticGimmick = null;
            WorldIntegration.OnZoneBackpackDropped = null;
            WorldIntegration.OnZoneNpcSpawn = null;
            WorldIntegration.OnZoneNpcRemove = null;
            WorldIntegration.OnZoneNpcKilled = null;
            WorldIntegration.OnMainWorldReady = null;
            WorldIntegration.OnWorldInstanceRemoved = null;
            // Game host already disposed DI — never touch Singleton<> here (was ObjectDisposedException).
            try { ZoneNetwork.Instance.Stop(); } catch { /* ignore */ }
            try { GameBridgeNetwork.Instance.Stop(); } catch { /* ignore */ }
            LogManager.Shutdown();
        }
    }

    private static void FlushWorldGimmicksToZone(uint zoneId)
    {
        var zone = PlayerEnterService.ForZoneId(zoneId);
        if (zone == null)
        {
            Logger.Warn("Gimmick flush skipped — no ZoneLoaded for zoneId={0}", zoneId);
            return;
        }

        var world = WorldIntegration.ResolveWorldForZone(zoneId);
        if (world == null)
        {
            Logger.Warn("Gimmick flush skipped — no world instance owns zoneId={0}", zoneId);
            return;
        }

        var sent = 0;
        var skippedOtherZone = 0;
        foreach (var gimmick in world.GimmickManager.GetActiveGimmicks())
        {
            if (gimmick?.Transform?.ZoneId != zoneId)
            {
                skippedOtherZone++;
                continue;
            }

            zone.SendPacket(new WZGimmickCreatedPacket(gimmick.ToSpawnData(), (int)zoneId));
            sent++;
        }

        Logger.Info(
            "Gimmick flush → zoneId={0} world={1} sent={2} skippedOtherZone={3}",
            zoneId, world.Template?.Name, sent, skippedOtherZone);
    }

    /// <summary>
    /// DancerS: Zone resolves mesh from doodad_almighties.model via designId (DB + game/).
    /// Skip empty model strings (still pumpkin even when patched).
    /// AAEMU_WZ_DOODAD_REQUIRE_MODEL=1 → also require prefab_elements→models.id map (legacy).
    /// </summary>
    private static bool ShouldSendWzCreateDoodad(AAEmu.Game.Models.Game.DoodadObj.Doodad doodad, out uint modelId)
    {
        modelId = doodad.GetZoneModelId();
        var path = doodad.Template?.Model;
        if (doodad.Template?.FuncGroups != null)
        {
            foreach (var fg in doodad.Template.FuncGroups)
            {
                if (fg.Id == doodad.FuncGroupId && !string.IsNullOrEmpty(fg.Model))
                {
                    path = fg.Model;
                    break;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(path))
            return false;

        if (Environment.GetEnvironmentVariable("AAEMU_WZ_DOODAD_REQUIRE_MODEL") == "1")
            return modelId != 0 || doodad.Template?.LoadModelFromWorld == true
                   || Environment.GetEnvironmentVariable("AAEMU_WZ_DOODAD_ALLOW_PUMPKIN") == "1";

        return true;
    }

    /// <summary>
    /// Batched to avoid a 40k-packet burst; kill with AAEMU_WZ_DOODAD=0.
    /// </summary>
    private static async Task FlushWorldDoodadsToZoneAsync(uint zoneId)
    {
        try
        {
            await Task.Delay(250).ConfigureAwait(false); // let ActivateNpcSpawners go first
            var zone = PlayerEnterService.ForZoneId(zoneId)
                       ?? (zoneId == 0 ? PlayerEnterService.PrimaryZone() : null);
            if (zone == null || zone.State < AAEmu.World.Core.Zone.ZoneConnectionState.ZoneLoaded)
            {
                Logger.Warn("Doodad flush skipped — no ZoneLoaded for zoneId={0}", zoneId);
                return;
            }

            // Doodads live in the WorldInstance that owns the zone, which is not always MainWorld:
            // zone 260 (arche_mall_world) was flushed against main_world's 42k, so every doodad but
            // one fell out as skippedOtherZone and Mirage Isle came up bare.
            var world = WorldIntegration.ResolveWorldForZone(zone.ZoneId);
            if (world == null)
            {
                Logger.Warn("Doodad flush skipped — no world instance owns zoneId={0}", zone.ZoneId);
                return;
            }

            var all = world.GetAllDoodads();
            if (all == null || all.Count == 0)
            {
                Logger.Info(
                    "Doodad flush: 0 doodads in world {0} for zoneId={1}",
                    world.Template?.Name, zone.ZoneId);
                return;
            }

            const int batch = 100;
            var sent = 0;
            var skippedNoModel = 0;
            var skippedOtherZone = 0;
            Logger.Info(
                "Doodad flush → zoneId={0} world={1} starting count={2} batch={3}",
                zone.ZoneId, world.Template?.Name, all.Count, batch);
            for (var i = 0; i < all.Count; i++)
            {
                zone = PlayerEnterService.ForZoneId(zoneId)
                       ?? (zoneId == 0 ? PlayerEnterService.PrimaryZone() : null);
                if (zone == null || zone.State < AAEmu.World.Core.Zone.ZoneConnectionState.ZoneLoaded)
                {
                    Logger.Error("Doodad flush aborted — Zone lost after {0}/{1}", sent, all.Count);
                    return;
                }

                var d = all[i];
                if (d == null)
                    continue;
                var dZone = d.Transform?.ZoneId ?? 0;
                if (zone.ZoneId != 0 && dZone != 0 && dZone != zone.ZoneId)
                {
                    skippedOtherZone++;
                    continue;
                }
                if (!ShouldSendWzCreateDoodad(d, out _))
                {
                    skippedNoModel++;
                    continue;
                }

                zone.SendPacket(new WZCreateDoodadPacket(d));
                sent++;
                if (sent % batch == 0)
                {
                    await Task.Delay(25).ConfigureAwait(false);
                    if (sent % 2000 == 0)
                        Logger.Info("Doodad flush progress zoneId={0} {1}/{2}", zone.ZoneId, sent, all.Count);
                }
            }

            Logger.Info(
                "Doodad flush → zoneId={0} done sent={1} skippedNoModelId={2} skippedOtherZone={3} (REQUIRE_MODEL={4})",
                zone.ZoneId,
                sent,
                skippedNoModel,
                skippedOtherZone,
                Environment.GetEnvironmentVariable("AAEMU_WZ_DOODAD_REQUIRE_MODEL") == "1");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Doodad flush failed");
        }
    }

    private static string ResolveGameContentRoot(WorldAppConfiguration appConfig)
    {
        // Prefer explicit Config.Local GameContentRoot (Game bin: compact.sqlite3 + Configurations).
        // Zone level packs stay on ZoneGameDataRoot / AAEMU_ZONE_GAME_DATA_ROOT.
        var root = GameContentRootResolver.Resolve(appConfig.GameContentRoot, AppContext.BaseDirectory);
        if (!GameContentRootResolver.HasTowerDefsOverlay(root))
        {
            Logger.Error(
                "GameContentRoot {0} is missing Configurations/TowerDefs.json — Game-Time tower auto-arm will stay empty. " +
                "Copy AAEmu.Game/Configurations/TowerDefs.json into that Configurations folder.",
                root);
        }

        return root;
    }
}
