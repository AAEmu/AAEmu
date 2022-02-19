﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

using AAEmu.Commons.IO;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.OpenPortal;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Models.Tasks.World;
using AAEmu.Game.Utils;
using AAEmu.Game.Utils.DB;

using NLog;

using Portal = AAEmu.Game.Models.Game.Portal;

namespace AAEmu.Game.Core.Managers
{
    public class PortalManager : Singleton<PortalManager>
    {
        private readonly Logger _log = LogManager.GetCurrentClassLogger();

        private Dictionary<uint, uint> _allDistrictPortalsKey;
        private Dictionary<uint, Portal> _allDistrictPortals;
        private Dictionary<uint, OpenPortalReagents> _openPortalInlandReagents;
        private Dictionary<uint, OpenPortalReagents> _openPortalOutlandReagents;
        private Dictionary<uint, DistrictReturnPoints> _districtReturnPoints;

        public Portal GetPortalBySubZoneId(uint subZoneId)
        {
            return _allDistrictPortals != null && _allDistrictPortals.ContainsKey(subZoneId)
                ? _allDistrictPortals[subZoneId]
                : null;
        }

        public Portal GetPortalById(uint id)
        {
            return _allDistrictPortalsKey != null && _allDistrictPortalsKey.ContainsKey(id)
                ? _allDistrictPortals.ContainsKey(_allDistrictPortalsKey[id])
                    ? _allDistrictPortals[_allDistrictPortalsKey[id]]
                    : null
                : null;
        }

        /// <summary>
        /// GetDistrictReturnPoint - вернуть точку возврата для соответствующего DistrictId
        /// </summary>
        /// <param name="districtId"></param>
        /// <returns>ReturnPointId</returns>
        public uint GetDistrictReturnPoint(uint districtId)
        {
            return (
                from point in _districtReturnPoints
                where point.Value.DistrictId == districtId
                select point.Value.ReturnPointId).FirstOrDefault();
        }

        /// <summary>
        /// GetDistrictReturnPoint - вернуть точку возврата для соответствующего DistrictId и FactionId, так как точки для фракций могут быть разные
        /// </summary>
        /// <param name="districtId"></param>
        /// <param name="factionId"></param>
        /// <returns>ReturnPointId</returns>
        public uint GetDistrictReturnPoint(uint districtId, uint factionId)
        {
            return (
                from point in _districtReturnPoints
                where point.Value.DistrictId == districtId
                where point.Value.FactionId == factionId
                select point.Value.ReturnPointId).FirstOrDefault();
        }

        public void Load()
        {
            _openPortalInlandReagents = new Dictionary<uint, OpenPortalReagents>();
            _openPortalOutlandReagents = new Dictionary<uint, OpenPortalReagents>();
            _allDistrictPortals = new Dictionary<uint, Portal>();
            _allDistrictPortalsKey = new Dictionary<uint, uint>();
            _districtReturnPoints = new Dictionary<uint, DistrictReturnPoints>();

            _log.Info("Loading Portals ...");

            #region FileManager

            //var filePath = Path.Combine(FileManager.AppPath, "Data", "Portal", "SubZonePortalCoords.json");
            var filePath = Path.Combine(FileManager.AppPath, "Data", "Portal", "return_points.json");

            if (!File.Exists(filePath))
                throw new IOException($"File {filePath} doesn't exists !");

            var contents = FileManager.GetFileContents(filePath);

            if (string.IsNullOrWhiteSpace(contents))
                throw new IOException($"File {filePath} is empty !");

            if (JsonHelper.TryDeserializeObject(contents, out List<Portal> portals, out _))
                foreach (var portal in portals)
                {
                    if (!_allDistrictPortals.ContainsKey(portal.SubZoneId))
                    {
                        _allDistrictPortals.Add(portal.SubZoneId, portal);
                    }
                    if (!_allDistrictPortalsKey.ContainsKey(portal.Id))
                    {
                        _allDistrictPortalsKey.Add(portal.Id, portal.SubZoneId);
                    }
                }
            else
                throw new Exception($"PortalManager: Parse {filePath} file");

            _log.Info("Loaded {0} District Portals", _allDistrictPortals.Count);

            #endregion

            #region Sqlite

            using (var connection = SQLite.CreateConnection())
            {
                // NOTE - priority -> to remove item from inventory first
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM open_portal_inland_reagents";
                    command.Prepare();
                    using var reader = new SQLiteWrapperReader(command.ExecuteReader());
                    while (reader.Read())
                    {
                        var template = new OpenPortalReagents
                        {
                            Id = reader.GetUInt32("id"),
                            OpenPortalEffectId = reader.GetUInt32("open_portal_effect_id"),
                            ItemId = reader.GetUInt32("item_id"),
                            Amount = reader.GetInt32("amount"),
                            Priority = reader.GetInt32("priority")
                        };
                        _openPortalInlandReagents.Add(template.Id, template);
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM open_portal_outland_reagents";
                    command.Prepare();
                    using var reader = new SQLiteWrapperReader(command.ExecuteReader());
                    while (reader.Read())
                    {
                        var template = new OpenPortalReagents
                        {
                            Id = reader.GetUInt32("id"),
                            OpenPortalEffectId = reader.GetUInt32("open_portal_effect_id"),
                            ItemId = reader.GetUInt32("item_id"),
                            Amount = reader.GetInt32("amount"),
                            Priority = reader.GetInt32("priority")
                        };
                        _openPortalOutlandReagents.Add(template.Id, template);
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM district_return_points";
                    command.Prepare();
                    using var reader = new SQLiteWrapperReader(command.ExecuteReader());
                    while (reader.Read())
                    {
                        var template = new DistrictReturnPoints
                        {
                            Id = reader.GetUInt32("id"),
                            DistrictId = reader.GetUInt32("district_id"),
                            FactionId = reader.GetUInt32("faction_id"),
                            ReturnPointId = reader.GetUInt32("return_point_id")
                        };
                        if (!_districtReturnPoints.ContainsKey(template.Id))
                            _districtReturnPoints.Add(template.Id, template);
                    }
                }
            }
            _log.Info("Loaded Portal Info");
            #endregion
        }

        private static bool CheckItemAndRemove(Character owner, uint itemId, int amount)
        {
            if (!owner.Inventory.CheckItems(SlotType.Inventory, itemId, amount)) return false;
            owner.Inventory.Bag.ConsumeItem(ItemTaskType.Teleport, itemId, amount, null);
            return true;
            /*
            var items = owner.Inventory.RemoveItem(itemId, amount);
            var tasks = new List<ItemTask>();
            foreach (var (item, count) in items)
            {
                if (item.Count == 0)
                    tasks.Add(new ItemRemove(item));
                else
                    tasks.Add(new ItemCountUpdate(item, -count));
            }
            owner.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.Teleport, tasks, new List<ulong>()));
            return true;
            */
        }

        private bool CheckCanOpenPortal(Character owner, uint targetZoneId)
        {
            var targetContinent = ZoneManager.Instance.GetTargetIdByZoneId(targetZoneId);
            var ownerContinent = ZoneManager.Instance.GetTargetIdByZoneId(owner.Transform.ZoneId);

            if (targetContinent == ownerContinent)
            {
                foreach (var (_, value) in _openPortalInlandReagents)
                {
                    if (CheckItemAndRemove(owner, value.ItemId, value.Amount)) return true;
                }
            }
            else
            {
                foreach (var (_, value) in _openPortalOutlandReagents)
                {
                    if (CheckItemAndRemove(owner, value.ItemId, value.Amount)) return true;
                }
            }
            return false; // Not enough items
        }

        private static void MakePortal(Unit owner, bool isExit, Portal portalInfo, SkillObjectUnk1 portalEffectObj)
        {
            // 3891 - Portal Entrance
            // 6949 - Portal Exit
            var portalPointDestination = new Transform(null, null,
                WorldManager.Instance.GetWorldByZone(portalInfo.ZoneId).Id, portalInfo.ZoneId,
                WorldManager.DefaultInstanceId, portalInfo.X, portalInfo.Y, portalInfo.Z,
                0f, 0f, portalInfo.ZRot);
            var portalPointLocation = new Transform(null, null,
                owner.Transform.WorldId, owner.Transform.ZoneId, owner.Transform.InstanceId,
                portalEffectObj.X, portalEffectObj.Y, portalEffectObj.Z,
                owner.Transform.World.Rotation.X, owner.Transform.World.Rotation.Y, owner.Transform.World.Rotation.Z);
            var templateId = isExit ? 6949u : 3891u; // TODO - better way? maybe not hardcoded
            var template = NpcManager.Instance.GetTemplate(templateId);
            var portalUnitModel = new Models.Game.Units.Portal
            {
                ObjId = ObjectIdManager.Instance.GetNextId(),
                OwnerId = ((Character)owner).Id,
                TemplateId = templateId,
                Template = template,
                ModelId = template.ModelId,
                Faction = owner.Faction, // INFO - FactionManager.Instance.GetFaction(template.FactionId)
                Level = template.Level,
                Transform = isExit ? portalPointDestination : portalPointLocation,
                Name = portalInfo.Name,
                Hp = 955, // BUG - portal.MaxHp does not work 1.0
                Mp = 290, // TODO - portal.MaxMp
                TeleportPosition = portalPointDestination
            };
            portalUnitModel.Spawn();

            var killTask = new KillPortalTask(portalUnitModel);
            TaskManager.Instance.Schedule(killTask, TimeSpan.FromSeconds(30));
        }

        public void OpenPortal(Character owner, SkillObjectUnk1 portalEffectObj)
        {
            var portalInfo = owner.Portals.GetPortalInfo((uint)portalEffectObj.Id);
            if (!CheckCanOpenPortal(owner, portalInfo.ZoneId)) return;

            MakePortal(owner, false, portalInfo, portalEffectObj);   // Entrance (green)
            MakePortal(owner, true, portalInfo, portalEffectObj);    // Exit (yellow)
        }

        public void UsePortal(Character character, uint objId)
        {
            // TODO - Cooldown between portals
            var portalInfo = (Models.Game.Units.Portal)WorldManager.Instance.GetNpc(objId);
            if (portalInfo == null) return;

            character.DisabledSetPosition = true;
            // TODO - UnitPortalUsed
            // TODO - Maybe need unitState?
            // TODO - Reason, ErrorMessage
            character.SendPacket(new SCTeleportUnitPacket(0, 0, portalInfo.TeleportPosition.World.Position.X,
                portalInfo.TeleportPosition.World.Position.Y, portalInfo.TeleportPosition.World.Position.Z,
                portalInfo.TeleportPosition.World.Rotation.Z));
        }

        public void DeletePortal(Character owner, byte type, uint id)
        {
            var isPrivate = type != 1;
            var portalInfo = owner.Portals.GetPortalInfo(id);
            if (portalInfo == null) return;
            owner.Portals.RemoveFromBookPortal(portalInfo, isPrivate);
        }

        public Portal GetClosestReturnPortal(Character character)
        {
            var cxyz = character.Transform.World.Position;
            var distance = 5000f;
            var portal = new Portal();

            foreach (var (_, value) in _allDistrictPortals)
            {
                if (!value.Name.ToLower().Contains("respawn")) { continue; }
                var pxyz = new Vector3(value.X, value.Y, value.Z);
                var dist = MathUtil.CalculateDistance(cxyz, pxyz);
                if (!(dist < distance)) { continue; }
                distance = dist;
                portal = value;
            }
            return portal;
        }
    }
}
