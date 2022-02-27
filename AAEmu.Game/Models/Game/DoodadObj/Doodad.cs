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
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Tasks.Doodads;
using AAEmu.Game.Utils.DB;

using NLog;

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
        public int PhaseRatio { get; set; }
        public int CumulativePhaseRatio { get; set; }
        public int OverridePhase { get; set; }
        private bool _deleted = false;
        public VehicleSeat Seat { get; set; }
        private List<uint> ListGroupId { get; set; }

        public Doodad()
        {
            _scale = 1f;
            PlantTime = DateTime.MinValue;
            AttachPoint = AttachPointKind.System;
            Seat = new VehicleSeat(this);
            ListGroupId = new List<uint>();
            //ListGroupId.AddRange(DoodadManager.Instance.GetDoodadFuncGroupsId(TemplateId));
        }

        public void SetScale(float scale)
        {
            _scale = scale;
        }
        private bool CheckPhase(uint anotherPhase)
        {
            return ListGroupId.Any(phase => phase == anotherPhase);
        }

        public void Use(Unit caster, uint skillId)
        {
            while (true)
            {
                _log.Debug("Use: TemplateId {0}, Using phase {1}", TemplateId, FuncGroupId);

                var func = DoodadManager.Instance.GetFunc(FuncGroupId, skillId);
                if (func == null)
                {
                    ListGroupId = new List<uint>();
                    return;
                }

                if (!ListGroupId.Contains(FuncGroupId))
                {
                    ListGroupId.Add(FuncGroupId); // для проверки CheckPhase()
                    // TemplateId= 2322
                    // 4623 - 4717 false
                    // 4717 - 4623 true

                    // TemplateId= 2309
                    // 4591 - 4592 false
                    // 4592 - 4593 false
                    // 4593 - 4594 false
                    // 4594 - 4592 true
                }

                func.Use(caster, this, skillId);
                if (func.NextPhase <= 0)
                {
                    ListGroupId = new List<uint>();
                    return;
                }

                if (func.SoundId > 0)
                {
                    BroadcastPacket(new SCDoodadSoundPacket(this, func.SoundId), true);
                }

                DoPhaseFuncs(caster, func.NextPhase);

                #region nextFunc

                var nextFunc = DoodadManager.Instance.GetFunc((uint)func.NextPhase, skillId);

                // проверки на завершение цикла функций
                if (nextFunc == null || nextFunc.NextPhase == -1)
                {
                    // закончились функции
                    ListGroupId = new List<uint>();
                    DoPhaseFuncs(caster, func.NextPhase);
                    return;
                }

                // проверка на зацикливание
                if (CheckPhase((uint)nextFunc.NextPhase))
                {
                    nextFunc.Use(caster, this, nextFunc.SkillId);
                    ListGroupId = new List<uint>();
                    return;
                }

                #endregion nextFunc

                skillId = 0;
            }
        }

        /// <summary>
        /// выполняем фазовые функции с прерыванием таймера
        /// </summary>
        /// <param name="caster"></param>
        /// <param name="nextPhase">фаза на которую необходимо переключиться</param>
        public void DoPhase(Unit caster, int nextPhase)
        {
            while (true)
            {
                if (nextPhase <= 0) { return; }

                _log.Debug("DoPhase: [0] TemplateId {0}, ObjId {1}, nextPhase {2}", TemplateId, ObjId, nextPhase);
                if (FuncTask is DoodadFuncTimerTask)
                {
                    _ = FuncTask.Cancel();
                    FuncTask = null;
                    _log.Trace("DoPhase: TemplateId {0}, ObjId {1}. The current timer has been cancelled.", TemplateId, ObjId);
                }
                FuncGroupId = (uint)nextPhase;
                if (!ListGroupId.Contains(FuncGroupId))
                {
                    ListGroupId.Add(FuncGroupId); // для проверки CheckPhase()
                }
                // проверка на зацикливание
                if (CheckPhase((uint)nextPhase))
                {
                    ListGroupId = new List<uint>();
                    return;
                }
                BroadcastPacket(new SCDoodadPhaseChangedPacket(this), true);
                var phaseFuncs = DoodadManager.Instance.GetPhaseFunc(FuncGroupId);
                if (phaseFuncs.Length == 0)
                {
                    ListGroupId = new List<uint>();
                    return; // нет фазовых функций для FuncGroupId
                }
                var stop = false;
                PhaseRatio = Rand.Next(0, 10000);
                CumulativePhaseRatio = 0;
                foreach (var phaseFunc in phaseFuncs)
                {
                    if (phaseFunc == null) { continue; }
                    stop = phaseFunc.Use(caster, this);
                    if (stop) // если TRUE - прерываем выполнение фазовых функций и переходим к OverridePhase
                    {
                        break;
                    }
                }

                if (OverridePhase != 0 && stop && FuncGroupId != OverridePhase)
                {
                    nextPhase = OverridePhase;
                    // проверка на зацикливание
                    if (CheckPhase((uint)nextPhase))
                    {
                        ListGroupId = new List<uint>();
                        return;
                    }
                    continue;
                }

                //_log.Debug("DoPhase: [2] TemplateId {0}, ObjId {1}, nextPhase {2}", TemplateId, ObjId, FuncGroupId);
                //BroadcastPacket(new SCDoodadPhaseChangedPacket(this), true);

                if (!_deleted)
                    Save();

                ListGroupId = new List<uint>();
                return;
            }
        }
        /// <summary>
        /// Выполнение фазовых функций
        /// </summary>
        /// <param name="caster"></param>
        /// <param name="nextPhase"></param>
        public void DoPhaseFuncs(Unit caster, int nextPhase)
        {
            while (true)
            {
                if (nextPhase <= 0) { return; }

                _log.Debug("DoPhaseFuncs: [0] TemplateId {0}, ObjId {1}, nextPhase {2}", TemplateId, ObjId, nextPhase);
                FuncGroupId = (uint)nextPhase;
                if (!ListGroupId.Contains(FuncGroupId))
                {
                    ListGroupId.Add(FuncGroupId); // для проверки CheckPhase()
                }
                BroadcastPacket(new SCDoodadPhaseChangedPacket(this), true);
                var phaseFuncs = DoodadManager.Instance.GetPhaseFunc(FuncGroupId);
                if (phaseFuncs.Length == 0)
                {
                    ListGroupId = new List<uint>();
                    return; // нет фазовых функций для FuncGroupId
                }
                var stop = false;
                PhaseRatio = Rand.Next(0, 10000);
                CumulativePhaseRatio = 0;
                foreach (var phaseFunc in phaseFuncs)
                {
                    if (phaseFunc == null) { continue; }
                    stop = phaseFunc.Use(caster, this);
                    if (stop) // если TRUE - прерываем выполнение фазовых функций и переходим к OverridePhase
                    {
                        break;
                    }
                }

                if (OverridePhase != 0 && stop && FuncGroupId != OverridePhase)
                {
                    nextPhase = OverridePhase;
                    // проверка на зацикливание
                    if (CheckPhase((uint)nextPhase))
                    {
                        ListGroupId = new List<uint>();
                        return;
                    }
                    continue;
                }

                //_log.Debug("DoPhaseFuncs: [2] TemplateId {0}, ObjId {1}, nextPhase {2}", TemplateId, ObjId, FuncGroupId);
                //BroadcastPacket(new SCDoodadPhaseChangedPacket(this), true);

                if (!_deleted)
                    Save();

                ListGroupId = new List<uint>();
                return;
            }
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
                    Use(null, skillId);
                }
            }
        }

        /// <summary>
        /// Инициализация doodad начальной фазой
        /// </summary>
        public override void Spawn()
        {
            base.Spawn();
            _log.Trace("Doing phase {0} for doodad TemplateId {1}, objId {2}", FuncGroupId, TemplateId, ObjId);
            FuncGroupId = GetFuncGroupId();  // Start phase
            var unit = WorldManager.Instance.GetUnit(OwnerObjId);
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
