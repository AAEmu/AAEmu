using System;
using System.Collections.Generic;
using System.Linq;

using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Tasks.Doodads;

/*
 *-----------------------------------------------------------------------------------------------------------------
 *                        Как работает doodad
 *-----------------------------------------------------------------------------------------------------------------
 [Doodad] Chain: TemplateId 2336 (поливаем клумбу)
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
метод public void Use(BaseUnit caster, uint skillId) запускает в цикле:

2. запуск Func (функций) func.Use(caster, this, skillId, func.NextPhase)
   - одна функция выбирается методом GetFunc(FuncGroupId, skillId)
   таких как DoodadFuncUse, DoodadFuncFakeUse, DoodadFuncLootItem и т.д.
2.1. запуск функции
2.2. проверка NextPhase и наличие функции
2.2.1. нет функции - выход  (останавливаемся на этой фазе для ожидания взаимодействия)
2.2.2. NextPhase = 0 или -1 - выход,
2.2.3. goto 2.3., либо переход на выполнение следующей фазы
2.3. прерход на выполнение NextPhase (повтор в бесконечном цикле, переход к п. 1.)

1. запуск PhaseFunc (фазовых функций) - phaseFunc.Use(caster, this) - возвращает результат на прерывание и смену фазы
   с проверкой перехода на новую фазу в зависимости от времени суток - DoodadFuncTod,
   проверкой на квест - DoodadFuncRequireQuest, возможна отметна выполнения
   проверкой - DoodadFuncRatioChange, смена фазы в зависимости от поподания в проценты
   запуск таймера - DoodadFuncTimer, с переходом на выполнения
   респавна doodad - DoodadFuncFinal
   рост растений - DoodadFuncGrowth и т.д.
   - GetPhaseFunc(FuncGroupId) - возвращает список фазовых функций
1.1. смена фазы
1.2. проверка валидности - немедленное завершение цикла
1.3. таймеры ожидания последующего выполнения с новой фазы
1.4. фазовой функции может не быть (останавливаемся на этой фазе для ожидания взаимодействия), или их несколько (выполняем в цикле)

*-----------------------------------------------------------------------------------------------------------------*/
namespace AAEmu.Game.Models.Game.DoodadObj
{
    public class Doodad : BaseUnit
    {
        private float _scale;

        private int _data;

        //public uint TemplateId { get; set; } // moved to BaseUnit
        public uint DbId { get; set; }
        public bool IsPersistent { get; set; } = false;
        public DoodadTemplate Template { get; set; }
        public override float Scale => _scale;
        public uint FuncGroupId { get; set; }
        public string FuncType { get; set; }
        public ulong ItemId { get; set; }
        public ulong UccId { get; set; }
        public uint ItemTemplateId { get; set; }
        public DateTime GrowthTime { get; set; }
        public DateTime PlantTime { get; set; }
        public uint OwnerId { get; set; }
        public uint OwnerObjId { get; set; }
        public uint ParentObjId { get; set; }
        public DoodadOwnerType OwnerType { get; set; }
        public AttachPointKind AttachPoint { get; set; }
        public uint DbHouseId { get; set; }

        public int Data
        {
            get => _data;
            set
            {
                if (value != _data)
                {
                    _data = value;
                    if (DbId > 0)
                        Save();
                }
            }
        }

        public uint QuestGlow { get; set; } //0 off // 1 on
        public int PuzzleGroup { get; set; } = -1; // -1 off
        public DoodadSpawner Spawner { get; set; }
        public DoodadFuncTask FuncTask { get; set; }

        public uint TimeLeft =>
            GrowthTime > DateTime.UtcNow
                ? (uint)(GrowthTime - DateTime.UtcNow).TotalMilliseconds
                : 0; // TODO formula time of phase

        public bool ToNextPhase { get; set; }
        public int PhaseRatio { get; set; }
        public int CumulativePhaseRatio { get; set; }
        public int OverridePhase { get; set; }
        private bool _deleted = false;
        public VehicleSeat Seat { get; set; }
        private List<uint> ListGroupId { get; set; }
        private List<uint> ListFuncGroupId { get; set; }

        public Doodad()
        {
            _scale = 1f;
            PlantTime = DateTime.MinValue;
            AttachPoint = AttachPointKind.System;
            Seat = new VehicleSeat(this);
            ListGroupId = new List<uint>();
            ListFuncGroupId = new List<uint>();
        }

        public void SetScale(float scale)
        {
            _scale = scale;
        }

        private bool CheckPhase(uint anotherPhase)
        {
            return ListGroupId.Any(phase => phase == anotherPhase);
        }

        private bool CheckFunc(uint anotherPhase)
        {
            return ListFuncGroupId.Any(phase => phase == anotherPhase);
        }

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
                if (caster is Character)
                    _log.Debug("Use: TemplateId {0}, Using phase {1} with SkillId {2}", TemplateId, FuncGroupId, skillId);
                else
                    _log.Trace("Use: TemplateId {0}, Using phase {1} with SkillId {2}", TemplateId, FuncGroupId, skillId);

                ToNextPhase = false; // по умолчанию не выполняем следующую фазу
                ListGroupId = new List<uint>();

                //  first we find the functions, then we execute
                var funcWithSkill = DoodadManager.Instance.GetFunc(FuncGroupId, skillId); // if skillId > 0
                var funcsWithoutSkill = DoodadManager.Instance.GetFuncsForGroup(FuncGroupId);
                if (funcWithSkill == null && funcsWithoutSkill.Count == 0)
                {
                    return;
                }

                if (skillId == 0)
                {
                    foreach (var funcWithoutSkill in funcsWithoutSkill.Where(f => f.FuncType is "DoodadFuncLootItem" or "DoodadFuncLootPack" or "DoodadFuncCutdowning"))
                    {
                        if (DoFunc(caster, 0, funcWithoutSkill))
                        {
                            ListGroupId = new List<uint>();
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
                        _log.Debug("Use: Did not pass the conditions check! TemplateId {0}, Using phase {1} with SkillId {2}", TemplateId, FuncGroupId, skillId);
                        _log.Debug("Use: Ждем взаимодействия с doodad TemplateId {0}, Using phase {1}", TemplateId, FuncGroupId);
                    }
                    else
                        _log.Trace("Use: Did not pass the conditions check! TemplateId {0}, Using phase {1} with SkillId {2}", TemplateId, FuncGroupId, skillId);

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
                    _log.Debug("DoFunc: Finished execution with func = null: TemplateId {0}, Using phase {1} with SkillId {2}", TemplateId, FuncGroupId, skillId);
                else
                    _log.Trace("DoFunc: Finished execution with func = null: TemplateId {0}, Using phase {1} with SkillId {2}", TemplateId, FuncGroupId, skillId);
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
                        if (FuncTask is DoodadFuncTimerTask)
                        {
                            FuncTask?.CancelAsync();
                            FuncTask = null;
                            _log.Debug($"DoFunc::DoodadFuncTimer: The current timer has been canceled. TemplateId {TemplateId}, ObjId {ObjId}, nextPhase {func.NextPhase}");
                        }
                        // Delete doodad
                        if (Spawner != null)
                        {
                            Spawner?.Despawn(this);
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
                    _log.Debug("DoFunc Finished execution withOut ToNextPhase = {3}: TemplateId {0}, Using phase {1} with SkillId {2}", TemplateId, FuncGroupId, skillId, ToNextPhase);
                else
                    _log.Trace("DoFunc Finished execution withOut ToNextPhase = {3}: TemplateId {0}, Using phase {1} with SkillId {2}", TemplateId, FuncGroupId, skillId, ToNextPhase);
                return true;
            }

            return false;
        }

        /// <summary>
        /// start-up for execution of phase functions
        /// </summary>
        /// <param name="caster"></param>
        /// <param name="nextPhase"></param>
        /// <returns>if true, it did not pass the check for the quest (it must be aborted)</returns>
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
                        _log.Debug("DoPhase: Finished execution with recurse: TemplateId {0}, Using phase {1}", TemplateId, FuncGroupId);
                    else
                        _log.Trace("DoPhase: Finished execution with recurse: TemplateId {0}, Using phase {1}", TemplateId, FuncGroupId);

                    ListGroupId = new List<uint>();
                    return true;
                }
                // например, если это ID=898, Prison Gate, то не надо прервать рекурсию
                ListGroupId = new List<uint>();
            }

            if (FuncTask is DoodadFuncTimerTask)
            {
                FuncTask?.CancelAsync();
                if (caster is Character)
                    _log.Debug("DoPhaseFuncs:DoodadFuncTimer: The current timer has been canceled.");
                else
                    _log.Trace("DoPhaseFuncs:DoodadFuncTimer: The current timer has been canceled.");
            }

            if (caster is Character)
                _log.Debug("DoPhaseFuncs: TemplateId {0}, ObjId {1}, nextPhase {2}", TemplateId, ObjId, nextPhase);
            else
                _log.Trace("DoPhaseFuncs: TemplateId {0}, ObjId {1}, nextPhase {2}", TemplateId, ObjId, nextPhase);

            var phaseFuncs = DoodadManager.Instance.GetPhaseFunc(FuncGroupId);
            if (phaseFuncs.Length == 0)
            {
                return false; // no phase functions for FuncGroupId
            }

            //CumulativePhaseRatio = 0; // не требуется
            var stop = false;
            // perform the phase functions one after the other
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
                Save(); // let's save the doodad in the database

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

            if (caster is Character)
                _log.Debug("DoChangePhase: TemplateId {0}, ObjId {1}, nextPhase {2}", TemplateId, ObjId, nextPhase);
            else
                _log.Trace("DoChangePhase: TemplateId {0}, ObjId {1}, nextPhase {2}", TemplateId, ObjId, nextPhase);

            var stop = DoPhaseFuncs(caster, ref nextPhase);

            // the phase change packet call must be after the phase functions to have the correct FuncGroupId in the packet
            BroadcastPacket(new SCDoodadPhaseChangedPacket(this), true); // change the phase to display doodad

            return stop; // if true, it did not pass the check for the quest (it must be aborted)
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
            // TODO has already been called in Create() - this eliminates re-initialization of plants/trees/animals
            //FuncGroupId = GetFuncGroupId();  // Start phase
            var unit = WorldManager.Instance.GetUnit(OwnerObjId);
            DoChangePhase(unit, (int)FuncGroupId);
        }

        public override void BroadcastPacket(GamePacket packet, bool self)
        {
            foreach (var character in WorldManager.Instance.GetAround<Character>(this))
                character.SendPacket(packet);
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

        public PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(ObjId); //The object # in the list
            stream.Write(TemplateId); //The template id needed for that object, the client then uses the template configurations, not the server
            stream.WriteBc(OwnerObjId); //The creator of the object
            stream.WriteBc(ParentObjId); //Things like boats or cars,
            stream.Write((byte)AttachPoint); // attachPoint, relative to the parentObj (Door or window on a house, seats on carriage, etc.)
            if ((AttachPoint > 0) || (ParentObjId > 0))
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
            stream.Write(false); // hasLootItem
            stream.Write(FuncGroupId); // doodad_func_group_id
            stream.Write(OwnerId); // characterId (Database relative)
            stream.Write(UccId);
            stream.Write(ItemTemplateId);
            stream.Write(0u); //??type2
            stream.Write(TimeLeft); // growing
            stream.Write(PlantTime); //Time stamp of when it was planted
            stream.Write(QuestGlow); //When this is higher than 0 it shows a blue orb over the doodad
            stream.Write(0); // family TODO
            stream.Write(PuzzleGroup); // puzzleGroup /for instances maybe?
            stream.Write((byte)OwnerType); // ownerType
            stream.Write(DbHouseId); // dbHouseId
            stream.Write(Data); // data

            return stream;
        }

        public override void Delete()
        {
            base.Delete();
            _deleted = true;

            if (DbId > 0)
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
            }
        }

        public void Save()
        {
            if (!IsPersistent)
                return;
            DbId = DbId > 0 ? DbId : DoodadIdManager.Instance.GetNextId();
            using (var connection = MySQL.CreateConnection())
            {
                using (var command = connection.CreateCommand())
                {
                    // Lookup Parent
                    var parentDoodadId = 0u;
                    if ((Transform?.Parent?.GameObject is Doodad pDoodad) && (pDoodad.DbId > 0))
                        parentDoodadId = pDoodad.DbId;

                    command.CommandText =
                        "REPLACE INTO doodads (`id`, `owner_id`, `owner_type`, `template_id`, `current_phase_id`, `plant_time`, `growth_time`, `phase_time`, `x`, `y`, `z`, `roll`, `pitch`, `yaw`, `item_id`, `house_id`, `parent_doodad`, `item_template_id`, `item_container_id`, `data`) " +
                        "VALUES(@id, @owner_id, @owner_type, @template_id, @current_phase_id, @plant_time, @growth_time, @phase_time, @x, @y, @z, @roll, @pitch, @yaw, @item_id, @house_id, @parent_doodad, @item_template_id, @item_container_id, @data)";
                    command.Parameters.AddWithValue("@id", DbId);
                    command.Parameters.AddWithValue("@owner_id", OwnerId);
                    command.Parameters.AddWithValue("@owner_type", OwnerType);
                    command.Parameters.AddWithValue("@template_id", TemplateId);
                    command.Parameters.AddWithValue("@current_phase_id", FuncGroupId);
                    command.Parameters.AddWithValue("@plant_time", PlantTime);
                    command.Parameters.AddWithValue("@growth_time", GrowthTime);
                    command.Parameters.AddWithValue("@phase_time", DateTime.MinValue);
                    // We save it's world position, and upon loading, we re-parent things depending on the data
                    command.Parameters.AddWithValue("@x", Transform?.Local.Position.X ?? 0f);
                    command.Parameters.AddWithValue("@y", Transform?.Local.Position.Y ?? 0f);
                    command.Parameters.AddWithValue("@z", Transform?.Local.Position.Z ?? 0f);
                    command.Parameters.AddWithValue("@roll", Transform?.Local.Rotation.X ?? 0f);
                    command.Parameters.AddWithValue("@pitch", Transform?.Local.Rotation.Y ?? 0f);
                    command.Parameters.AddWithValue("@yaw", Transform?.Local.Rotation.Z ?? 0f);
                    command.Parameters.AddWithValue("@item_id", ItemId);
                    command.Parameters.AddWithValue("@house_id", DbHouseId);
                    command.Parameters.AddWithValue("@parent_doodad", parentDoodadId);
                    command.Parameters.AddWithValue("@item_template_id", ItemTemplateId);
                    command.Parameters.AddWithValue("@item_container_id", GetItemContainerId());
                    command.Parameters.AddWithValue("@data", Data);
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
                if ((child.GameObject is Doodad doodad) && (doodad.DbId > 0))
                    return false;
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
}
