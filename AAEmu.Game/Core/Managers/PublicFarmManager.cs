using System;
using System.Collections.Generic;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.CommonFarm.Static;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Tasks.PublicFarm;

using NLog;

namespace AAEmu.Game.Core.Managers
{
    public class PublicFarmManager : Singleton<PublicFarmManager>
    {
        private static Logger Logger = LogManager.GetCurrentClassLogger();

        private List<uint> _farmZones;
        private Dictionary<uint, FarmType> _farmZonesTypes;

        private const int protectedTime = 1;
        private const int deletionTime = 3;

        public void Initialize()
        {
            Logger.Info("Initialising Public Farm Manager...");
            PublicFarmTickStart();
        }

        public void PublicFarmTickStart()
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
                if (DateTime.UtcNow >= doodad.PlantTime.AddDays(protectedTime))
                {
                    if (doodad.FarmType != (uint)FarmType.Invalid)
                    {
                        _deleted.Add(doodad);
                    }
                }
            }

            foreach (var doodad in _deleted)
            {
                doodad.Delete();
            }
        }

        public bool InPublicFarm(uint worldId, float x, float y)
        {
            var subZoneList = SubZoneManager.Instance.GetSubZoneByPosition(worldId, x, y);

            if (subZoneList.Count > 0)
            {
                foreach (var subzoneId in subZoneList)
                {
                    if (_farmZones.Contains(subzoneId))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public uint GetFarmId(uint worldId, float x, float y)
        {
            var subZoneList = SubZoneManager.Instance.GetSubZoneByPosition(worldId, x, y);

            if (subZoneList.Count > 0)
            {
                foreach (var subzoneId in subZoneList)
                {
                    if (_farmZones.Contains(subzoneId))
                    {
                        return subzoneId;
                    }
                }
            }

            return 0;
        }

        public FarmType GetFarmType(uint worldId, float x, float y)
        {
            var subzoneId = GetFarmId(worldId, x, y);

            if (_farmZonesTypes.TryGetValue(subzoneId, out var type))
            {
                return type;
            }
            else
                return FarmType.Invalid;
        }

        public bool CanPlace(Character character, FarmType farmType, uint doodadId)
        {
            var allPlanted = GetCommonFarmDoodads(character);

            if (allPlanted.TryGetValue(farmType, out var doodadList))
            {
                if (doodadList.Count >= CommonFarmGameData.Instance.GetFarmGroupMaxCount(farmType))
                {
                    character.SendErrorMessage(Models.Game.ErrorMessageType.CommonFarmCountOver);
                    return false;
                }
            }

            var alloweDoodads = CommonFarmGameData.Instance.GetAllowedDoodads(farmType);

            foreach (var id in alloweDoodads)
            {
                if (doodadId == id)
                {
                    return true;
                }
            }

            character.SendErrorMessage(Models.Game.ErrorMessageType.CommonFarmNotAllowedType);
            return false;
        }

        public Dictionary<FarmType, List<Doodad>> GetCommonFarmDoodads(Character character)
        {
            var _list = new Dictionary<FarmType, List<Doodad>>();

            var playerDoodads = SpawnManager.Instance.GetPlayerDoodads(character.Id);

            foreach (var doodad in playerDoodads)
            {
                if (InPublicFarm(character.Transform.WorldId, doodad.Transform.World.Position.X, doodad.Transform.World.Position.Y))
                {
                    var farmType = GetFarmType(character.Transform.WorldId, doodad.Transform.World.Position.X, doodad.Transform.World.Position.Y);

                    if (doodad.FarmType == (uint)farmType)
                    {
                        if (!_list.ContainsKey(farmType))
                            _list.Add(farmType, new List<Doodad>());
                        _list[farmType].Add(doodad);
                    }
                }
            }

            return _list;
        }

        public bool IsProtected(Doodad doodad)
        {
            var protectionTime = doodad.PlantTime.AddDays(protectedTime);

            if (doodad.PlantTime >= protectionTime)
            {
                return false;
            }

            return true;
        }

        public void Load()
        {
            //common farm subzone ID's
            _farmZones = new List<uint>();
            _farmZones.Add(998);
            _farmZones.Add(966);
            _farmZones.Add(968);
            _farmZones.Add(967);
            _farmZones.Add(974);

            _farmZonesTypes = new Dictionary<uint, FarmType>();
            _farmZonesTypes.Add(998, FarmType.Farm);
            _farmZonesTypes.Add(966, FarmType.Farm);
            _farmZonesTypes.Add(968, FarmType.Nursery);
            _farmZonesTypes.Add(967, FarmType.Ranch);
            _farmZonesTypes.Add(974, FarmType.Stable);
        }

    }
}
