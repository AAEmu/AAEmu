using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.CommonFarm.Static;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Tasks.PublicFarm;

using NLog;

namespace AAEmu.Game.Core.Managers
{
    public class PublicFarmManager : Singleton<PublicFarmManager>
    {
        private static Logger Logger = LogManager.GetCurrentClassLogger();

        private Dictionary<uint, FarmType> _farmZones;

        public void Initialize()
        {
            Logger.Info("Initialising Public Farm Manager...");
            PublicFarmTickStart();
        }

        private void PublicFarmTickStart()
        {
            Logger.Warn("PublicFarmTickTask: Started");

            var lpTickStartTask = new PublicFarmTickStartTask();
            TaskManager.Instance.Schedule(lpTickStartTask, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }

        public void PublicFarmTick()
        {
            var _deleted = new List<Doodad>();
            foreach (var doodad in SpawnManager.Instance.GetAllPlayerDoodads())
            {
                var guardTime = CommonFarmGameData.Instance.GetDoodadGuardTime(doodad.Template.GroupId);
                if (DateTime.UtcNow < doodad.PlantTime.AddSeconds(guardTime)) { continue; }
                if (doodad.FarmType == FarmType.Invalid) { continue; }

                // закончилось время защиты
                doodad.OwnerId = 0;
                doodad.OwnerType = DoodadOwnerType.System;
                doodad.FarmType = FarmType.Invalid;
                doodad.Save();
                _deleted.Add(doodad);
            }

            foreach (var doodad in _deleted)
            {
                //doodad.Delete();
                SpawnManager.Instance.RemovePlayerDoodad(doodad);
            }
        }

        public bool InPublicFarm(uint worldId, Vector3 pos)
        {
            var subZoneList = SubZoneManager.Instance.GetSubZoneByPosition(worldId, pos);
            return subZoneList.Count > 0 && subZoneList.Any(subZoneId => _farmZones.ContainsKey(subZoneId));
        }

        private uint GetFarmId(uint worldId, Vector3 pos)
        {
            var subZoneList = SubZoneManager.Instance.GetSubZoneByPosition(worldId, pos);

            return subZoneList.Count > 0 ? subZoneList.FirstOrDefault(subZoneId => _farmZones.ContainsKey(subZoneId)) : 0;
        }

        public FarmType GetFarmType(uint worldId, Vector3 pos)
        {
            var subZoneId = GetFarmId(worldId, pos);
            return _farmZones.GetValueOrDefault(subZoneId, FarmType.Invalid);
        }

        public bool CanPlace(Character character, FarmType farmType, uint doodadId)
        {
            var allPlanted = GetCommonFarmDoodads(character);
            if (allPlanted.TryGetValue(farmType, out var doodadList))
            {
                if (doodadList.Count >= CommonFarmGameData.Instance.GetFarmGroupMaxCount(farmType))
                {
                    character.SendErrorMessage(ErrorMessageType.CommonFarmCountOver);
                    return false;
                }
            }

            var allowedDoodads = CommonFarmGameData.Instance.GetAllowedDoodads(farmType);
            if (allowedDoodads.Any(id => doodadId == id))
            {
                return true;
            }

            character.SendErrorMessage(ErrorMessageType.CommonFarmNotAllowedType);
            return false;
        }

        public Dictionary<FarmType, List<Doodad>> GetCommonFarmDoodads(Character character)
        {
            var list = new Dictionary<FarmType, List<Doodad>>();

            var playerDoodads = SpawnManager.Instance.GetPlayerDoodads(character.Id);

            foreach (var doodad in playerDoodads)
            {
                if (InPublicFarm(character.Transform.WorldId, doodad.Transform.World.Position))
                {
                    var farmType = GetFarmType(character.Transform.WorldId, doodad.Transform.World.Position);

                    if (doodad.FarmType == farmType)
                    {
                        if (!list.ContainsKey(farmType))
                            list.Add(farmType, new List<Doodad>());
                        list[farmType].Add(doodad);
                    }
                }
            }

            return list;
        }

        public static bool IsProtected(Doodad doodad)
        {
            var guardTime = CommonFarmGameData.Instance.GetDoodadGuardTime(doodad.Template.GroupId);
            var protectionTime = doodad.PlantTime.AddSeconds(guardTime);

            return doodad.PlantTime < protectionTime;
        }

        public void Load()
        {
            //common farm subzone ID's
            _farmZones = new Dictionary<uint, FarmType>();
            _farmZones.Add(998, FarmType.Farm);
            _farmZones.Add(966, FarmType.Farm);
            _farmZones.Add(968, FarmType.Nursery);
            _farmZones.Add(967, FarmType.Ranch);
            _farmZones.Add(974, FarmType.Stable);
        }

    }
}
