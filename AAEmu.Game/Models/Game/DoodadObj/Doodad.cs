using System;
using System.Collections.Generic;
using System.Linq;

using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.CommonFarm.Static;
using AAEmu.Game.Models.Game.DoodadObj.Funcs;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Tasks.Doodads;

/*
 *-----------------------------------------------------------------------------------------------------------------
 *                        How doodad works
 *-----------------------------------------------------------------------------------------------------------------
 [Doodad] Chain: TemplateId 2336 (water the flowerbed)
 [Doodad] FuncGroupId : 4651 - start
 [Doodad] PhaseFunc: GroupId 4651, FuncId 250, FuncType DoodadFuncTod : NextPhase 5136, tod 2000
 [Doodad] Func: GroupId 4651, FuncId 543, FuncType DoodadFuncFakeUse, NextPhase 4652, Skill 0

 [Doodad] FuncGroupId : 4652 - normal
 [Doodad] PhaseFunc: GroupId 4652, FuncId 822, FuncType DoodadFuncTimer : delay=30000, nextPhase=4651
 [Doodad] Func: GroupId 4652, FuncId 0

 [Doodad] FuncGroupId : 5136 - normal
 [Doodad] PhaseFunc: GroupId 5136, FuncId 251, FuncType DoodadFuncTod : NextPhase 4651, tod 600
 [Doodad] Func: GroupId 5136, FuncId 775, FuncType DoodadFuncFakeUse, NextPhase 5137, Skill 0

 [Doodad] FuncGroupId : 5137 - normal
 [Doodad] PhaseFunc: GroupId 5137, FuncId 1001, FuncType DoodadFuncTimer : delay=30000, nextPhase=5136
 [Doodad] Func: GroupId 5137, FuncId 0
*-----------------------------------------------------------------------------------------------------------------
method public void Use(BaseUnit caster, uint skillId) runs in a loop:

2. start Func(functions) func.Use(caster, this, skillId, func.NextPhase)
   - one function is selected by the GetFunc(FuncGroupId, skillId) method
   such as DoodadFuncUse, DoodadFuncFakeUse, DoodadFuncLootItem, etc.
2.1. function launch
2.2. checking NextPhase and the presence of the function
2.2.1. no function - exit (we stop at this phase to wait for interaction)
2.2.2. NextPhase = 0 or -1 - exit,
2.2.3. goto 2.3., or transition to the next phase execution
2.3. transition to execution of NextPhase (repetition in an infinite loop, transition to step 1.)

1. start PhaseFunc (phase functions) - phaseFunc.Use(caster, this) - returns the result on interrupt and phase change
   with check for phase change depending on the time of day - DoodadFuncTod,
   check for quest - DoodadFuncRequireQuest, execution can be flagged.
   check - DoodadFuncRatioChange, phase change depending on the percentage hit.
   timer start - DoodadFuncTimer, with transition to execution
   doodad respawn - DoodadFuncFinal
   plant growth - DoodadFuncGrowth, etc.
   - GetPhaseFunc(FuncGroupId) - returns the list of phase functions
1.1. phase change
1.2. validity check - immediate loop termination
1.3. timers waiting for subsequent execution from the new phase
1.4. there may be no phase function (stop at this phase to wait for interaction), or there may be several (execute in a loop)

*-----------------------------------------------------------------------------------------------------------------
 */
namespace AAEmu.Game.Models.Game.DoodadObj;

public class Doodad : BaseUnit
{
    private float _scale;
    public byte Flag { get; set; }
    private int _data;
    private uint _funcGroupId;

    //public uint TemplateId { get; set; } // moved to BaseUnit
    public uint DbId { get; set; }
    public bool IsPersistent { get; set; }
    public DoodadTemplate Template { get; set; }
    public override float Scale => _scale;

    public DoodadFuncPermission FuncPermission
    {
        get
        {
            foreach (var currentFunc in CurrentFuncs)
            {
                return (DoodadFuncPermission)currentFunc.PermId;
            }

            return DoodadFuncPermission.Any;
        }
    }

    public uint FuncGroupId
    {
        get => _funcGroupId;
        set
        {
            if (value != _funcGroupId)
            {
                _funcGroupId = value;
                PhaseTime = DateTime.UtcNow; // Save PhaseTime at start of new phase (group)
                if (IsPersistent)
                {
                    Save();
                }

                CurrentFuncs = DoodadManager.Instance.GetFuncsForGroup(_funcGroupId);
                CurrentPhaseFuncs = DoodadManager.Instance.GetPhaseFunc(_funcGroupId);

                // Register new ToD triggers (if any)
                CurrentToDTriggers.Clear();
                foreach (var currentPhaseFunc in CurrentPhaseFuncs)
                {
                    if (currentPhaseFunc.FuncType != "DoodadFuncTod")
                        continue;
                    var todPhaseFunc = DoodadManager.Instance.GetPhaseFuncTemplate(currentPhaseFunc.FuncId, currentPhaseFunc.FuncType);
                    if (todPhaseFunc is not DoodadFuncTod doodadFuncTod)
                    {
                        Logger.Error($"DoodadFuncTod is not a DoodadFuncTod");
                        continue;
                    }

                    CurrentToDTriggers.TryAdd(doodadFuncTod.TodAsHours, doodadFuncTod.NextPhase);
                }
            }
        }
    }

    // public string FuncType { get; set; }
    public ulong ItemId { get; set; }
    public ulong UccId { get; set; }
    public uint ItemTemplateId { get; set; }
    public DateTime GrowthTime { get; set; }
    public DateTime PlantTime { get; set; }
    public DateTime PhaseTime { get; set; }
    public uint OwnerId { get; set; }
    public uint OwnerObjId { get; set; }
    public uint ParentObjId { get; set; }
    public DoodadOwnerType OwnerType { get; set; }
    public AttachPointKind AttachPoint { get; set; }
    public uint OwnerDbId { get; set; }
    public uint Type2 { get; set; } = 0;

    public int Data
    {
        get => _data;
        set
        {
            if (value != _data)
            {
                _data = value;
                if (IsPersistent)
                {
                    Save();
                }
            }
        }
    }
    public FarmType FarmType { get; set; }
    public uint QuestGlow { get; set; } //0 off // 1 on
    public int PuzzleGroup { get; set; } = -1; // -1 off
    public DoodadSpawner Spawner { get; set; }
    public DoodadFuncTask FuncTask { get; set; }
    public DateTime FreshnessTime { get; set; }

    public List<DoodadFunc> CurrentFuncs { get; set; }
    public List<DoodadPhaseFunc> CurrentPhaseFuncs { get; set; }
    /// <summary>
    /// ToD, next_phase
    /// </summary>
    public Dictionary<float, int> CurrentToDTriggers { get; set; }

    /// <summary>
    /// Time left to show on Doodads in milliseconds
    /// </summary>
    public uint TimeLeft
    {
        get
        {
            // This probably needs a better way to calculate, like a separate field to store the end-time
            foreach (var func in CurrentPhaseFuncs)
            {
                var template = DoodadManager.Instance.GetPhaseFuncTemplate(func.FuncId, func.FuncType);
                if (template is DoodadFuncFinal doodadFuncRecoverItemTemplate)
                {
                    if (doodadFuncRecoverItemTemplate.After > 0)
                    {
                        var left = (PhaseTime + TimeSpan.FromMilliseconds(doodadFuncRecoverItemTemplate.After) -
                                    DateTime.UtcNow).TotalMilliseconds;
                        return (uint)Math.Round(Math.Max(1, left));
                    }
                }
            }

            if (GrowthTime > DateTime.UtcNow)
            {
                return (uint)(GrowthTime - DateTime.UtcNow).TotalMilliseconds;
            }

            return 0;
        }
    }

    public bool ToNextPhase { get; set; }
    public int PhaseRatio { get; set; }
    public int CumulativePhaseRatio { get; set; }

    /// <summary>
    /// Used to indicate the starting phase of the doodad should be overriden when loading player doodads
    /// </summary>
    public int OverridePhase { get; set; }

    /// <summary>
    /// Used to indicate that the phase starting time should be overriden on timing related funcs
    /// </summary>
    public DateTime OverridePhaseTime { get; set; } = DateTime.MinValue;

    private bool _deleted = false;
    public VehicleSeat Seat { get; set; }
    private List<uint> ListGroupId { get; set; }
    public List<AreaTrigger> AttachAreaTriggers { get; set; } = new();
    
    public Doodad()
    {
        _scale = 1f;
        PlantTime = DateTime.MinValue;
        AttachPoint = AttachPointKind.System;
        Seat = new VehicleSeat(this);
        ListGroupId = new List<uint>();
        CurrentFuncs = new List<DoodadFunc>();
        CurrentPhaseFuncs = new List<DoodadPhaseFunc>();
        CurrentToDTriggers = new Dictionary<float, int>();
    }

    public void SetScale(float scale)
    {
        _scale = scale;
    }

    /* Unused
    private bool CheckPhase(uint anotherPhase)
    {
        return ListGroupId.Any(phase => phase == anotherPhase);
    }

    private bool CheckFunc(uint anotherPhase)
    {
        return ListFuncGroupId.Any(phase => phase == anotherPhase);
    }*/

    /*
     * 1. Создание (посадка) Doodad запускает на стартовой фазе PhaseFunc;
     * 2. Ждем взаимодействия с Doodad;
     * 3. Непосредствено взаимодействие начинается с выполнения Func с учётом SkillId;
     * 4. Далее на следующей фазе начинаем выполнение с фазовых функций, а затем сами функции, если перед этим прошли проверки в фазовых функциях;
     *
     * 1. Creation (landing) Doodad launches on the PhaseFunc start phase;
     * 2. Looking forward to interacting with Doodad;
     * 3. Direct interaction starts with execution of a Func, taking into account the SkillId;
     * 4. Then in the next phase we start execution with the phase functions and then the functions themselves, if the checks in the phase functions have been passed before;
     */
    public void SetData(int data)
    {
        _data = data;
    }

    public void Use(BaseUnit caster, uint skillId = 0, int funcGroupId = 0)
    {
        if (caster == null)
        {
            return;
        }

        if (funcGroupId > 0)
        {
            FuncGroupId = (uint)funcGroupId;
        }

        while (true)
        {
            var player = caster as Character;
            if (player != null)
            {
                Logger.Warn($"Use: TemplateId {TemplateId}, Using phase {FuncGroupId} with SkillId {skillId}");
            }
            else
            {
                Logger.Trace($"Use: TemplateId {TemplateId}, Using phase {FuncGroupId} with SkillId {skillId}");
            }

            ToNextPhase = false; // по умолчанию не выполняем следующую фазу
            ListGroupId.Clear();

            //  first we find the functions, then we execute
            var funcWithSkill = DoodadManager.Instance.GetFunc(FuncGroupId, skillId); // if skillId > 0
            var allFuncsForGroup = DoodadManager.Instance.GetFuncsForGroup(FuncGroupId);

            if (allFuncsForGroup.Count <= 0)
            {
                // Phase has no funcs
                return;
            }

            if (skillId == 0)
            {
                foreach (var funcWithoutSkill in allFuncsForGroup.Where(f =>
                             f.FuncType is "DoodadFuncLootItem" or "DoodadFuncLootPack" or "DoodadFuncCutdowning"))
                {
                    if (DoFunc(caster, 0, funcWithoutSkill))
                    {
                        ListGroupId.Clear();
                        return;
                    }
                }
            }
            else
            {
                if (DoFunc(caster, skillId, funcWithSkill))
                {
                    // FuncGroupId будет равен либо текущая фаза, либо func.NextPhase, либо OverridePhase
                    DoChangePhase(caster, (int)FuncGroupId);
                    return;
                }
            }

            // then execute the phase functions (the FuncGroupId may change to a different one than it was before)
            var stop = DoChangePhase(caster, (int)FuncGroupId);
            if (stop || ToNextPhase == false)
            {
                // did not pass the quest conditions check or there is no phase function
                if (caster is Character)
                {
                    Logger.Debug($"Use: Did not pass the conditions check! TemplateId {TemplateId}, Using phase {FuncGroupId} with SkillId {skillId}");
                    Logger.Debug($"Use: Looking forward to interacting with doodad TemplateId {TemplateId}, Using phase {FuncGroupId}");
                }
                else
                {
                    Logger.Trace($"Use: Did not pass the conditions check! TemplateId {TemplateId}, Using phase {FuncGroupId} with SkillId {skillId}");
                }

                return;
            }

            skillId = 0;
        }
    }

    /// <summary>
    /// Launch of functions
    /// </summary>
    /// <param name="caster"></param>
    /// <param name="skillId"></param>
    /// <param name="func"></param>
    /// <returns>If TRUE, then we stop further execution of functions and wait for interaction</returns>
    public bool DoFunc(BaseUnit caster, uint skillId, DoodadFunc func)
    {
        // if there is no function, complete the cycle
        if (func == null)
        {
            if (caster is Character)
            {
                Logger.Debug($"DoFunc: Finished execution with func = null: TemplateId {TemplateId}, Using phase {FuncGroupId} with SkillId {skillId}");
            }
            else
            {
                Logger.Trace($"DoFunc: Finished execution with func = null: TemplateId {TemplateId}, Using phase {FuncGroupId} with SkillId {skillId}");
            }

            return true;
        }

        // then perform the function
        func.Use(caster, this, skillId, func.NextPhase);
        if (func.SoundId > 0)
        {
            BroadcastPacket(new SCDoodadSoundPacket(this, func.SoundId), true);
        }

        if (ToNextPhase)
        {
            if (func.NextPhase == -1)
            {
                // не надо переходить на другую фазу, остаемся на текущей фазе
                // проверка нужна для Windstone id=1473
                if (!HasOnlyGroupKindStart())
                {
                    if (FuncTask != null)
                    {
                        FuncTask.Cancel();
                        FuncTask = null;
                        Logger.Debug($"DoFunc::DoodadFuncTimer: The current timer has been canceled. TemplateId {TemplateId}, ObjId {ObjId}, nextPhase {func.NextPhase}");
                    }

                    // Delete doodad
                    if (Spawner is not null)
                    {
                        Spawner.Despawn(this);
                    }
                    else
                    {
                        Delete();
                    }
                }

                return true;
            }

            // требуется переход на другую фазу
            if (OverridePhase > 0)
            {
                // встречается в DoodadFuncConditionalUse
                FuncGroupId = (uint)OverridePhase;
                OverridePhase = 0;
            }
            else
            {
                FuncGroupId = (uint)func.NextPhase;
            }
        }
        else
        {
            if (caster is Character)
            {
                Logger.Debug($"DoFunc Finished execution withOut ToNextPhase = {ToNextPhase}: TemplateId {TemplateId}, Using phase {FuncGroupId} with SkillId {skillId}");
            }
            else
            {
                Logger.Trace($"DoFunc Finished execution withOut ToNextPhase = {ToNextPhase}: TemplateId {TemplateId}, Using phase {FuncGroupId} with SkillId {skillId}");
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// start-up for execution of phase functions
    /// </summary>
    /// <param name="caster"></param>
    /// <param name="nextPhase"></param>
    /// <returns>if true, it did not pass the check for the phase (it must be aborted)</returns>
    private bool DoPhaseFuncs(BaseUnit caster, ref int nextPhase)
    {
        if (nextPhase <= 0) { return true; }

        // Changing the phase.
        FuncGroupId = (uint)nextPhase;

        if (!ListGroupId.Contains((uint)nextPhase))
        {
            ListGroupId.Add((uint)nextPhase); // to check CheckPhase()
        }
        else
        {
            var funcs = DoodadManager.Instance.GetFuncsForGroup(FuncGroupId);
            if (funcs.Count > 0)
            {
                // например, если это ID=2231, Target, то надо прервать рекурсию
                if (caster is Character)
                {
                    Logger.Debug($"DoPhase: Finished execution with recurse: TemplateId {TemplateId}, Using phase {FuncGroupId}");
                }
                else
                {
                    Logger.Trace($"DoPhase: Finished execution with recurse: TemplateId {TemplateId}, Using phase {FuncGroupId}");
                }

                ListGroupId.Clear();
                return true;
            }

            // например, если это ID=898, Prison Gate, то не надо прервать рекурсию
            ListGroupId.Clear();
        }

        if (FuncTask != null)
        {
            FuncTask.Cancel();
            FuncTask = null;
            if (caster is Character)
            {
                Logger.Debug("DoPhaseFuncs:DoodadFuncTimer: The current timer has been canceled.");
            }
            else
            {
                Logger.Trace("DoPhaseFuncs:DoodadFuncTimer: The current timer has been canceled.");
            }
        }

        if (caster is Character)
        {
            Logger.Debug($"DoPhaseFuncs: TemplateId {TemplateId}, ObjId {ObjId}, nextPhase {nextPhase}");
        }
        else
        {
            Logger.Trace($"DoPhaseFuncs: TemplateId {TemplateId}, ObjId {ObjId}, nextPhase {nextPhase}");
        }

        var phaseFuncs = DoodadManager.Instance.GetPhaseFunc(FuncGroupId);
        if (phaseFuncs.Count == 0)
        {
            return false; // no phase functions for FuncGroupId
        }

        //CumulativePhaseRatio = 0; // не требуется
        var stop = false;

        // Perform the phase functions one after the other
        foreach (var phaseFunc in phaseFuncs)
        {
            if (phaseFunc == null) { continue; }

            PhaseRatio = Rand.Next(0, 10000); // проверяем шанс для каждой фазовой функции

            stop = phaseFunc.Use(caster, this);
            if (stop)
            {
                break; // interrupt execution of phase functions and switch to OverridePhase
            }
        }

        if (OverridePhase != 0 && stop && FuncGroupId != OverridePhase)
        {
            nextPhase = OverridePhase;
            OverridePhase = 0;
            var res = DoPhaseFuncs(caster, ref nextPhase);
            return res;
        }

        if (!_deleted)
        {
            Save(); // let's save the doodad in the database
        }

        return stop; // if true, it did not pass the check for the quest (it must be aborted)
    }

    /// <summary>
    /// Start phase functions and phase change
    /// </summary>
    /// <param name="caster"></param>
    /// <param name="nextPhase"></param>
    /// <returns>if TRUE, it did not pass the check for the quest (it must be aborted)</returns>
    public bool DoChangePhase(BaseUnit caster, int nextPhase)
    {
        // здесь не надо удалять doodad
        //if (nextPhase == -1)
        //{
        //    Delete();
        //    return false;
        //}

        if (nextPhase <= 0) { return false; }

        // Skip if moving to self.
        //if (nextPhase == FuncGroupId)
        //    return true;
            

        if (caster is Character)
        {
            Logger.Debug($"DoChangePhase: TemplateId {TemplateId}, ObjId {ObjId}, nextPhase {nextPhase}");
        }
        else
        {
            Logger.Trace($"DoChangePhase: TemplateId {TemplateId}, ObjId {ObjId}, nextPhase {nextPhase}");
        }

        var stop = DoPhaseFuncs(caster, ref nextPhase);

        // the phase change packet call must be after the phase functions to have the correct FuncGroupId in the packet
        BroadcastPacket(new SCDoodadPhaseChangedPacket(this), true); // change the phase to display doodad

        return stop; // if true, it did not pass the check for the quest (it must be aborted)
    }
    public bool DoChangeOtherDoodadPhase(BaseUnit caster, Doodad doodad, int nextPhase)
    {
        //var prevFuncGroupId = FuncGroupId;
        FuncGroupId = (uint)nextPhase;

        if (nextPhase <= 0) { return false; }

        Logger.Debug($"DoChangePhase: TemplateId {TemplateId}, ObjId {ObjId}, nextPhase {nextPhase}");
        //var stop = DoPhaseFuncs(caster, ref nextPhase);
        // the phase change packet call must be after the phase functions to have the correct FuncGroupId in the packet
        BroadcastPacket(new SCDoodadPhaseChangedPacket(doodad), true); // change the phase to display doodad

        return false; // if true, it did not pass the check for the quest (it must be aborted)
    }

    private bool HasOnlyGroupKindStart()
    {
        return Template.FuncGroups.All(funcGroup =>
            funcGroup.GroupKindId is not (DoodadFuncGroups.DoodadFuncGroupKind.Normal
                or DoodadFuncGroups.DoodadFuncGroupKind.End));
    }

    public bool IsGroupKindStart(uint funcGroupId)
    {
        return Template.FuncGroups.Where(funcGroup =>
                funcGroup.GroupKindId == DoodadFuncGroups.DoodadFuncGroupKind.Start)
            .Any(funcGroup => funcGroupId == funcGroup.Id);
    }

    public uint GetFuncGroupId()
    {
        return (from funcGroup in Template.FuncGroups
                where funcGroup.GroupKindId == DoodadFuncGroups.DoodadFuncGroupKind.Start
                select funcGroup.Id).FirstOrDefault();
    }

    public void OnSkillHit(BaseUnit caster, uint skillId)
    {
        var funcs = DoodadManager.Instance.GetFuncsForGroup(FuncGroupId);
        if (funcs == null) { return; }

        foreach (var func in funcs.Where(func => func.FuncType == "DoodadFuncSkillHit"))
        {
            Use(caster, skillId);
        }
    }

    /// <summary>
    /// initialization of the current doodad phase
    /// </summary>
    public void InitDoodad()
    {
        // Apply Climate settings
        var growTime = Template.TotalDoodadGrowthTime / AppConfiguration.Instance.World.GrowthRate;
        if (Template.TotalDoodadGrowthTime > 0 && ZoneManager.DoodadHasMatchingClimate(this))
        {
            growTime = (int)Math.Round(growTime * 0.73f);
        }

        GrowthTime = PlantTime.AddMilliseconds(growTime);

        // Actually do the phase change
        var unit = WorldManager.Instance.GetUnit(OwnerObjId);
        DoChangePhase(unit, (int)FuncGroupId);
    }

    public override void BroadcastPacket(GamePacket packet, bool self)
    {
        foreach (var character in WorldManager.GetAround<Character>(this))
        {
            character.SendPacket(packet);
        }
    }

    public override void AddVisibleObject(Character character)
    {
        character.SendPacket(new SCDoodadCreatedPacket(this));
        base.AddVisibleObject(character);
    }

    public override void RemoveVisibleObject(Character character)
    {
        base.RemoveVisibleObject(character);
        character.SendPacket(new SCDoodadRemovedPacket(ObjId));
    }
    
    /// <summary>
    /// GetItemTemplateIdByDoodadTemplateId - если doodad это backpack, то вернуть TemplateId предмета, который заменит этот doodad у персонажа.
    /// </summary>
    /// <param name="doodadTemplateId"></param>
    /// <returns></returns>
    private uint GetItemTemplateIdByDoodadTemplateId(uint doodadTemplateId)
    {

       return 0;
    }

    public PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(ObjId); //The object # in the list
        // TemplateId - The template id needed for that object, the client then uses the template configurations, not the server
        // CurrentPhaseId / FuncGroupId - doodad_func_group_id
        // Здесь должны вставить: doodadTemplateId -> itemTemplateId
        // можно найти: backpack_doodad_id->PutdownBackPackEffect->effect->skill_effect->skill->templateId
        // ItemTemplateId от BackPack, который лежит на земле: Item->skill->PutdownBackPackEffect->backpack_doodad_id
        // Doodad ID=7794 Solzreed Dried Food [Interaction - Backpack] -> ID=31857 Solzreed Dried Food -> Skill ID=24870 Drop Specialty
        // PutdownBackPackEffect : backpack_doodad_id->effect_id, 
        // QuestGlow - When this is higher than 0 it shows a blue orb over the doodad
        var backpack = GetItemTemplateIdByDoodadTemplateId(TemplateId);

        stream.WritePisc(TemplateId, FuncGroupId, backpack, QuestGlow);

        stream.Write(Flag);
        stream.WriteBc(OwnerObjId); //The creator of the object
        stream.WriteBc(ParentObjId); //Things like boats or cars,
        stream.Write((byte)AttachPoint); // attachPoint, relative to the parentObj (Door or window on a house, seats on carriage, etc.)
        if (AttachPoint > 0 || ParentObjId > 0)
        {
            stream.WritePosition(Transform.Local.Position.X, Transform.Local.Position.Y, Transform.Local.Position.Z);
            var (roll, pitch, yaw) = Transform.Local.ToRollPitchYawShorts();
            stream.Write(roll);
            stream.Write(pitch);
            stream.Write(yaw);
        }
        else
        {
            stream.WritePosition(Transform.World.Position.X, Transform.World.Position.Y, Transform.World.Position.Z);
            var (roll, pitch, yaw) = Transform.World.ToRollPitchYawShorts();
            stream.Write(roll);
            stream.Write(pitch);
            stream.Write(yaw);
        }

        stream.Write(Scale); //The size of the object
        stream.Write(OwnerId);         // characterId
        stream.Write(UccId);           // type(id)
        stream.Write(ItemTemplateId);  // type(id)
        stream.Write(TimeLeft);        // growing
        stream.Write(PlantTime);       // plantTime
        stream.Write(0);               // family
        stream.Write(PuzzleGroup);     // puzzleGroup
        stream.Write((byte)OwnerType); // ownerType
        stream.Write(OwnerDbId); // dbHouseId
        stream.Write(Data); // data - attachPointId для хранения в базе данных
        if (backpack != 0)
        {
            stream.Write(FreshnessTime); // freshnessTime
            stream.Write((uint)0);       // type crafter?
            stream.Write((short)0);      // type
        }
        stream.Write(0u);              // type

        return stream;
    }

    public override void Delete()
    {
        base.Delete();
        _deleted = true;
        foreach (var areaTrigger in AttachAreaTriggers)
        {
            AreaTriggerManager.Instance.RemoveAreaTrigger(areaTrigger);
        }

        AttachAreaTriggers.Clear();

        // Delete associated item if expired
        if (ItemId > 0)
        {
            var item = ItemManager.Instance.GetItemByItemId(ItemId);
            if (item != null && item._holdingContainer != null &&
                (item._holdingContainer.ContainerType == SlotType.Invalid ||
                 item._holdingContainer.ContainerType == SlotType.Money))
            {
                item._holdingContainer.RemoveItem(ItemTaskType.Invalid, item, true);
            }
        }

        if (IsPersistent)
        {
            using (var connection = MySQL.CreateConnection())
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "DELETE FROM doodads WHERE id = @id";
                    command.Parameters.AddWithValue("@id", DbId);
                    command.Prepare();
                    command.ExecuteNonQuery();
                }
            }

            SpawnManager.Instance.RemovePlayerDoodad(this);
            IsPersistent = false;
        }
    }

    public void Save()
    {
        if (!IsPersistent)
        {
            return;
        }

        DbId = DbId > 0 ? DbId : DoodadIdManager.Instance.GetNextId();
        using (var connection = MySQL.CreateConnection())
        {
            using (var command = connection.CreateCommand())
            {
                // Lookup Parent
                var parentDoodadId = 0u;
                if (Transform?.Parent?.GameObject is Doodad pDoodad && pDoodad.DbId > 0)
                {
                    parentDoodadId = pDoodad.DbId;
                }

                command.CommandText =
                    "REPLACE INTO doodads (`id`, `owner_id`, `owner_type`, `attach_point`, `template_id`, `current_phase_id`, `plant_time`, `growth_time`, `phase_time`, `x`, `y`, `z`, `roll`, `pitch`, `yaw`, `scale`, `item_id`, `house_id`, `parent_doodad`, `item_template_id`, `item_container_id`, `data`, `farm_type`) " +
                    "VALUES(@id, @owner_id, @owner_type, @attach_point, @template_id, @current_phase_id, @plant_time, @growth_time, @phase_time, @x, @y, @z, @roll, @pitch, @yaw, @scale, @item_id, @house_id, @parent_doodad, @item_template_id, @item_container_id, @data, @farm_type)";
                command.Parameters.AddWithValue("@id", DbId);
                command.Parameters.AddWithValue("@owner_id", OwnerId);
                command.Parameters.AddWithValue("@owner_type", OwnerType);
                command.Parameters.AddWithValue("@attach_point", AttachPoint);
                command.Parameters.AddWithValue("@template_id", TemplateId);
                command.Parameters.AddWithValue("@current_phase_id", FuncGroupId);
                command.Parameters.AddWithValue("@plant_time", PlantTime);
                command.Parameters.AddWithValue("@growth_time", GrowthTime);
                command.Parameters.AddWithValue("@phase_time", PhaseTime);
                // We save it's world position, and upon loading, we re-parent things depending on the data
                command.Parameters.AddWithValue("@x", Transform?.Local.Position.X ?? 0f);
                command.Parameters.AddWithValue("@y", Transform?.Local.Position.Y ?? 0f);
                command.Parameters.AddWithValue("@z", Transform?.Local.Position.Z ?? 0f);
                command.Parameters.AddWithValue("@roll", Transform?.Local.Rotation.X ?? 0f);
                command.Parameters.AddWithValue("@pitch", Transform?.Local.Rotation.Y ?? 0f);
                command.Parameters.AddWithValue("@yaw", Transform?.Local.Rotation.Z ?? 0f);
                command.Parameters.AddWithValue("@scale", Scale);
                command.Parameters.AddWithValue("@item_id", ItemId);
                command.Parameters.AddWithValue("@house_id", OwnerDbId);
                command.Parameters.AddWithValue("@parent_doodad", parentDoodadId);
                command.Parameters.AddWithValue("@item_template_id", ItemTemplateId);
                command.Parameters.AddWithValue("@item_container_id", GetItemContainerId());
                command.Parameters.AddWithValue("@data", Data);
                command.Parameters.AddWithValue("@farm_type", FarmType);
                command.Prepare();
                command.ExecuteNonQuery();
            }
        }
    }

    public void DoDespawn(Doodad doodad)
    {
        Spawner.DoDespawn(doodad);
    }

    public override bool AllowRemoval()
    {
        // Only allow removal if there is no other persistent Doodads stacked on top of this
        foreach (var child in Transform.Children)
        {
            if (child.GameObject is Doodad { IsPersistent: true })
            {
                return false;
            }
        }

        return base.AllowRemoval();
    }

    /// <summary>
    /// Return the associated ItemContainerId for this Doodad
    /// </summary>
    /// <returns></returns>
    public virtual ulong GetItemContainerId()
    {
        return 0;
    }

    public PacketStream WriteFishFinderUnit(PacketStream stream)
    {
        stream.WriteBc(ObjId);
        stream.Write(Template.Id);
        stream.WritePosition(Transform.World.Position);

        return stream;
    }
}
