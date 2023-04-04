using System;
using System.Collections.Generic;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Shipyard;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Tasks.FishSchools;
using AAEmu.Game.Utils;

using NLog;

namespace AAEmu.Game.Core.Managers
{
    public class FishSchoolManager : Singleton<FishSchoolManager>
    {
        protected static Logger _log = LogManager.GetCurrentClassLogger();
        private const double Delay = 500;
        private List<Doodad> FishSchools = new List<Doodad>();
        private bool _loaded = false;

        public void Initialize()
        {
            _log.Info("Initialising FishSchool Manager...");
        }

        public void Load()
        {
            if (_loaded)
                return;

            _log.Info("Loading FishSchool...");
            var doodads = WorldManager.Instance.GetAllDoodads();
            if (doodads != null)
            {
                foreach (var d in doodads)
                {
                    // ID=6447, "Freshwater Fish School", ID=6448, "Saltwater Fish School"
                    if (d.TemplateId == 6447 || d.TemplateId == 6448)
                    {
                        FishSchools.Add(d);
                    }
                }
            }
            _log.Info($"Loaded {FishSchools.Count} FishSchool...");

            _loaded = true;
        }

        public void FishFinderStart(Character character)
        {
            if (character.FishSchool.FishFinderTickTask != null)
            {
                StopFishFinderTickAsync(character).GetAwaiter().GetResult();
                return;
            }

            var buffId = 5736u; // Id=5736, "Search School of Fish"
            character.Buffs.AddBuff(new Buff(character, character, SkillCaster.GetByType(SkillCasterType.Unit), SkillManager.Instance.GetBuffTemplate(buffId), null, DateTime.UtcNow));

            character.SendPacket(new SCSchoolOfFishFinderToggledPacket(true, 800));
            character.FishSchool.FishFinderTickTask = new FishSchoolTickTask(character);
            TaskManager.Instance.Schedule(character.FishSchool.FishFinderTickTask, TimeSpan.FromMilliseconds(Delay));
        }

        internal void FishFinderTick(Character character)
        {
            const int MaxCount = 10;
            Doodad[] transfers;

            // не ограничивать дальность видимости для GM & Admins
            if (character.AccessLevel == 0)
            {
                var transfers2 = new List<Doodad>();
                foreach (var t in FishSchools)
                {
                    if (!(MathF.Abs(MathUtil.CalculateDistance(character, t)) < 800f)) { continue; }

                    transfers2.Add(t);
                }
                transfers = transfers2.ToArray();
            }
            else
            {
                transfers = FishSchools.ToArray();
            }

            if (transfers.Length > 0)
            {
                for (var i = 0; i < transfers.Length; i += MaxCount)
                {
                    var last = transfers.Length - i <= MaxCount;
                    var temp = new Doodad[last ? transfers.Length - i : MaxCount];
                    Array.Copy(transfers, i, temp, 0, temp.Length);
                    character.SendPacket(new SCSchoolOfFishDoodadsPacket(last, temp));
                }
            }
            else
            {
                character.SendPacket(new SCSchoolOfFishDoodadsPacket(true, Array.Empty<Doodad>()));
            }
            TaskManager.Instance.Schedule(character.FishSchool.FishFinderTickTask, TimeSpan.FromMilliseconds(Delay));
        }

        public async System.Threading.Tasks.Task StopFishFinderTickAsync(Character character)
        {
            if (character.FishSchool.FishFinderTickTask == null)
                return;

            await character.FishSchool.FishFinderTickTask.CancelAsync();
            character.FishSchool.FishFinderTickTask = null;
            character.SendPacket(new SCSchoolOfFishFinderToggledPacket(false, 0));
        }
    }
}
