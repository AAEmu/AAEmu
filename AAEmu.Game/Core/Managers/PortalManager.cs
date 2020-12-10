﻿using System;
using System.Collections.Generic;
using System.IO;
using AAEmu.Commons.IO;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.OpenPortal;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Tasks.World;
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

        public Portal GetPortalBySubZoneId(uint subZoneId)
        {
            return _allDistrictPortals.ContainsKey(subZoneId) ? _allDistrictPortals[subZoneId] : null;
        }

        public Portal GetPortalById(uint id)
        {
            return _allDistrictPortalsKey.ContainsKey(id) ? (_allDistrictPortals.ContainsKey(_allDistrictPortalsKey[id]) ? _allDistrictPortals[_allDistrictPortalsKey[id]] : null) : null;
        }

        public void Load()
        {
            _openPortalInlandReagents = new Dictionary<uint, OpenPortalReagents>();
            _openPortalOutlandReagents = new Dictionary<uint, OpenPortalReagents>();
            _allDistrictPortals = new Dictionary<uint, Portal>();
            _allDistrictPortalsKey = new Dictionary<uint, uint>();
            _log.Info("Loading Portals ...");

            #region FileManager

            var filePath = $"{FileManager.AppPath}Data/Portal/SubZonePortalCoords.json";
            var contents = FileManager.GetFileContents(filePath);

            if (string.IsNullOrWhiteSpace(contents))
                throw new IOException($"File {filePath} doesn't exists or is empty.");

            if (JsonHelper.TryDeserializeObject(contents, out List<Portal> portals, out _))
                foreach (var portal in portals)
                {
                    _allDistrictPortals.Add(portal.SubZoneId, portal);
                    _allDistrictPortalsKey.Add(portal.Id, portal.SubZoneId);
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
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
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
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM open_portal_outland_reagents";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
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
                }
            }
            _log.Info("Loaded Portal Info");

            #endregion

        }

        private static bool CheckItemAndRemove(Character owner, uint itemId, int amount)
        {
            if (!owner.Inventory.CheckItems(SlotType.Inventory, itemId, amount)) return false;
            owner.Inventory.Bag.ConsumeItem(ItemTaskType.Teleport, itemId, amount,null);
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
            var ownerContinent = ZoneManager.Instance.GetTargetIdByZoneId(owner.Position.ZoneId);

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
            var portalPointDestination = new Point
            {
                X = portalInfo.X,
                Y = portalInfo.Y,
                Z = portalInfo.Z,
                ZoneId = portalInfo.ZoneId,
                RotationZ = Helpers.ConvertRotation(Convert.ToInt16(portalInfo.ZRot)),
                WorldId = WorldManager.Instance.GetWorldByZone(portalInfo.ZoneId).Id
            };
            var portalPointLocation = new Point
            {
                X = portalEffectObj.X,
                Y = portalEffectObj.Y,
                Z = portalEffectObj.Z,
                ZoneId = owner.Position.ZoneId,
                RotationZ = owner.Position.RotationZ,
                WorldId = owner.Position.WorldId
            };
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
                Position = isExit ? portalPointDestination : portalPointLocation,
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
            // TODO - Maybe need unitstate?
            // TODO - Reason, ErrorMessage
            character.SendPacket(new SCTeleportUnitPacket(0, 0, portalInfo.TeleportPosition.X,
                portalInfo.TeleportPosition.Y, portalInfo.TeleportPosition.Z, portalInfo.TeleportPosition.RotationZ));
        }

        public void DeletePortal(Character owner, byte type, uint id)
        {
            var isPrivate = type != 1;
            var portalInfo = owner.Portals.GetPortalInfo(id);
            if (portalInfo == null) return;
            owner.Portals.RemoveFromBookPortal(portalInfo, isPrivate);
        }
    }
}
