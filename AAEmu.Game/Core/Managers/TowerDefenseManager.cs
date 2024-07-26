using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.TowerDefs;
using AAEmu.Game.Models.Game.Units.Route;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Models.Observers;
using NLog;

namespace AAEmu.Game.Core.Managers;

public class TowerDefenseManager : Singleton<TowerDefenseManager>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private Dictionary<byte, List<Spawner<Npc>>> _towerDefSpawns;

    private Dictionary<uint, NpcSpawner> _tmpNpcSpawner;

    public void Initialize()
    {
        TowerDefGameData.Instance._towerDefs.TryGetValue(3, out var td3);

        for (int j = 0; j < 2; j++)
        {
            var tdpst = new TowerDefProgSpawnTarget();
            tdpst.SpawnTargetType = "Npc";
            tdpst.SpawnTargetId = 8850;
            td3.Progs[^1].SpawnTargets.Add(tdpst);
        }

        TowerDefGameData.Instance._towerDefs.TryGetValue(5, out var td5);

        for (int j = 0; j < 2; j++)
        {
            var tdpst = new TowerDefProgSpawnTarget();
            tdpst.SpawnTargetType = "Npc";
            tdpst.SpawnTargetId = 8850;
            td5.Progs[^1].SpawnTargets.Add(tdpst);
        }

        TimeManager.Instance.Subscribe(new TowerDefenseObserver(td3));
        TimeManager.Instance.Subscribe(new TowerDefenseObserver(td5));

        TowerDefGameData.Instance._towerDefs.TryGetValue(13, out var td13);
        TowerDefGameData.Instance._towerDefs.TryGetValue(15, out var td15);


        for (int j = 0; j < 10; j++)
        {
            var tdpst = new TowerDefProgSpawnTarget();
            tdpst.SpawnTargetType = "Npc";
            tdpst.SpawnTargetId = 12728;
            td13.Progs.First().SpawnTargets.Add(tdpst);
        }

        TimeManager.Instance.Subscribe(new TowerDefenseObserver(td13));
        TimeManager.Instance.Subscribe(new TowerDefenseObserver(td15));
    }

    public void start(TowerDef td)
    {
        var targetNpcSpawn = NpcGameData.Instance.GetNpcSpawnerTemplate(td.TargetNpcSpawnId);
        var npcId = targetNpcSpawn.Npcs[^1].MemberId;
        var nsIds = NpcGameData.Instance.GetSpawnerIds(npcId);
        // Test world id 0
        var nss = SpawnManager.Instance.GetNpcSpawners(nsIds[^1], 0);

        var zoneId = nss[^1].Position.ZoneId;

        start(zoneId, td.Id);
    }

    public void start(uint zoneId, uint tdId)
    {
        var cts = new CancellationTokenSource();
        var token = cts.Token;


        try
        {
            // simulator
            Task.Run(async () =>
            {
                if (TowerDefGameData.Instance._towerDefs.TryGetValue(tdId, out var td))
                {
                    Logger.Debug($"TowerDefense {td.Id} Start Msg: {td.StartMsg}");
                    var startPacket =
                        new SCTowerDefStartPacket(new TowerDefKey() { TowerDefId = td.Id, ZoneGroupId = 5 }, zoneId);
                    // start
                    _ = Task.Run(() =>
                    {
                        ChatManager.Instance.GetZoneChat(zoneId).SendPacket(startPacket);
                    }, token);

                    var instanceNpcSpawnList = new List<NpcSpawner>();

                    var position = new WorldSpawnPosition();

                    var despawnOnNextProgList = new List<NpcSpawner>();

                    if (td.ForceEndTime > 0)
                    {
                        Task.Run(async () =>
                        {
                            await Task.Delay((int)(td.ForceEndTime * 1000));
                            cts.Cancel();
                            
                            Logger.Debug($"TowerDefense {td.Id} Force End MSG: {td.EndMsg}");
                            var endPacket =
                                new SCTowerDefEndPacket(new TowerDefKey() { TowerDefId = tdId, ZoneGroupId = 5 }, zoneId);
                            ChatManager.Instance.GetZoneChat(zoneId).SendPacket(endPacket);


                            if (instanceNpcSpawnList.Count > 0)
                            {
                                foreach (var npcSpawner in instanceNpcSpawnList)
                                {
                                    var npcs = npcSpawner.GetSpawnedList();
                                    var ids = npcs.Select(npc => npc.ObjId).ToArray();

                                    npcSpawner.DoDespawnAll();
                                    Task.Run(async () =>
                                    {
                                        await Task.Delay(2 * 1000);
                                        ChatManager.Instance.GetZoneChat(zoneId)
                                            .SendPacket(new SCUnitsRemovedPacket(ids));
                                    });
                                }
                            }
                        });
                    }

                    var rand = new Random();


                    if (td.TargetNpcSpawnId > 0)
                    {
                        Logger.Debug($"TowerDefense {td.Id} spawn NPC");
                        var nst = NpcGameData.Instance.GetNpcSpawnerTemplate(td.TargetNpcSpawnId);

                        if (nst.SpawnDelayMin > 0 && nst.SpawnDelayMax >= nst.SpawnDelayMin)
                        {
                            await Task.Delay(rand.Next((int)nst.SpawnDelayMin, (int)(nst.SpawnDelayMax + 1)) * 1000,
                                token);
                        }

                        var mnsList = SpawnManager.Instance.GetNpcSpawners(td.TargetNpcSpawnId, 0);
                        if (mnsList.Count > 0)
                        {
                            foreach (var npcSpawner in mnsList)
                            {
                                instanceNpcSpawnList.Add(npcSpawner);
                            }

                            var mainNpcSpawn = mnsList[^1];
                            if (mainNpcSpawn.FollowPaths.Count > 1)
                            {
                                mainNpcSpawn.FollowPath =
                                    mainNpcSpawn.FollowPaths[rand.Next(mainNpcSpawn.FollowPaths.Count)];
                            }
                            else if (string.IsNullOrWhiteSpace(mainNpcSpawn.FollowPath) && mainNpcSpawn.FollowPaths.Count > 0)
                            {
                                mainNpcSpawn.FollowPath = mainNpcSpawn.FollowPaths[^1];
                            }

                            mainNpcSpawn.SpawnAll();
                            position = mainNpcSpawn.Position.Clone();

                            // cancel npc cycle
                            foreach (var npc in mainNpcSpawn.GetSpawnedList())
                            {
                                position.X = npc.Ai?.PathHandler?.AiPathPoints[^1]?.Position.X ?? position.X;
                                position.Y = npc.Ai?.PathHandler?.AiPathPoints[^1]?.Position.Y ?? position.Y;
                            }

                            instanceNpcSpawnList.Add(mainNpcSpawn);
                        }
                    }

                    if (td.FirstWaveAfter > 0)
                    {
                        await Task.Delay((int)(td.FirstWaveAfter * 1000), token);
                    }
                    else
                    {
                        await Task.Delay(5 * 1000, token);
                    }

                    foreach (var tdProg in td.Progs)
                    {
                        var step = (uint)td.Progs.IndexOf(tdProg);
                        Logger.Debug($"TowerDefense {td.Id} Prog {tdProg.Id} MSG: {tdProg.Msg}");
                        var tdProgPacket =
                            new SCTowerDefWaveStartPacket(new TowerDefKey() { TowerDefId = tdId, ZoneGroupId = 5 },
                                zoneId,
                                step);
                        ChatManager.Instance.GetZoneChat(zoneId).SendPacket(tdProgPacket);

                        if (despawnOnNextProgList.Count > 0)
                        {
                            foreach (var npcSpawner in despawnOnNextProgList)
                            {
                                npcSpawner.DoDespawnAll();
                            }

                            despawnOnNextProgList.Clear();
                        }

                        if (tdProg.SpawnTargets.Count > 0)
                        {
                            foreach (var towerDefProgSpawnTarget in tdProg.SpawnTargets)
                            {
                                if (towerDefProgSpawnTarget.SpawnTargetType == "NpcSpawner")
                                {
                                    var nst = NpcGameData.Instance.GetNpcSpawnerTemplate(towerDefProgSpawnTarget
                                        .SpawnTargetId);
                                    if (nst.SpawnDelayMin > 0 && nst.SpawnDelayMax > 0)
                                    {
                                        await Task.Delay((int)(nst.SpawnDelayMin * 1000), token);
                                    }

                                    var sList = SpawnManager.Instance.GetNpcSpawners(td.TargetNpcSpawnId, 0);
                                    if (sList.Count > 0)
                                    {
                                        foreach (var npcSpawner in sList)
                                        {
                                            // npcSpawner.DoDespawnAll();
                                            despawnOnNextProgList.Add(npcSpawner);
                                        }

                                        var spawner = sList[^1];
                                        spawner.SpawnAll();
                                        instanceNpcSpawnList.Add(spawner);
                                        if (towerDefProgSpawnTarget.DespawnOnNextStep)
                                        {
                                            despawnOnNextProgList.Add(spawner);
                                        }
                                    }
                                }
                                else if (towerDefProgSpawnTarget.SpawnTargetType == "Npc")
                                {
                                    var npcSpawner = new NpcSpawner
                                    {
                                        UnitId = towerDefProgSpawnTarget.SpawnTargetId,
                                        Position = new WorldSpawnPosition()
                                        {
                                            Pitch = 0,
                                            Roll = 0,
                                            WorldId = WorldManager.Instance.GetWorldIdByZone(zoneId),
                                            ZoneId = zoneId,
                                            X = position.X,
                                            Y = position.Y,
                                            Z = position.Z
                                        },
                                    };
                                    // spawn
                                    SpawnManager.Instance.AddNpcSpawner(npcSpawner);
                                    npcSpawner.Template.MaxPopulation = 1;
                                    npcSpawner.Template.SuspendSpawnCount = 1;
                                    npcSpawner.Nearby = 50f;
                                    npcSpawner.SpawnAll();
                                    npcSpawner.RespawnTime = 0;

                                    if (towerDefProgSpawnTarget.DespawnOnNextStep)
                                    {
                                        despawnOnNextProgList.Add(npcSpawner);
                                    }

                                    instanceNpcSpawnList.Add(npcSpawner);
                                }
                            }
                        }

                        if (tdProg.KillTargets.Count > 0)
                        {
                            Logger.Debug($"TowerDefense {td.Id} Prog {tdProg.Id} spawn NPC");

                            var withKillSpawnList = new List<NpcSpawner>();

                            foreach (var towerDefProgKillTarget in tdProg.KillTargets)
                            {
                                var npcSpawner = new NpcSpawner
                                {
                                    UnitId = towerDefProgKillTarget.KillTargetId,
                                    Position = new WorldSpawnPosition()
                                    {
                                        Pitch = 0,
                                        Roll = 0,
                                        WorldId = WorldManager.Instance.GetWorldIdByZone(zoneId),
                                        ZoneId = zoneId,
                                        X = position.X,
                                        Y = position.Y,
                                        Z = position.Z
                                    },
                                };
                                // 投放到
                                SpawnManager.Instance.AddNpcSpawner(npcSpawner);
                                npcSpawner.Template.MaxPopulation = towerDefProgKillTarget.KillCount;
                                npcSpawner.Template.SuspendSpawnCount = towerDefProgKillTarget.KillCount;
                                npcSpawner.Template.SpawnDelayMin = 0;
                                npcSpawner.Template.SpawnDelayMax = 0;
                                npcSpawner.Nearby = 10f;
                                npcSpawner.SpawnAll();
                                npcSpawner.RespawnTime = 0;

                                withKillSpawnList.Add(npcSpawner);
                                instanceNpcSpawnList.Add(npcSpawner);
                            }

                            var allDead = false;
                            while (!allDead)
                            {
                                await Task.Delay(5 * 1000, token);
                                allDead = true;
                                foreach (var withKillNpcspawn in withKillSpawnList)
                                {
                                    if (withKillNpcspawn.GetSpawnedList().Any(npc => !npc.IsDead))
                                    {
                                        allDead = false;
                                    }
                                }

                                Logger.Debug(
                                    $"TowerDefense {td.Id} Prog {tdProg.Id}  Check if all NPCs have died {allDead}");
                            }
                        }
                        else if (tdProg.CondToNextTime > 0)
                        {
                            await Task.Delay((int)(tdProg.CondToNextTime * 1000),token);
                            // await Task.Delay(5 * 1000);
                        }
                        else
                        {
                            // delay 5s
                            await Task.Delay(5 * 1000, token);
                        }
                    }


                    if (td.ForceEndTime > 0)
                    {
                        // wait force end ...
                        await Task.Delay((int)(td.ForceEndTime * 1000), token);
                    }
                    // await Task.Delay(60 * 1000);
                    // end
                    Logger.Debug($"TowerDefense {td.Id} End MSG: {td.EndMsg}");
                    var endPacket =
                        new SCTowerDefEndPacket(new TowerDefKey() { TowerDefId = tdId, ZoneGroupId = 5 }, zoneId);
                    ChatManager.Instance.GetZoneChat(zoneId).SendPacket(endPacket);

                    // fixme Unknown reason，send SCUnitsRemovedPacket still sending afterwards SCUnitStatePacket, caused Obj to not be removed correctly
                    if (instanceNpcSpawnList.Count > 0)
                    {
                        foreach (var npcSpawner in instanceNpcSpawnList)
                        {
                            var npcs = npcSpawner.GetSpawnedList();
                            var ids = npcs.Select(npc => npc.ObjId).ToArray();

                            npcSpawner.DoDespawnAll();
                            Task.Run(async () =>
                            {
                                await Task.Delay(2 * 1000, token);
                                ChatManager.Instance.GetZoneChat(zoneId).SendPacket(new SCUnitsRemovedPacket(ids));
                            });
                        }
                    }
                }
            }, token);
        }
        catch (Exception e)
        {
            Logger.Error(e);
            throw;
        }
    }
}
