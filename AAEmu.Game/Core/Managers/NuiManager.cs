using System.Collections.Generic;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Core.Managers
{
    class NuiManager : Singleton<NuiManager>
    {
        private Dictionary<uint, AreaTrigger> _nuiTriggers;

        public void Add(Npc nui)
        {
            var areaShape = new AreaShape();
            areaShape.Type = AreaShapeType.Sphere;
            areaShape.Value1 = 15f; // 15m?

            var areaTrigger = new AreaTrigger()
            {
                Shape = areaShape,
                Owner = nui,
                Caster = nui,
                InsideBuffTemplate = SkillManager.Instance.GetBuffTemplate(2149), // No Fight
                TargetRelation = 0,
                TickRate = 0
            };


            _nuiTriggers.Add(nui.ObjId, areaTrigger);

            AreaTriggerManager.Instance.AddAreaTrigger(areaTrigger);
        }

        public void Remove(Npc npc)
        {
            _nuiTriggers.Remove(npc.ObjId);
        }
    }
}
