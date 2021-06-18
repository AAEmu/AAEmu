using System;
using System.Collections.Generic;

using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
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
        public byte AttachPoint { get; set; }
        public Point AttachPosition { get; set; }
        public uint DbHouseId { get; set; }
        public int Data { get; set; }
        public uint QuestGlow { get; set; } //0 off // 1 on
        public DoodadSpawner Spawner { get; set; }
        public DoodadFuncTask FuncTask { get; set; }
        public uint TimeLeft => GrowthTime > DateTime.Now ? (uint)(GrowthTime - DateTime.Now).TotalMilliseconds : 0; // TODO formula time of phase
        public bool ToPhaseAndUse { get; set; }
        public int PhaseRatio { get; set; }
        public int CumulativePhaseRatio { get; set; }
        public uint CurrentPhaseId { get; set; }
        public uint OverridePhase { get; set; }
        private bool _deleted = false;
        public VehicleSeat Seat { get; set; }

        public Doodad()
        {
            _scale = 1f;
            Position = new Point();
            PlantTime = DateTime.MinValue;
            AttachPoint = 255;
            Seat = new VehicleSeat();
        }

        public void SetScale(float scale)
        {
            _scale = scale;
        }

        // public void DoFirstPhase(Unit unit)
        // {
        //     
        // }

        /// <summary>
        /// This "uses" the doodad. Using a doodad means running its functions in doodad_funcs
        /// </summary>
        public void Use(Unit unit, uint skillId, uint recursionDepth = 0)
        {
            recursionDepth++;
            if (recursionDepth % 10 == 0)
                _log.Warn("Doodad {0} (TemplateId {1}) might be looping indefinitely. {2} recursionDepth.", ObjId, TemplateId, recursionDepth);
            _log.Trace("Using phase {0}", CurrentPhaseId);
            // Get all doodad_funcs
            var funcs = DoodadManager.Instance.GetFuncsForGroup(CurrentPhaseId);

            // Apply them
            var nextFunc = 0;
            var isUse = false;
            ToPhaseAndUse = false;

            try
            {
                foreach (var func in funcs)
                {
                    if ((func.SkillId <= 0 || func.SkillId != skillId) && func.SkillId != 0)
                        continue;

                    func.Use(unit, this, skillId, func.NextPhase);

                    if (ToPhaseAndUse)
                    {
                        isUse = true;
                        nextFunc = func.NextPhase;
                    }

                    break;
                }
            }
            catch (Exception e)
            {
                _log.Fatal(e, "Doodad func crashed !");
            }

            if (nextFunc == 0)
                return;

            if (isUse)
                GoToPhaseAndUse(unit, nextFunc, skillId, recursionDepth);
            else
                GoToPhase(unit, nextFunc);
        }

        /// <summary>
        /// This executes a doodad's phase. Phase functions start as soon as the doodad switches to a new phase.
        /// </summary>
        public void DoPhase(Unit unit, uint skillId, uint recursionDepth = 0)
        {
            recursionDepth++;
            if (recursionDepth % 10 == 0)
                _log.Warn("Doodad {0} (TemplateId {1}) might be phasing indefinitely. {2} recursionDepth.", ObjId, TemplateId, recursionDepth);

            _log.Trace("Doing phase {0}", CurrentPhaseId);
            var phaseFuncs = DoodadManager.Instance.GetPhaseFunc(CurrentPhaseId);

            OverridePhase = 0;
            try
            {
                foreach (var phaseFunc in phaseFuncs)
                {
                    phaseFunc.Use(unit, this, skillId);
                    if (OverridePhase > 0)
                        break;
                }
            }
            catch (Exception e)
            {
                _log.Fatal(e, "Doodad phase crashed!");
            }

            if (OverridePhase > 0)
            {
                CurrentPhaseId = OverridePhase;
                DoPhase(unit, skillId, recursionDepth);
            }

            if (!_deleted)
                Save();
        }

        /// <summary>
        /// Changes the doodad's phase
        /// </summary>
        /// <param name="unit">Unit who triggered the change</param>
        /// <param name="funcGroupId">New phase to go to</param>
        public void GoToPhase(Unit unit, int funcGroupId, uint skillId = 0)
        {
            _log.Trace("Going to phase {0}", funcGroupId);
            if (funcGroupId == -1)
            {
                // Delete doodad
                Delete();
            }
            else
            {
                CurrentPhaseId = (uint)funcGroupId;
                PhaseRatio = Rand.Next(0, 10000);
                CumulativePhaseRatio = 0;
                DoPhase(unit, skillId);

                _log.Debug("SCDoodadPhaseChangedPacket : CurrentPhaseId {0}", CurrentPhaseId);
                BroadcastPacket(new SCDoodadPhaseChangedPacket(this), true);
            }
        }

        public void GoToPhaseAndUse(Unit unit, int funcGroupId, uint skillId, uint recursionDepth = 0)
        {
            recursionDepth++;
            _log.Trace("Going to phase {0} and using it", funcGroupId);
            if (funcGroupId == -1)
            {
                // Delete doodad
                Delete();
            }
            else
            {
                CurrentPhaseId = (uint)funcGroupId;
                PhaseRatio = Rand.Next(0, 10000);
                CumulativePhaseRatio = 0;
                DoPhase(unit, skillId);

                _log.Debug("SCDoodadPhaseChangedPacket : CurrentPhaseId {0}", CurrentPhaseId);
                BroadcastPacket(new SCDoodadPhaseChangedPacket(this), true);

                Use(unit, skillId, recursionDepth);
            }
        }

        public List<uint> GetStartFuncs()
        {
            var startGroupIds = new List<uint>();
            foreach (var funcGroup in Template.FuncGroups)
            {
                if (funcGroup.GroupKindId == DoodadFuncGroups.DoodadFuncGroupKind.Start)
                    startGroupIds.Add(funcGroup.Id);
            }

            return startGroupIds;
        }

        public uint GetFuncGroupId()
        {
            foreach (var funcGroup in Template.FuncGroups)
            {
                if (funcGroup.GroupKindId == DoodadFuncGroups.DoodadFuncGroupKind.Start)
                    return funcGroup.Id;
            }
            return 0;
        }

        public void OnSkillHit(Unit caster, uint skillId)
        {
            var funcs = DoodadManager.Instance.GetFuncsForGroup(CurrentPhaseId);
            foreach (var func in funcs)
            {
                if (func.FuncType == "DoodadFuncSkillHit")
                {
                    Use(null, skillId);
                }
            }
        }

        public override void BroadcastPacket(GamePacket packet, bool self)
        {
            foreach (var character in WorldManager.Instance.GetAround<Character>(this))
                character.SendPacket(packet);
        }

        public override void AddVisibleObject(Character character)
        {
            character.SendPacket(new SCDoodadCreatedPacket(this));
        }

        public override void RemoveVisibleObject(Character character)
        {
            if (character.CurrentTarget != null && character.CurrentTarget == this)
            {
                character.CurrentTarget = null;
                character.SendPacket(new SCTargetChangedPacket(character.ObjId, 0));
            }
            character.SendPacket(new SCDoodadRemovedPacket(ObjId));
        }

        public PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(ObjId); //The object # in the list
            stream.Write(TemplateId); //The template id needed for that object, the client then uses the template configurations, not the server
            stream.WriteBc(OwnerObjId); //The creator of the object
            stream.WriteBc(ParentObjId); //Things like boats or cars,
            stream.Write(AttachPoint); // attachPoint, relative to the parentObj, (Door or window on a house)
            if (AttachPoint != 255 && AttachPosition != null)
            {
                stream.WritePosition(AttachPosition.X, AttachPosition.Y, AttachPosition.Z);
                stream.Write(Helpers.ConvertRotation(AttachPosition.RotationX)); //''
                stream.Write(Helpers.ConvertRotation(AttachPosition.RotationY)); //''
                stream.Write(Helpers.ConvertRotation(AttachPosition.RotationZ)); //''
            }
            else
            {
                stream.WritePosition(Position.X, Position.Y, Position.Z); //self explanatory
                stream.Write(Helpers.ConvertRotation(Position.RotationX)); //''
                stream.Write(Helpers.ConvertRotation(Position.RotationY)); //''
                stream.Write(Helpers.ConvertRotation(Position.RotationZ)); //''
            }

            stream.Write(Scale); //The size of the object
            stream.Write(false); // hasLootItem
            stream.Write(CurrentPhaseId); // doodad_func_group_id
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
            DbId = DbId > 0 ? DbId : DoodadIdManager.Instance.GetNextId();
            using (var connection = MySQL.CreateConnection())
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText =
                        "REPLACE INTO doodads (id, owner_id, owner_type, template_id, current_phase_id, plant_time, growth_time, phase_time, x, y, z, rotation_x, rotation_y, rotation_z) " +
                        "VALUES(@id, @owner_id, @owner_type, @template_id, @current_phase_id, @plant_time, @growth_time, @phase_time, @x, @y, @z, @rotation_x, @rotation_y, @rotation_z)";
                    command.Parameters.AddWithValue("@id", DbId);
                    command.Parameters.AddWithValue("@owner_id", OwnerId);
                    command.Parameters.AddWithValue("@owner_type", OwnerType);
                    command.Parameters.AddWithValue("@template_id", TemplateId);
                    command.Parameters.AddWithValue("@current_phase_id", CurrentPhaseId);
                    command.Parameters.AddWithValue("@plant_time", PlantTime);
                    command.Parameters.AddWithValue("@growth_time", GrowthTime);
                    command.Parameters.AddWithValue("@phase_time", DateTime.MinValue);
                    command.Parameters.AddWithValue("@x", Position.X);
                    command.Parameters.AddWithValue("@y", Position.Y);
                    command.Parameters.AddWithValue("@z", Position.Z);
                    command.Parameters.AddWithValue("@rotation_x", Position.RotationX);
                    command.Parameters.AddWithValue("@rotation_y", Position.RotationY);
                    command.Parameters.AddWithValue("@rotation_z", Position.RotationZ);
                    command.Prepare();
                    command.ExecuteNonQuery();
                }
            }
        }
    }
}
