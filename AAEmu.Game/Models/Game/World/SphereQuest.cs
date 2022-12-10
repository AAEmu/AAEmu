using System;
using System.Collections.Generic;
using System.Numerics;

using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Utils;

using NLog;

namespace AAEmu.Game.Models.Game.World
{
    public enum AreaSphereTriggerCondition
    {
        Invalid = 0,
        TriggerOnceAtAll = 1,
        TriggerOnceInRuntime = 2,
        TriggerEveryNTimeAfter = 3
    }

    public class SphereQuest
    {
        public uint ZoneID { get; set; }
        public string WorldID { get; set; }
        public uint QuestID { get; set; }
        public uint ComponentID { get; set; }
        public float Radius { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
    }

    public class SphereQuestTrigger
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        public SphereQuest Sphere { get; set; }
        public ICharacter Owner { get; set; }
        public Quest Quest { get; set; }

        private List<SphereQuestTrigger> Triggers { get; set; }
        public int TickRate { get; set; }
        private DateTime _lastTick = DateTime.MinValue;

        public SphereQuestTrigger()
        {
            Triggers = new List<SphereQuestTrigger>();
        }

        public void UpdateUnits()
        {
            Triggers = SphereQuestManager.Instance.GetSphereQuestTriggers();
        }

        public void ApplyEffects()
        {
            foreach (var trigger in Triggers)
            {
                if (trigger.Quest.ComponentId == trigger.Sphere.ComponentID)
                {
                    var xyzSphereQuest = new Vector3(trigger.Sphere.X, trigger.Sphere.Y, trigger.Sphere.Z);
                    // TODO срабатывает триггер в радиусе от центра сферы
                    if (MathUtil.CalculateDistance(trigger.Owner.Transform.World.Position, xyzSphereQuest, true) < trigger.Sphere.Radius)
                    {
                        trigger.Owner.Quests.OnEnterSphere(trigger.Sphere);
                        SphereQuestManager.Instance.RemoveSphereQuestTrigger(trigger);
                    }
                }
            }
        }

        public void Tick(TimeSpan delta)
        {
            UpdateUnits();
            if (TickRate > 0)
            {
                if ((DateTime.UtcNow - _lastTick).TotalMilliseconds > TickRate)
                {
                    ApplyEffects();
                    _lastTick = DateTime.UtcNow;
                }
            }
        }
    }
}
