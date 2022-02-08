using System;
using System.Collections.Generic;
using System.Numerics;

using AAEmu.Commons.IO;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.World;

using NLog;

namespace AAEmu.Game.Core.Managers.World
{
    public class SphereQuestManager : Singleton<SphereQuestManager>
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();

        private Dictionary<uint, SphereQuest> _sphereQuests;

        private readonly List<SphereQuestTrigger> _sphereQuestTriggers;
        private List<SphereQuestTrigger> _addQueue;
        private List<SphereQuestTrigger> _removeQueue;

        private object _addLock = new object();
        private object _remLock = new object();

        public SphereQuestManager()
        {
            _sphereQuestTriggers = new List<SphereQuestTrigger>();
            _addQueue = new List<SphereQuestTrigger>();
            _removeQueue = new List<SphereQuestTrigger>();
        }

        public void Initialize()
        {
            TickManager.Instance.OnTick.Subscribe(Tick, TimeSpan.FromMilliseconds(500), true);
        }
        public void Load()
        {
            _sphereQuests = LoadSphereQuests();
        }

        public void AddSphereQuestTrigger(SphereQuestTrigger trigger)
        {
            lock (_addLock)
            {
                _addQueue.Add(trigger);
            }
        }

        public void RemoveSphereQuestTrigger(SphereQuestTrigger trigger)
        {
            lock (_remLock)
            {
                _removeQueue.Add(trigger);
            }
        }

        public void Tick(TimeSpan delta)
        {
            try
            {
                lock (_addLock)
                {
                    if (_addQueue?.Count > 0)
                        _sphereQuestTriggers.AddRange(_addQueue);
                    _addQueue = new List<SphereQuestTrigger>();
                }

                foreach (var trigger in _sphereQuestTriggers)
                {
                    if (trigger?.Owner?.Region?.HasPlayerActivity() ?? false)
                        trigger?.Tick(delta);
                }

                lock (_remLock)
                {
                    foreach (var triggerToRemove in _removeQueue)
                    {
                        _sphereQuestTriggers.Remove(triggerToRemove);
                    }

                    _removeQueue = new List<SphereQuestTrigger>();
                }
            }
            catch (Exception e)
            {
                _log.Error(e, "Error in SphereQuestTrigger tick !");
            }
        }

        public Dictionary<uint, SphereQuest> LoadSphereQuests()
        {
            var spheres = new List<SphereQuest>();
            var sphereQuests = new Dictionary<uint, SphereQuest>();

            var contents = FileManager.GetFileContents($"{FileManager.AppPath}Data/quest_sign_spheres.json");
            if (string.IsNullOrWhiteSpace(contents))
                _log.Warn($"File {FileManager.AppPath}Data/quest_sign_spheres.json doesn't exists or is empty.");
            else
            {
                try
                {
                    JsonHelper.TryDeserializeObject(contents, out spheres, out _);
                    foreach (var sphere in spheres)
                    {
                        if (sphereQuests.ContainsKey(sphere.ComponentID))
                            continue;

                        // конвертируем координаты из локальных в мировые, сразу при считывании из файла
                        var _xyz = new Vector3(sphere.X, sphere.Y, sphere.Z);
                        var xyz = ZoneManager.Instance.ConvertToWorldCoordinates(sphere.ZoneID, _xyz);
                        sphere.X = xyz.X;
                        sphere.Y = xyz.Y;
                        sphere.Z = xyz.Z;
                        sphereQuests.Add(sphere.ComponentID, sphere);
                    }
                }
                catch (Exception)
                {
                    throw new Exception($"SpawnManager: Parse {FileManager.AppPath}Data/quest_sign_spheres.json file");
                }
            }
            return sphereQuests;
        }

        public SphereQuest GetQuestSpheres(uint ComponentID)
        {
            return _sphereQuests.ContainsKey(ComponentID) ? _sphereQuests[ComponentID] : null;
        }

        public List<SphereQuestTrigger> GetSphereQuestTriggers()
        {
            return _sphereQuestTriggers;
        }

    }
}
