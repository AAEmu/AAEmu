using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncTod : DoodadPhaseFuncTemplate
{
    /// <summary>
    /// Time of Day as HH:MM represented by a uint
    /// </summary>
    public uint Tod { get; set; }
    public int NextPhase { get; set; }
    /// <summary>
    /// Helper property for ToD
    /// </summary>
    public float TodAsHours { get; set; }

    public override bool Use(BaseUnit caster, Doodad owner)
    {
        // I think this is used to reschedule anything that needs triggered at a specific gametime
        // По моему, здесь должна быть проверка на время дня.
        // Например: уличные светильники должны гореть ночью, а не днем.
        // если текущее время более 4:00 (tod=400), то переключим на 4024 - выкл, а после 20:00 (tod=2000) на 4023 - вкл.
        /*
           [Doodad] Chain: TemplateId 2322 - светильник (4623|4718-вкл, 4624|4717-выкл)
           [Doodad] FuncGroupId : 4623
           [Doodad] PhaseFunc: GroupId 4623, FuncId 132, FuncType DoodadFuncTod : tod=400, nextPhase=4624
           [Doodad] Func: GroupId 4623, FuncId 533, FuncType DoodadFuncFakeUse, NextPhase 4717, Skill 0 : skillId=0, fakeSkillId=13167

           [Doodad] FuncGroupId : 4624
           [Doodad] PhaseFunc: GroupId 4624, FuncId 133, FuncType DoodadFuncTod : tod=2000, nextPhase=4623
           [Doodad] PhaseFunc: GroupId 4624, FuncId 301, FuncType DoodadFuncTod : tod=930, nextPhase=-1
           [Doodad] Func: GroupId 4624, FuncId 534, FuncType DoodadFuncFakeUse, NextPhase 4718, Skill 0 : skillId=0, fakeSkillId=13166

           [Doodad] FuncGroupId : 4717
           [Doodad] PhaseFunc: GroupId 4717, FuncId 833, FuncType DoodadFuncTimer : Delay 60000, NextPhase 4623
           [Doodad] PhaseFunc: GroupId 4717, FuncId 138, FuncType DoodadFuncTod : tod=400, nextPhase=4624
           [Doodad] Func: GroupId 4717, FuncId 596, FuncType DoodadFuncFakeUse, NextPhase 4623, Skill 0 : skillId=0, fakeSkillId=13166

           [Doodad] FuncGroupId : 4718
           [Doodad] PhaseFunc: GroupId 4718, FuncId 834, FuncType DoodadFuncTimer : Delay 60000, NextPhase 4624
           [Doodad] PhaseFunc: GroupId 4718, FuncId 139, FuncType DoodadFuncTod : tod=2000, nextPhase=4623
           [Doodad] Func: GroupId 4718, FuncId 597, FuncType DoodadFuncFakeUse, NextPhase 4624, Skill 0 : skillId=0, fakeSkillId=13167
        */
        //var curTime = TimeManager.Instance.GetTime();
        //if (owner.FuncTask != null)
        //{
        //    _ = owner.FuncTask.Cancel();
        //    _ = owner.FuncTask = null;
        //    Logger.Trace("DoodadFuncTimerTask: The current timer has been canceled by the TOD {0}", curTime);
        //}

        return false; // Temporary ignore for now

        var curTime = TimeManager.Instance.GetTime;
        if (curTime >= TodAsHours)
        {
            if (caster is Character)
                Logger.Debug($"DoodadFuncTod: curTime {curTime}, Tod {Tod}, OverridePhase {NextPhase}");
            else
                Logger.Trace($"DoodadFuncTod: curTime {curTime}, Tod {Tod}, OverridePhase {NextPhase}");
            owner.OverridePhase = NextPhase;
            return true; // it is necessary to interrupt the phase functions and switch to NextPhase
        }

        return false;
    }
}
