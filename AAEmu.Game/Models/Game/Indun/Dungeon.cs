using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Indun.Events;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Teleport;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils;

using NLog;

namespace AAEmu.Game.Models.Game.Indun
{
    public class Dungeon
    {
        private static Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly List<uint> _players;
        private readonly World.World _world;
        private readonly ZoneInstanceId _zoneInstanceId;
        private readonly List<Npc> _spawnedNpcs;
        public readonly IndunZone _indunZone;
        // unused private List<Character> _teleportList;
        private ConcurrentDictionary<uint, DateTime> _leaveRequests;
        private Character _characterOwner;
        private Team.Team _teamOwner;
        private bool _isTeamOwned;
        private Dictionary<uint, bool> _rooms;
        //private static Dictionary<uint, Dictionary<uint, int>> _attempts; // <ownerId, <zoneGroupId, attempts>> - использовано попыток прохождения данжона
        //private const int FreeAttempts = 3;  // свободных попыток
        //private const int ExtraAttempts = 2; // дополнительных попыток
        //public bool IsWaitingDungeonAccessAttemptsCleared { get; set; }

        private object _lock = new();

        public bool IsTeamOwned { get => _isTeamOwned; }
        public Character GetCharacterOwner { get => _characterOwner; }
        public Team.Team GetTeamOwner { get => _teamOwner; }
        public uint GetZoneGroupId { get => _indunZone.ZoneGroupId; }


        public Dungeon(IndunZone indunZone, Character character, Team.Team team = null)
        {
            _indunZone = indunZone;
            _players = new List<uint>();
            _leaveRequests = new ConcurrentDictionary<uint, DateTime>();
            _rooms = new Dictionary<uint, bool>();
            _characterOwner = character;

            if (team == null)
            {
                _isTeamOwned = false;
            }
            else
            {
                _characterOwner = null;
                _teamOwner = team;
                _isTeamOwned = true;
            }

            if (_teamOwner != null)
            {
                TickManager.Instance.OnTick.Subscribe(LeaveDungeonTick, TimeSpan.FromSeconds(1), true);
            }

            TickManager.Instance.OnTick.Subscribe(AreaClearTick, TimeSpan.FromSeconds(1), true);
            var zoneKeys = ZoneManager.Instance.GetZoneKeysInZoneGroupById(_indunZone.ZoneGroupId);
            switch (zoneKeys.Count)
            {
                case > 1:
                    {
                        Logger.Info("There are more than one zone keys for this dungeon?!");
                        break;
                    }
                case 0:
                    {
                        Logger.Error("No Zone Keys found for this zone group id.");
                        return;
                    }
            }
            var world = WorldManager.Instance.GetWorldByZone(zoneKeys[0]);
            _world = WorldManager.Instance.CreateWorld(world);
            _spawnedNpcs = SpawnManager.Instance.SpawnAll(_world.Id, _world.TemplateId);
            foreach (var ev in IndunGameData.Instance.GetIndunEvents(_indunZone.ZoneGroupId))
            {
                ev?.Subscribe(_world);
            }

            foreach (var npc in _spawnedNpcs)
            {
                if (npc == null) { continue; }
                RegisterNpcEvents(npc);
            }

            _zoneInstanceId = new ZoneInstanceId(zoneKeys[0], _world.Id);
        }

        /// <summary>
        /// Returns true if the dungeon is full capacity, false if not.
        /// </summary>
        public bool IsFull => _players.Count == _indunZone.MaxPlayers;

        /// <summary>
        /// Returns true if the dungeon has players inside, false if not.
        /// </summary>
        public bool HasPlayers => _players.Count > 0;

        /// <summary>
        /// Add player to Dungeon
        /// </summary>
        /// <param name="character"></param>
        public void AddPlayer(Character character)
        {
            Logger.Info($"[Dungeon] Adding player {character.Name} to dungeon {_zoneInstanceId.InstanceId}, {_zoneInstanceId.ZoneId}");

            // выводится окошко о том, что создается данжон
            character.SendPacket(new SCProcessingInstancePacket((int)_zoneInstanceId.ZoneId));

            lock (_lock)
            {
                if (!_players.Contains(character.Id))
                {
                    _players.Add(character.Id);
                }
                else
                {
                    Logger.Info($"[Dungeon] Player {character.Name} already exists in dungeon {_zoneInstanceId.InstanceId}, {_zoneInstanceId.ZoneId}. Most likely an error in logic?");
                }
            }
            MoveCharacterToWorld(character);
        }

        /// <summary>
        /// Remove player from Dungeon
        /// </summary>
        /// <param name="character"></param>
        public bool RemovePlayer(Character character)
        {
            if (character == null) { return false; }
            lock (_lock)
            {
                return _players.Remove(character.Id);
            }
        }

        /// <summary>
        /// Destroys dungeon instance for teams
        /// </summary>
        private async Task DestroyTeamDungeon()
        {
            await Task.Delay(5000);

            Logger.Info($"[Dungeon] instanceId: {_zoneInstanceId.InstanceId}, zoneId: {_zoneInstanceId.ZoneId}. Destroying team dungeon...");

            IndunManager.Instance.RemoveDungeon(_teamOwner);
            if (_world == null) { return; }

            foreach (var ev in IndunGameData.Instance.GetIndunEvents(_indunZone.ZoneGroupId))
            {
                ev?.UnSubscribe(_world);
            }

            var npcList = new List<Npc>();
            foreach (var region in _world.Regions)
            {
                region?.GetList(npcList, 0);
            }

            foreach (var npc in npcList)
            {
                if (npc == null) { continue; }

                UnregisterNpcEvents(npc);
                npc.Delete();
                ObjectIdManager.Instance.ReleaseId(npc.ObjId);
            }

            WorldManager.Instance.RemoveWorld(_world.Id);
            WorldIdManager.Instance.ReleaseId(_world.Id);
        }

        /// <summary>
        ///  Destroys dungeon instance for solo players
        /// </summary>
        public bool DestroySoloDungeon(Character character, Dungeon soloDungeon)
        {
            if (character == null) { return false; }

            TickManager.Instance.OnTick.UnSubscribe(AreaClearTick);

            RemovePlayer(character);

            if (!soloDungeon.IsOwner(character) || soloDungeon.HasPlayers) { return false; }
            if (_world == null) { return true; }

            foreach (var ev in IndunGameData.Instance.GetIndunEvents(_indunZone.ZoneGroupId))
            {
                ev.UnSubscribe(_world);
            }

            var npcList = new List<Npc>();

            foreach (var region in _world.Regions)
            {
                region.GetList(npcList, 0);
            }

            foreach (var npc in npcList)
            {
                UnregisterNpcEvents(npc);
                npc.Delete();
                ObjectIdManager.Instance.ReleaseId(npc.ObjId);
            }

            WorldManager.Instance.RemoveWorld(_world.Id);
            WorldIdManager.Instance.ReleaseId(_world.Id);

            return true;

        }

        /// <summary>
        /// Moves character to instanced dungeon world
        /// </summary>
        /// <param name="character"></param>
        private void MoveCharacterToWorld(Character character)
        {
            if (_world.SpawnPosition != null)
            {
                character.DisabledSetPosition = true;
                character.MainWorldPosition = character.Transform.CloneDetached(character); // сохраним координаты для возврата в основной мир
                character.SendPacket(new SCLoadInstancePacket(_world.Id, _zoneInstanceId.ZoneId, _world.SpawnPosition.X, _world.SpawnPosition.Y, _world.SpawnPosition.Z, 0, 0, _world.SpawnPosition.Yaw.DegToRad()));
                character.Transform.ApplyWorldSpawnPosition(_world.SpawnPosition, _world.Id);
                character.InstanceId = _world.Id;

                character.Events.OnTeamJoin += OnTeamJoin;
                character.Events.OnTeamKick += OnTeamLeave;
                character.Events.OnTeamLeave += OnTeamLeave;
                character.Events.OnDungeonLeave += OnDungeonLeave;
                character.Events.OnDisconnect += OnDisconnect;

                Task.Run(() =>
                {
                    Task.Delay(5000);
                    if (character.Transform.World.Position == character.MainWorldPosition.World.Position)
                    {
                        // проверим, если координаты остались как в основном мире, то пробуем телепортнуть персонажа
                        // check if the coordinates are the same as in the main world, then try to teleport the character
                        character.DisabledSetPosition = true;
                        character.SendPacket(new SCTeleportUnitPacket(TeleportReason.IndunDirectTel, 0, _world.SpawnPosition.X, _world.SpawnPosition.Y, _world.SpawnPosition.Z, _world.SpawnPosition.Yaw.DegToRad()));
                    }
                });
            }
            else
            {
                Logger.Info($"World #{_world.Id}, not have default spawn position.");
                character.SendErrorMessage(ErrorMessageType.NoServerInstanceResource);
            }
        }

        /// <summary>
        /// Moves player out of the instanced dungeon world.
        /// </summary>
        /// <param name="character"></param>
        private void LeaveInstance(Character character)
        {
            character.Events.OnTeamJoin -= OnTeamJoin;
            character.Events.OnTeamKick -= OnTeamLeave;
            character.Events.OnTeamLeave -= OnTeamLeave;
            character.Events.OnDungeonLeave -= OnDungeonLeave;
            character.Events.OnDisconnect -= OnDisconnect;

            _leaveRequests.TryRemove(character.Id, out var _);
            RemovePlayer(character);

            if (character.MainWorldPosition == null) { return; }

            var tmpTransform = character.Transform.Clone(); // временно сохраним координаты положения персонажа в данже // temporarily save the position of the character in the dungeon
            character.DisabledSetPosition = true;
            character.SendPacket(
                new SCLoadInstancePacket(
                    character.MainWorldPosition.WorldId,
                    character.MainWorldPosition.ZoneId,
                    character.MainWorldPosition.World.Position.X,
                    character.MainWorldPosition.World.Position.Y,
                    character.MainWorldPosition.World.Position.Z,
                    0,
                    0,
                    character.MainWorldPosition.World.Rotation.Z.DegToRad()
                )
            );
            character.InstanceId = character.MainWorldPosition.WorldId;
            character.Transform = character.MainWorldPosition.Clone();
            character.MainWorldPosition.Dispose();
            character.MainWorldPosition = null; // уничтожаем, так как мы переместились в основной мир // destroy since we moved to the main world

            Task.Run(() =>
            {
                Task.Delay(5000);
                if (character.Transform.World.Position == tmpTransform.World.Position)
                {
                    // проверим, если координаты остались как в данже, то пробуем телепортнуть персонажа
                    // check if the coordinates are the same as in the dungeon, then try to teleport the character
                    character.DisabledSetPosition = true;
                    character.SendPacket(new SCTeleportUnitPacket(TeleportReason.IndunDirectTel, 0, character.Transform.World.Position.X, character.Transform.World.Position.Y, character.Transform.World.Position.Z, character.Transform.World.Rotation.Z.DegToRad()));
                }

                tmpTransform.Dispose();
                tmpTransform = null;
            });
        }

        private void OnTeamJoin(object sender, OnTeamJoinArgs args)
        {
            var character = args.Player;
            var team = args.Team;
            var ownerId = team.OwnerId;
            if (character == null) { return; }

            Logger.Info($"Player {character.Name} has joined a party!");

            if (_isTeamOwned == false)
            {
                if (ownerId != _characterOwner.Id) { return; }
                if (!IndunManager.Instance.SoloToParty(character, team, this)) { return; }
                _teamOwner = team;
                TickManager.Instance.OnTick.Subscribe(LeaveDungeonTick, TimeSpan.FromSeconds(1), true);
                _isTeamOwned = true;
                _characterOwner = null;
                Logger.Info($"[Dungeon] instanceId: {_zoneInstanceId.InstanceId}, zoneId: {_zoneInstanceId.ZoneId}. Converting solo instance into a party instance.");
                return;
            }

            if (PlayerInSameTeam(character) && !_players.Contains(character.Id))
            {
                _players.Add(character.Id);
            }
        }

        private void OnTeamLeave(object sender, OnTeamLeaveArgs args)
        {
            var teamId = args.Id;
            var team = args.Team;
            var character = args.Player;

            if (character == null) { return; }

            Logger.Info($"Player {character.Name} has left the party {teamId}!");
            character.SendErrorMessage(ErrorMessageType.InstanceLeaveParty);
            if (_players.Contains(character.Id) && character.InstanceId == _zoneInstanceId.InstanceId)
            {
                _leaveRequests.TryAdd(character.Id, DateTime.UtcNow.AddSeconds(30));
            }
        }

        private void OnDungeonLeave(object sender, OnDungeonLeaveArgs args)
        {
            var character = args.Player;
            if (character == null) { return; }

            Logger.Info($"Player {character.Name} has exit from dungeon {_world.Id}!");

            if (character.InParty)
            {
                // выход из данжона (без его удаления)
                LeaveInstance(character);
            }

            if (!_players.Contains(character.Id) || character.InstanceId != _zoneInstanceId.InstanceId) { return; }
            // выход из данжона (с его удалением)
            //IndunManager.Instance.RequestDeletion(character, this);
            LeaveInstance(character);
        }

        private void OnDisconnect(object sender, OnDisconnectArgs args)
        {
            Logger.Info($"[Dungeon] instanceId: {_zoneInstanceId.InstanceId}, zoneId: {_zoneInstanceId.ZoneId} name: {args.Player.Name} disconnected!");

            if (!args.Player.InParty)
            {
                IndunManager.Instance.RequestDeletion(args.Player, this);
            }

            RemovePlayer(args.Player);
            args.Player.Events.OnTeamJoin -= OnTeamJoin;
            args.Player.Events.OnTeamKick -= OnTeamLeave;
            args.Player.Events.OnTeamLeave -= OnTeamLeave;
            args.Player.Events.OnDungeonLeave -= OnDungeonLeave;
            args.Player.Events.OnDisconnect -= OnDisconnect;
        }

        private void OnCombatStarted(object sender, OnCombatStartedArgs args)
        {
            if (args.Owner is not Npc npc)
            {
                if (args.Target is not Npc target)
                {
                    return;
                }
                npc = target;
            }

            if (npc.IsInBattle)
            {
                return;
            }
            npc.IsInBattle = true;

            var skills = NpcGameData.Instance.GetNpSkill(npc.TemplateId, SkillUseConditionKind.InCombat);
            if (skills == null) { return; }

            Logger.Info($"Npc={npc.ObjId}:{npc.TemplateId} has started combat.");

            foreach (var npcSkill in skills)
            {
                var skill = SkillManager.Instance.GetNpSkillTemplate(npcSkill);

                if (skill is null) { continue; }

                var skillCaster = SkillCaster.GetByType(SkillCasterType.Unit);
                skillCaster.ObjId = npc.ObjId;

                var skillTarget = SkillCastTarget.GetByType(SkillCastTargetType.Unit);
                skillTarget.ObjId = npc.ObjId;

                if (npc.Cooldowns.CheckCooldown(skill.Id)) { continue; }
                if (skill.Template.CooldownTime == 0)
                {
                    npc.Cooldowns.AddCooldown(skill.Id, uint.MaxValue); // выполняем один раз
                }
                Logger.Info($"Npc={npc.ObjId}:{npc.TemplateId} using skill={skill.Id}");
                skill.Use(npc, skillCaster, skillTarget);
            }
        }

        private void InIdle(object sender, InIdleArgs args)
        {
            if (args.Owner is not Npc npc) { return; }
            var skills = NpcGameData.Instance.GetNpSkill(npc.TemplateId, SkillUseConditionKind.InIdle);
            if (skills == null) { return; }

            Logger.Info($"Npc={npc.ObjId}:{npc.TemplateId} has entered idle state.");

            foreach (var npcSkill in skills)
            {
                var skill = SkillManager.Instance.GetNpSkillTemplate(npcSkill);

                if (skill is null) { continue; }

                var skillCaster = SkillCaster.GetByType(SkillCasterType.Unit);
                skillCaster.ObjId = npc.ObjId;

                var skillTarget = SkillCastTarget.GetByType(SkillCastTargetType.Unit);
                skillTarget.ObjId = npc.ObjId;

                if (npc.Cooldowns.CheckCooldown(skill.Id)) { continue; }
                if (skill.Template.CooldownTime == 0)
                {
                    npc.Cooldowns.AddCooldown(skill.Id, uint.MaxValue); // выполняем один раз
                }
                Logger.Info($"Npc={npc.ObjId}:{npc.TemplateId} using skill={skill.Id}");
                skill.Use(npc, skillCaster, skillTarget);
            }
        }

        private void OnDeath(object sender, OnDeathArgs args)
        {
            if (args.Victim is not Npc npc) { return; }
            var skills = NpcGameData.Instance.GetNpSkill(npc.TemplateId, SkillUseConditionKind.OnDeath);
            if (skills == null) { return; }

            Logger.Info($"Npc objId: {npc.ObjId}, templateId: {npc.TemplateId} has died.");

            UnregisterNpcEvents(npc);
            UnregisterIndunEvents();

            foreach (var npcSkill in skills)
            {
                var skill = SkillManager.Instance.GetNpSkillTemplate(npcSkill);

                if (skill is null) { continue; }

                var skillCaster = SkillCaster.GetByType(SkillCasterType.Unit);
                skillCaster.ObjId = npc.ObjId;

                var skillTarget = SkillCastTarget.GetByType(SkillCastTargetType.Unit);
                skillTarget.ObjId = npc.ObjId;

                if (npc.Cooldowns.CheckCooldown(skill.Id)) { continue; }
                if (skill.Template.CooldownTime == 0)
                {
                    npc.Cooldowns.AddCooldown(skill.Id, uint.MaxValue); // выполняем один раз
                }
                Logger.Info($"Npc={npc.ObjId}:{npc.TemplateId} using skill={skill.Id}");
                skill.Use(npc, skillCaster, skillTarget);
            }

            const uint Dahuta = 13310u; // Npc Id=13310 "Dahuta", 50
            if (npc.TemplateId is not Dahuta)
            {
                SpawnManager.Instance.SpawnWithinSourceRange(_world.TemplateId, args.Victim);
            }
        }

        private void OnSpawn(object sender, OnSpawnArgs args)
        {
            if (args.Npc is not Npc npc) { return; }
            var skills = NpcGameData.Instance.GetNpSkill(npc.TemplateId, SkillUseConditionKind.InIdle);
            if (skills == null) { return; }

            Logger.Info($"Npc objId: {npc.ObjId}, templateId: {npc.TemplateId} OnSpawn triggered.");

            foreach (var npcSkill in skills)
            {
                var skill = SkillManager.Instance.GetNpSkillTemplate(npcSkill);

                if (skill is null) { continue; }

                var skillCaster = SkillCaster.GetByType(SkillCasterType.Unit);
                skillCaster.ObjId = npc.ObjId;

                var skillTarget = SkillCastTarget.GetByType(SkillCastTargetType.Unit);
                skillTarget.ObjId = npc.ObjId;

                if (npc.Cooldowns.CheckCooldown(skill.Id)) { continue; }
                if (skill.Template.CooldownTime == 0)
                {
                    npc.Cooldowns.AddCooldown(skill.Id, uint.MaxValue); // выполняем один раз
                }
                Logger.Info($"Npc={npc.ObjId}:{npc.TemplateId} using skill={skill.Id}");
                skill.Use(npc, skillCaster, skillTarget);
            }
        }

        private void OnDespawn(object sender, OnDespawnArgs args)
        {
            if (args.Npc is not Npc npc) { return; }
            var skills = NpcGameData.Instance.GetNpSkill(npc.TemplateId, SkillUseConditionKind.InIdle);
            if (skills == null) { return; }

            Logger.Info($"Npc objId: {npc.ObjId}, templateId: {npc.TemplateId} OnDespawn triggered.");

            foreach (var npcSkill in skills)
            {
                var skill = SkillManager.Instance.GetNpSkillTemplate(npcSkill);

                if (skill is null) { continue; }

                var skillCaster = SkillCaster.GetByType(SkillCasterType.Unit);
                skillCaster.ObjId = npc.ObjId;

                var skillTarget = SkillCastTarget.GetByType(SkillCastTargetType.Unit);
                skillTarget.ObjId = npc.ObjId;

                if (npc.Cooldowns.CheckCooldown(skill.Id)) { continue; }
                if (skill.Template.CooldownTime == 0)
                {
                    npc.Cooldowns.AddCooldown(skill.Id, uint.MaxValue); // выполняем один раз
                }
                Logger.Info($"Npc={npc.ObjId}:{npc.TemplateId} using skill={skill.Id}");
                skill.Use(npc, skillCaster, skillTarget);
            }
        }

        private void InAlert(object sender, InAlertArgs args)
        {
            Logger.Info($"Npc={args.Npc.ObjId}:{args.Npc.TemplateId} is in alert.");
        }

        private void InDead(object sender, InDeadArgs args)
        {
            Logger.Info($"Npc={args.Npc.ObjId} : {args.Npc.TemplateId} is in death state.");
        }

        /// <summary>
        /// Return true if the team Id matches to the team that owns the dungeon instance, false if not.
        /// </summary>
        /// <param name="player"></param>
        /// <returns></returns>
        private bool PlayerInSameTeam(Character player)
        {
            if (_isTeamOwned == false) { return false; }

            return _teamOwner.Id == TeamManager.Instance.GetTeamByObjId(player.ObjId).Id;
        }

        public bool IsOwner(Character character)
        {
            return _isTeamOwned == false && _characterOwner.Id == character.Id;
        }

        public bool IsPlayerInDungeon(uint characterId)
        {
            return _players.Contains(characterId);
        }

        /// <summary>
        /// Удаляем данжон, когда все игроки тимы вышли оффлайн
        /// </summary>
        /// <param name="delta"></param>
        private void LeaveDungeonTick(TimeSpan delta)
        {
            if (_teamOwner != null && _teamOwner.MembersOnlineCount() == 0 && _leaveRequests.IsEmpty)
            {
                TickManager.Instance.OnTick.UnSubscribe(LeaveDungeonTick);
                TickManager.Instance.OnTick.UnSubscribe(AreaClearTick);
                _ = DestroyTeamDungeon();
            }
            else if (_leaveRequests.IsEmpty)
            {
                return;
            }

            foreach (var leaveRequest in _leaveRequests)
            {
                if (DateTime.UtcNow <= leaveRequest.Value) { continue; }

                Logger.Info($"[Dungeon] instanceId: {_zoneInstanceId.InstanceId}, zoneId: {_zoneInstanceId.ZoneId}. Removing qualifying players from instance.");
                var character = WorldManager.Instance.GetCharacterById(leaveRequest.Key);
                if (character == null)
                {
                    Logger.Info($"[Dungeon] instanceId: {_zoneInstanceId.InstanceId}, zoneId: {_zoneInstanceId.ZoneId}. Player Id not found. Remove from processing..");
                    _leaveRequests.TryRemove(leaveRequest);
                    return;
                }
                if (character.InParty)
                {
                    if (PlayerInSameTeam(character))
                    {
                        Logger.Info($"[Dungeon] instanceId: {_zoneInstanceId.InstanceId}, zoneId: {_zoneInstanceId.ZoneId} player name: {character.Name} rejoined party, aborting.");
                        _leaveRequests.TryRemove(leaveRequest);
                        return;
                    }
                }

                LeaveInstance(character);
            }
        }

        public void RegisterIndunEvents()
        {
            Logger.Info($"Registering Indun Events...");
            foreach (var ev in IndunGameData.Instance.GetIndunEvents(_indunZone.ZoneGroupId))
            {
                ev?.Subscribe(_world);
            }
        }

        public void UnregisterIndunEvents()
        {
            Logger.Info($"Unregistering Indun Events...");
            foreach (var ev in IndunGameData.Instance.GetIndunEvents(_indunZone.ZoneGroupId))
            {
                ev?.UnSubscribe(_world);
            }
        }

        public void RegisterNpcEvents(Npc npc)
        {
            if (npc == null) { return; }

            var np = NpcGameData.Instance.GetNpSkill(npc.TemplateId);

            if (np is null) { return; }

            Logger.Info($"Registering Events for npc objId: {npc.ObjId}, templateId: {npc.TemplateId}");

            foreach (var skill in np)
            {
                switch (skill.SkillUseCondition)
                {
                    case SkillUseConditionKind.InCombat:
                        {
                            Logger.Info($"Found OnCombat event for npc objId: {npc.ObjId}, templateId: {npc.TemplateId}, skill {skill.SkillId}");
                            npc.Events.OnCombatStarted += OnCombatStarted;
                            break;
                        }
                    case SkillUseConditionKind.InIdle:
                        {
                            Logger.Info($"Found InIdle event for npc objId: {npc.ObjId}, templateId: {npc.TemplateId}, skill {skill.SkillId}");
                            npc.Events.InIdle += InIdle;
                            break;
                        }
                    case SkillUseConditionKind.OnDeath:
                        {
                            Logger.Info($"Found OnDeath event for npc objId: {npc.ObjId}, templateId: {npc.TemplateId}, skill {skill.SkillId}");
                            npc.Events.OnDeath += OnDeath;
                            break;
                        }
                    case SkillUseConditionKind.InAlert:
                        {
                            Logger.Info($"Found InAlert event for npc objId: {npc.ObjId}, templateId: {npc.TemplateId}, skill {skill.SkillId}");
                            npc.Events.InAlert += InAlert;
                            break;
                        }
                    case SkillUseConditionKind.InDead:
                        {
                            Logger.Info($"Found InDead event for npc objId: {npc.ObjId}, templateId: {npc.TemplateId}, skill {skill.SkillId}");
                            npc.Events.InDead += InDead;
                            break;
                        }
                    case SkillUseConditionKind.OnSpawn:
                        {
                            Logger.Info($"Found OnSpawn event for npc objId: {npc.ObjId}, templateId: {npc.TemplateId}, skill {skill.SkillId}");
                            npc.Events.OnSpawn += OnSpawn;
                            break;
                        }
                    case SkillUseConditionKind.OnDespawn:
                        {
                            Logger.Info($"Found OnDespawn event for npc objId: {npc.ObjId}, templateId: {npc.TemplateId}, skill {skill.SkillId}");
                            npc.Events.OnDespawn += OnDespawn;
                            break;
                        }
                    default:
                        {
                            Logger.Info("No SkillUseCondition found for skill " + skill.SkillId + " for npc " + npc.TemplateId);
                            Logger.Info($"Found InDead event for npc objId: {npc.ObjId}, templateId: {npc.TemplateId}");
                            break;
                        }
                }
            }
        }

        public void UnregisterNpcEvents(Npc npc)
        {
            var np = NpcGameData.Instance.GetNpSkill(npc.TemplateId);
            if (np is null) { return; }

            Logger.Info("Unregistering Npc Events for " + npc.TemplateId);

            foreach (var skill in np)
            {
                switch (skill.SkillUseCondition)
                {
                    case SkillUseConditionKind.InCombat:
                        {
                            Logger.Info($"Found OnCombat event for npc {npc.TemplateId}, skill {skill.SkillId}");
                            npc.Events.OnCombatStarted -= OnCombatStarted;
                            break;
                        }
                    case SkillUseConditionKind.InIdle:
                        {
                            Logger.Info($"Found InIdle event for npc {npc.TemplateId}, skill {skill.SkillId}");
                            npc.Events.InIdle -= InIdle;
                            break;
                        }
                    case SkillUseConditionKind.OnDeath:
                        {
                            Logger.Info($"Found OnDeath event for npc {npc.TemplateId}, skill {skill.SkillId}");
                            npc.Events.OnDeath -= OnDeath;
                            break;
                        }
                    case SkillUseConditionKind.InAlert:
                        {
                            Logger.Info($"Found InAlert event for npc {npc.TemplateId}, skill {skill.SkillId}");
                            npc.Events.InAlert += InAlert;
                            break;
                        }
                    case SkillUseConditionKind.InDead:
                        {
                            Logger.Info($"Found InDead event for npc {npc.TemplateId}, skill {skill.SkillId}");
                            npc.Events.InDead += InDead;
                            break;
                        }
                    case SkillUseConditionKind.OnSpawn:
                        {
                            Logger.Info($"Found OnSpawn event for npc {npc.TemplateId}, skill {skill.SkillId}");
                            npc.Events.OnSpawn += OnSpawn;
                            break;
                        }
                    case SkillUseConditionKind.OnDespawn:
                        {
                            Logger.Info($"Found OnDespawn event for npc {npc.TemplateId}, skill {skill.SkillId}");
                            npc.Events.OnDespawn += OnDespawn;
                            break;
                        }
                    default:
                        {
                            Logger.Info($"No SkillUseCondition found for skill {skill.SkillId}, for npc {npc.TemplateId}");
                            break;
                        }
                }
            }
        }

        public bool IsRoomCleared(uint roomId)
        {
            return _rooms.TryGetValue(roomId, out var cleared) && cleared;
        }

        public void SetRoomCleared(uint roomId)
        {
            _rooms[roomId] = true;
        }

        public uint GetDungeonWorldId()
        {
            return _world.Id;
        }

        public uint GetDungeonTemplateId()
        {
            return _world.TemplateId;
        }

        private void AreaClearTick(TimeSpan delta)
        {
            lock (_lock)
            {
                foreach (var ev in IndunGameData.Instance.GetIndunEvents(_indunZone.ZoneGroupId))
                {
                    if (ev is not IndunEventNoAliveChInRooms room) { continue; }

                    if (IsRoomCleared(room.RoomId)) { return; }

                    var indunRoom = IndunGameData.Instance.GetRoom(room.RoomId);
                    if (_world != null)
                    {
                        var doodad = room.GetRoomDoodad(_world.Id);

                        if (doodad == null) { continue; }

                        var radiusCount = WorldManager.GetAround<Character>(doodad, indunRoom.Radius)
                            .Where(o => o.GetDistanceTo(doodad) <= indunRoom.Radius).ToList().Count;

                        Logger.Info($"Character:{radiusCount} in room:{room.RoomId}");

                        if (radiusCount == 0 && room.GetRoomPlayerCount(_world.Id) != 0)
                        {
                            IndunManager.DoIndunActions(ev.StartActionId, _world); // запускаем задачу при первом входе в данжон
                        }

                        room.SetRoomPlayerCount(_world.Id, (uint)radiusCount);
                    }
                }
            }
        }

        public int GetPlayerCount()
        {
            return _players.Count;
        }

        public void WaitingDungeonAccessAttemptsCleared(TimeSpan delta)
        {
            if (!IndunManager.Instance.GetWaitingDungeonAccess(this))
            {
                IndunManager.Instance.SetWaitingDungeonAccess(this, true);
                return;
            }
            TickManager.Instance.OnTick.UnSubscribe(WaitingDungeonAccessAttemptsCleared);
            IndunManager.Instance.ClearAttemts(this);
        }
    }
}
