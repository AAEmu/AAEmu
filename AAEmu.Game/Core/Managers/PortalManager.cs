using System;
using System.Collections.Generic;
using System.IO;
using AAEmu.Commons.IO;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.OpenPortal;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils.DB;
using NLog;
using Portal = AAEmu.Game.Models.Game.Portal;

namespace AAEmu.Game.Core.Managers
{
    public class PortalManager : Singleton<PortalManager>
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();
        private Dictionary<uint, Portal> _allDistrictPortals;
        private Dictionary<uint, OpenPortalReagents> _openPortalInlandReagents;
        private Dictionary<uint, OpenPortalReagents> _openPortalOutlandReagents;

        public Portal GetPortalBySubZoneId(uint subZoneId)
        {
            return _allDistrictPortals.ContainsKey(subZoneId) ? _allDistrictPortals[subZoneId] : null;
        }

        public void Load()
        {
            _openPortalInlandReagents = new Dictionary<uint, OpenPortalReagents>();
            _openPortalOutlandReagents = new Dictionary<uint, OpenPortalReagents>();
            _allDistrictPortals = new Dictionary<uint, Portal>();
            _log.Info("Loading Portals ...");

            #region FileManager

            var filePath = $"{FileManager.AppPath}Data/Portal/SubZonePortalCoords.json";
            var contents = FileManager.GetFileContents(filePath);

            if (string.IsNullOrWhiteSpace(contents))
                throw new IOException($"File {filePath} doesn't exists or is empty.");

            if (JsonHelper.TryDeserializeObject(contents, out List<Portal> portals, out _))
                foreach (var portal in portals)
                    _allDistrictPortals.Add(portal.SubZoneId, portal);
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

        public bool CheckCanOpenPortal()
        {
            // TODO
            return true;
        }

        public bool RemoveItemFromInventory(Character owner)
        {
            // TODO
            return true;
        }

        private void MakePortal(Character owner, bool isExit, uint portalId)
        {
            // 3891 - Portal Entrance
            // 6949 - Portal Exit

            var portalInfo = owner.Portals.GetPortalInfo(portalId);
            var portalLocation = new Point
            {
                X = portalInfo.X,
                Y = portalInfo.Y,
                Z = portalInfo.Z,
                ZoneId = portalInfo.ZoneId,
                RotationZ = Helpers.ConvertRotation(Convert.ToInt16(portalInfo.ZRot))
            };
            var templateId = isExit ? 6949u : 3891u; // TODO - better way? maybe not hardcoded
            var template = NpcManager.Instance.GetTemplate(templateId);
            var portalUnitModel = new Models.Game.Units.Portal
            {
                ObjId = ObjectIdManager.Instance.GetNextId(),
                Master = owner,
                TemplateId = templateId,
                Template = template,
                ModelId = template.ModelId,
                Faction = owner.Faction, // INFO - FactionManager.Instance.GetFaction(template.FactionId)
                Level = template.Level,
                Position = isExit ? portalLocation : owner.Position.Clone(),
                Name = portalInfo.Name,
                Hp = 862, // BUG - portal.MaxHp does not work
                Mp = 290, // TODO - portal.MaxMp
                TeleportPosition = portalLocation
            };
            portalUnitModel.Spawn();
        }

        public void OpenPortal(Character owner, SkillObjectUnk1 obj)
        {
            if (!RemoveItemFromInventory(owner))
            {
                // TODO - Send error message? Not enough Hereafter Stone
                return;
            }

            MakePortal(owner, false, obj.Id);   // Entrance (green)
            MakePortal(owner, true, obj.Id);    // Exit (yellow)
        }

        public void UsePortal(Character character, uint objId)
        {
            // TODO - Cooldown between portals
            // TODO - Reason, ErrorMessage
            var portalInfo = (Models.Game.Units.Portal)WorldManager.Instance.GetNpc(objId);
            if (portalInfo == null) return;

            character.DisabledSetPosition = true;
            character.BroadcastPacket(new SCTeleportUnitPacket(0, 0, portalInfo.TeleportPosition.X,
                portalInfo.TeleportPosition.Y, portalInfo.TeleportPosition.Z, portalInfo.TeleportPosition.RotationZ), true);
        }
    }
}
