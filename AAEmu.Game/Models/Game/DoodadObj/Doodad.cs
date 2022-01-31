using System;
using System.Collections.Generic;
using System.Numerics;
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
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Models.Tasks.Doodads;
using AAEmu.Game.Utils.DB;
using AAEmu.Game.Utils;
using NLog;
using System.Threading.Tasks;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.World.Interactions;

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
        public bool NeedChangePhase { get; set; }
        public int PhaseRatio { get; set; }
        public int CumulativePhaseRatio { get; set; }
        public uint OverridePhase { get; set; }
        private bool _deleted = false;
        public VehicleSeat Seat { get; set; }

        public Doodad()
        {
            _scale = 1f;
            PlantTime = DateTime.MinValue;
            AttachPoint = AttachPointKind.System;
            Seat = new VehicleSeat(this);
        }

        public void SetScale(float scale)
        {
            _scale = scale;
        }

        /// <summary>
        /// This "uses" the doodad. Using a doodad means running its functions in doodad_funcs
        /// </summary>
        public void UseNew(Unit unit, uint skillId)
        {
            try
            {
                _log.Warn("[Doodad] UseNew: Doodad {0}, TemplateId {1}. Using skill {2} with doodad phase {3}", ObjId, TemplateId, skillId, FuncGroupId);
                var doodadFuncGroups = DoodadManager.Instance.GetDoodadFuncGroups(TemplateId);
                if (doodadFuncGroups.Count <= 0)
                {
                    return;
                }
                var find = false;
                foreach (var doodadFuncGroup in doodadFuncGroups)
                {
                    /*
                     * 4623 -> 4717 - Timer (60000) 4623, Tod (400) 4624 
                    */
                    if (FuncGroupId != 0)
                    {
                        if (doodadFuncGroup.Id != FuncGroupId && !find)
                        {
                            continue; // пропустим все фазы до текущей
                        }
                    }
                    find = true;
                    //if (doodadFunc.Model.Length != 0 || doodadFunc.Model != "a://invalid")
                    //    return; // закончим цикл функций, так как вернулись к первой
                    FuncGroupId = doodadFuncGroup.Id;
                    // Get all doodad_funcs
                    var doodadFuncs = DoodadManager.Instance.GetDoodadFuncs(FuncGroupId); // 4623
                    foreach (var func in doodadFuncs)
                    {
                        /*
                         * 4623 -> 533 DoodadFakeUse 13167, NextPhase -> 4717
                        */
                        /*
                         * для 4623 -> FakeUse 4717
                         * для 4624 -> FakeUse 4718
                        */
                        func?.Use(unit, this, skillId, func.NextPhase);                        
                        if (FuncTask != null)
                        {
                            _ = FuncTask.Cancel();
                            FuncTask = null;
                        }
                        if (func.SoundId > 0)
                        {
                            BroadcastPacket(new SCDoodadSoundPacket(this, func.SoundId), true);
                        }
                        // Get all doodad_phase_funcs
                        var phaseFuncs = DoodadManager.Instance.GetPhaseFunc((uint)func.NextPhase); // 4717
                        foreach (var phaseFunc in phaseFuncs)
                        {
                            //  для 4623 -> 4717 Tod (400) 4624 - выкл, Timer (60000) 4623 - вкл
                            //  для 4624 -> 4718 Tod (400) 4623 - вкл, Timer (60000) 4624 - выкл
                            phaseFunc?.Use(unit, this, skillId, func.NextPhase);
                            FuncGroupId = (uint)func.NextPhase;
                            //if (NeedChangePhase)
                            {
                                if (OverridePhase > 0)
                                {
                                    FuncGroupId = OverridePhase; // 4624 - выкл
                                }
                            }
                            GoToPhaseChanged(unit, (int)FuncGroupId); // 4624 - выкл
                        }
                        //if (FuncGroupId == (uint)func.NextPhase || func.NextPhase == -1)
                        //    return; // закончим цикл функций, так как вернулись к первой или функций нет
                    }
                }
            }
            catch (Exception e)
            {
                _log.Fatal(e, "[Doodad] Doodad func crashed !");
            }
        }

        /// <summary>
        /// This executes a doodad's phase. Phase functions start as soon as the doodad switches to a new phase.
        /// </summary>
        public void GoToPhaseChanged(Unit unit, int funcGroupId, uint skillId = 0)
        {
            _log.Warn("[Doodad] GoToPhase: Using skill {0} with doodad  phase {1}", skillId, funcGroupId);

            if (funcGroupId == -1)
            {
                // Delete doodad
                Delete();
            }
            else
            {
                FuncGroupId = (uint)funcGroupId; // 4623 - вкл

                _log.Debug("[Doodad] SCDoodadPhaseChangedPacket: FuncGroupId {0}", FuncGroupId);
                BroadcastPacket(new SCDoodadPhaseChangedPacket(this), true); // 4623 - вкл
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
            var funcs = DoodadManager.Instance.GetFuncsForGroup(FuncGroupId);
            foreach (var func in funcs)
            {
                if (func.FuncType == "DoodadFuncSkillHit")
                {
                    UseNew(null, skillId);
                }
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
                    command.Parameters.AddWithValue("@x", Transform.World.Position.X);
                    command.Parameters.AddWithValue("@y", Transform.World.Position.Y);
                    command.Parameters.AddWithValue("@z", Transform.World.Position.Z);
                    command.Parameters.AddWithValue("@roll", Transform.World.Rotation.X);
                    command.Parameters.AddWithValue("@pitch", Transform.World.Rotation.Y);
                    command.Parameters.AddWithValue("@yaw", Transform.World.Rotation.Z);
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
