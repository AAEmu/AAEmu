using System;
using System.Collections.Generic;
using System.Linq;

using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
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
using AAEmu.Game.Utils.DB;

using NLog;
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
метод public void Use(Unit caster, uint skillId) запускает в цикле:

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
        private static Logger _log = LogManager.GetCurrentClassLogger();
        private float _scale;
        public uint TemplateId { get; set; }
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
        public int Data { get; set; }
        public uint QuestGlow { get; set; } //0 off // 1 on
        public DoodadSpawner Spawner { get; set; }
        public DoodadFuncTask FuncTask { get; set; }
        public uint TimeLeft => GrowthTime > DateTime.UtcNow ? (uint)(GrowthTime - DateTime.UtcNow).TotalMilliseconds : 0; // TODO formula time of phase
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
         */

        public void Use(Unit caster, uint skillId)
        {
            if (caster == null) { return; }
            ListFuncGroupId = new List<uint>();

            while (true)
            {
                _log.Debug("Use: TemplateId {0}, Using phase {1} with SkillId {2}", TemplateId, FuncGroupId, skillId);
                ToNextPhase = false;
                if (!ListFuncGroupId.Contains(FuncGroupId))
                {
                    ListFuncGroupId.Add(FuncGroupId); // для проверки CheckPhase()
                }
                else
                {
                    _log.Debug("Use: Finished execution with recurse: TemplateId {0}, Using phase {1} with SkillId {2}", TemplateId, FuncGroupId, skillId);
                    return;
                }

                //  сначала найдем функции, затем выполняем
                var func = DoodadManager.Instance.GetFunc(FuncGroupId, skillId); // если skillId > 0
                var funcs = DoodadManager.Instance.GetFuncsForGroup(FuncGroupId); // если несколько функций с одним NextPhase
                if (funcs.Count > 1)
                {
                    // проверка на то, что функции надо выполнить, если у них NextPhase одинаковые
                    var res = true;
                    var prev = 0;
                    foreach (var a in funcs)
                    {
                        if (prev == 0)
                        {
                            prev = a.NextPhase;
                            continue;
                        }
                        res = a.NextPhase != prev;
                    }
                    if (res)
                    {
                        if (DoFunc(caster, skillId, func)) { return; }
                    }
                    else
                    {
                        foreach (var fu in funcs)
                        {
                            if (DoFunc(caster, skillId, fu)) { return; }
                        }
                    }
                }
                else
                {
                    if (DoFunc(caster, skillId, func)) { return; }
                }
                // затем выполняем фазовые функции (может произойти изменение FuncGroupId на другое, чем было ранее)
                var stop = DoPhaseFuncs(caster, (int)FuncGroupId);
                if (stop)
                {
                    // не прошли проверку условий для квестов или нет фазовой функции
                    _log.Debug("Use:DoPhaseFuncs Did not pass the conditions check! TemplateId {0}, Using phase {1} with SkillId {2}", TemplateId, FuncGroupId, skillId);
                    return;
                }
                skillId = 0; // надо ли обнулять?
            }
        }

        /// <summary>
        /// запуск функций
        /// </summary>
        /// <param name="caster"></param>
        /// <param name="skillId"></param>
        /// <param name="func"></param>
        /// <returns>если TRUE, то останавливаем дальнейшее выполнение функций, ждем взаимодействия</returns>
        private bool DoFunc(Unit caster, uint skillId, DoodadFunc func)
        {
            // при отсутствии функции завершим цикл
            if (func == null)
            {
                _log.Debug("Use:DoFunc Finished execution with func = null: TemplateId {0}, Using phase {1} with SkillId {2}", TemplateId, FuncGroupId, skillId);
                return true;
            }
            // далее выполняем функцию
            func.Use(caster, this, skillId, func.NextPhase);
            if (func.SoundId > 0)
            {
                BroadcastPacket(new SCDoodadSoundPacket(this, func.SoundId), true);
            }

            if (ToNextPhase)
            {
                if (func.NextPhase == -1) { Delete(); } // Delete doodad
                FuncGroupId = (uint)func.NextPhase;
            }
            else
            {
                _log.Debug("Use:DoFunc Finished execution with ToNextPhase = {3}: TemplateId {0}, Using phase {1} with SkillId {2}", TemplateId, FuncGroupId, skillId, ToNextPhase);
                return true;
            }

            return false;
        }

        /// <summary>
        /// запуск фазовых функций
        /// </summary>
        /// <param name="caster"></param>
        /// <param name="nextPhase"></param>
        /// <returns>если true, то это не прошли проверку для квеста (необходимо прервать выполнение)</returns>
        private bool DoPhase(Unit caster, int nextPhase)
        {
            ListGroupId = new List<uint>();
            if (nextPhase <= 0) { return false; }

            if (FuncTask is DoodadFuncTimerTask)
            {
                FuncTask?.Cancel();
                if (caster is Character)
                    _log.Debug("DoPhase:DoodadFuncTimer: The current timer has been canceled.");
                else
                    _log.Trace("DoPhase:DoodadFuncTimer: The current timer has been canceled.");
            }
            if (caster is Character)
                _log.Debug("DoPhase: TemplateId {0}, ObjId {1}, nextPhase {2}", TemplateId, ObjId, nextPhase);
            else
                _log.Trace("DoPhase: TemplateId {0}, ObjId {1}, nextPhase {2}", TemplateId, ObjId, nextPhase);

            // меняем фазу
            FuncGroupId = (uint)nextPhase;
            var phaseFuncs = DoodadManager.Instance.GetPhaseFunc(FuncGroupId);
            if (phaseFuncs.Length == 0)
            {
                return false; // нет фазовых функций для FuncGroupId
            }
            PhaseRatio = Rand.Next(0, 10000);
            CumulativePhaseRatio = 0;
            var stop = false;
            // по очереди выполняем фазовые функции
            foreach (var phaseFunc in phaseFuncs)
            {
                if (phaseFunc == null) { continue; }
                stop = phaseFunc.Use(caster, this);
                if (stop)
                {
                    break; // прерываем выполнение фазовых функций и переходим к OverridePhase
                }
            }
            if (OverridePhase != 0 && stop && FuncGroupId != OverridePhase)
            {
                nextPhase = OverridePhase;
                return DoPhase(caster, nextPhase);
            }

            if (!_deleted)
                Save(); // сохраним doodad в базе данных

            return stop; // если true, то это не прошли проверку для квеста (необходимо прервать выполнение)
        }

        /// <summary>
        /// запуск фазовых функций и смена фазы
        /// </summary>
        /// <param name="caster"></param>
        /// <param name="nextPhase"></param>
        /// <returns>если TRUE, то это не прошли проверку для квеста (необходимо прервать выполнение)</returns>
        public bool DoPhaseFuncs(Unit caster, int nextPhase)
        {
            if (nextPhase <= 0) { return false; }
            if (caster is Character)
                _log.Debug("DoPhaseFuncs: TemplateId {0}, ObjId {1}, nextPhase {2}", TemplateId, ObjId, nextPhase);
            else
                _log.Trace("DoPhaseFuncs: TemplateId {0}, ObjId {1}, nextPhase {2}", TemplateId, ObjId, nextPhase);

            // запускаем фазовые функции
            var stop = DoPhase(caster, nextPhase);
            // вызов пакета смены фазы должен быть после фазовых функций, что бы был правильный TimeLeft в пакете
            BroadcastPacket(new SCDoodadPhaseChangedPacket(this), true);

            return stop; // если true, то это не прошли проверку для квеста (необходимо прервать выполнение)
        }

        public uint GetFuncGroupId()
        {
            return (from funcGroup in Template.FuncGroups
                    where funcGroup.GroupKindId == DoodadFuncGroups.DoodadFuncGroupKind.Start
                    select funcGroup.Id).FirstOrDefault();
        }

        public void OnSkillHit(Unit caster, uint skillId)
        {
            var funcs = DoodadManager.Instance.GetFuncsForGroup(FuncGroupId);
            foreach (var func in funcs)
            {
                if (func.FuncType == "DoodadFuncSkillHit")
                {
                    Use(caster, skillId);
                }
            }
        }

        /// <summary>
        /// Инициализация doodad начальной фазой
        /// </summary>
        public override void Spawn()
        {
            base.Spawn();
            FuncGroupId = GetFuncGroupId();  // Start phase
            var unit = WorldManager.Instance.GetUnit(OwnerObjId);
            _log.Trace("Doing phase {0} for WorldDoodad TemplateId {1}, objId {2}", FuncGroupId, TemplateId, ObjId);
            DoPhaseFuncs(unit, (int)FuncGroupId);
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
            stream.Write(-1); // puzzleGroup /for instances maybe?
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
                    var parentDoodadId = 0u;
                    if ((Transform?.Parent?.GameObject is Doodad pDoodad) && (pDoodad.DbId > 0))
                        parentDoodadId = pDoodad.DbId;

                    command.CommandText =
                        "REPLACE INTO doodads (`id`, `owner_id`, `owner_type`, `template_id`, `current_phase_id`, `plant_time`, `growth_time`, `phase_time`, `x`, `y`, `z`, `roll`, `pitch`, `yaw`, `item_id`, `house_id`, `parent_doodad`, `item_template_id`) " +
                        "VALUES(@id, @owner_id, @owner_type, @template_id, @current_phase_id, @plant_time, @growth_time, @phase_time, @x, @y, @z, @roll, @pitch, @yaw, @item_id, @house_id, @parent_doodad, @item_template_id)";
                    command.Parameters.AddWithValue("@id", DbId);
                    command.Parameters.AddWithValue("@owner_id", OwnerId);
                    command.Parameters.AddWithValue("@owner_type", OwnerType);
                    command.Parameters.AddWithValue("@template_id", TemplateId);
                    command.Parameters.AddWithValue("@current_phase_id", FuncGroupId);
                    command.Parameters.AddWithValue("@plant_time", PlantTime);
                    command.Parameters.AddWithValue("@growth_time", GrowthTime);
                    command.Parameters.AddWithValue("@phase_time", DateTime.MinValue);
                    // We save it's world position, and upon loading, we re-parent things depending on the data
                    if (Transform != null)
                    {
                        command.Parameters.AddWithValue("@x", Transform.World.Position.X);
                        command.Parameters.AddWithValue("@y", Transform.World.Position.Y);
                        command.Parameters.AddWithValue("@z", Transform.World.Position.Z);
                        command.Parameters.AddWithValue("@roll", Transform.World.Rotation.X);
                        command.Parameters.AddWithValue("@pitch", Transform.World.Rotation.Y);
                        command.Parameters.AddWithValue("@yaw", Transform.World.Rotation.Z);
                    }
                    command.Parameters.AddWithValue("@item_id", ItemId);
                    command.Parameters.AddWithValue("@house_id", DbHouseId);
                    command.Parameters.AddWithValue("@parent_doodad", parentDoodadId);
                    command.Parameters.AddWithValue("@item_template_id", ItemTemplateId);
                    command.Prepare();
                    command.ExecuteNonQuery();
                }
            }
        }
    }
}
