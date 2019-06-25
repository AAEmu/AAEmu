using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Tasks.Doodads;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncTimer : DoodadFuncTemplate
    {
        public int Delay { get; set; }
        public uint NextPhase { get; set; }
        public bool KeepRequester { get; set; }
        public bool ShowTip { get; set; }
        public bool ShowEndTime { get; set; }
        public string Tip { get; set; }

        public override void Use(Unit caster, Doodad owner, uint skillId)
        {
            _log.Debug("DoodadFuncTimer : NextPhase {0}, SkillId {1}", NextPhase, skillId);

            //This is a temporary fix. We need to find how to properly call the next function.
            // var nextFunc = DoodadManager.Instance.GetFunc(owner.FuncGroupId, skillId);
            // if (nextFunc != null) nextFunc.Use(caster, owner, skillId);
            if (Delay > 0)
            {
                owner.GrowthTime = DateTime.Now.AddMilliseconds(Delay); // TODO ... need here?
                // выполняем действие
                owner.BroadcastPacket(new SCDoodadPhaseChangedPacket(owner), true); // TODO door, windows with delay of this timer...

                // планируем выполнение действия NextPhase
                owner.FuncTask = new DoodadFuncTimerTask(caster, owner, skillId, NextPhase);
                TaskManager.Instance.Schedule(owner.FuncTask, TimeSpan.FromMilliseconds(Delay));
            }
        }
    }
}
