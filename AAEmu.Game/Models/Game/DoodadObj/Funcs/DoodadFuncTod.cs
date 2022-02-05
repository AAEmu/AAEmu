using System;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Tasks.Doodads;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncTod : DoodadFuncTemplate
    {
        public int Tod { get; set; }
        public uint NextPhase { get; set; }

        public override void Use(Unit caster, Doodad owner, uint skillId, int nextPhase = 0)
        {
            _log.Debug("DoodadFuncTod: skillId {0}, nextPhase {1},  Tod {2}, NextPhase {3}",
                skillId, nextPhase, Tod, NextPhase);

            //if (caster is Character)
            {
                //I think this is used to reschedule anything that needs triggered at a specific gametime
                // По моему, здесь должна быть проверка на время дня.
                // Например: уличные светильники должны гореть ночью, а не днем.
                // к в 4:00 (tod=400) переключим на 4024 - выкл, а в 20:00 (tod=2000) на 4023 - вкл.
                /*
                [Doodad] Chain: TemplateId 2322
                [Doodad] FuncGroupId : 4623 Start
                [Doodad] Func: GroupId 4623, FuncId 533, FuncType DoodadFuncFakeUse, NextPhase 4717, Skill 0
                [Doodad] PhaseFunc: GroupId 4717, FuncId 833, FuncType DoodadFuncTimer, NextPhase 4623, Delay 60000
                [Doodad] PhaseFunc: GroupId 4717, FuncId 138, FuncType DoodadFuncTod, NextPhase 4624, tod 400
                [Doodad] FuncGroupId : 4624
                [Doodad] Func: GroupId 4624, FuncId 534, FuncType DoodadFuncFakeUse, NextPhase 4718, Skill 0
                [Doodad] PhaseFunc: GroupId 4718, FuncId 834, FuncType DoodadFuncTimer, NextPhase 4024, Delay 60000 
                [Doodad] PhaseFunc: GroupId 4718, FuncId 139, FuncType DoodadFuncTod, NextPhase 4623, tod 2000
                [Doodad] FuncGroupId : 4717
                [Doodad] Func: GroupId 4717, FuncId 596, FuncType DoodadFuncFakeUse, NextPhase 4623, Skill 0
                [Doodad] PhaseFunc: GroupId 4623, FuncId 132, FuncType DoodadFuncTod, NextPhase 4624, tod 400
                [Doodad] FuncGroupId : 4718
                [Doodad] Func: GroupId 4718, FuncId 597, FuncType DoodadFuncFakeUse, NextPhase 4624, Skill 0
                [Doodad] PhaseFunc: GroupId 4624, FuncId 133, FuncType DoodadFuncTod, NextPhase 4623, tod 2000
                [Doodad] PhaseFunc: GroupId 4624, FuncId 301, FuncType DoodadFuncTod, NextPhase -1, tod 930
                */
                //var curTime = TimeManager.Instance.GetTime();
                //if (owner.FuncTask != null)
                //{
                //    _ = owner.FuncTask.Cancel();
                //    _ = owner.FuncTask = null;
                //    _log.Debug("DoodadFuncTimerTask: The current timer has been canceled by the TOD {0}", curTime);
                //}
                //if (NextPhase <= 0)
                //{
                //    return;
                //}
                //if (curTime >= 4 && curTime < 20)
                //{
                //    owner.FuncGroupId = 4617;
                //    owner.BroadcastPacket(new SCDoodadPhaseChangedPacket(owner), true);
                //    owner.FuncGroupId = 4624;
                //}
                //else if (curTime < 4 && curTime >= 20)
                //{
                //    owner.FuncGroupId = 4618;
                //    owner.BroadcastPacket(new SCDoodadPhaseChangedPacket(owner), true);
                //    owner.FuncGroupId = 4623;
                //}
            }

        }
    }
}
