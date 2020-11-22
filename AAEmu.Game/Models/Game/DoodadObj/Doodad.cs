﻿using System;
using System.Collections.Generic;
using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Tasks.Doodads;
using NLog;

namespace AAEmu.Game.Models.Game.DoodadObj
{
    public class Doodad : BaseUnit
    {
        private float _scale;

        private static Logger _log = LogManager.GetCurrentClassLogger();
        public uint TemplateId { get; set; }
        public DoodadTemplate Template { get; set; }
        public override float Scale => _scale;
        public uint FuncGroupId { get; set; }
        public string FuncType { get; set; }
        public ulong ItemId { get; set; }
        public DateTime GrowthTime { get; set; }
        public DateTime PlantTime { get; set; }
        public uint OwnerId { get; set; }
        public uint OwnerObjId { get; set; }
        public uint ParentObjId { get; set; }
        public DoodadOwnerType OwnerType { get; set; }
        public byte AttachPoint { get; set; }
        public uint DbHouseId { get; set; }
        public int Data { get; set; }
        public uint QuestGlow { get; set; } //0 off // 1 on

        public DoodadSpawner Spawner { get; set; }
        public DoodadFuncTask FuncTask { get; set; }

        public uint TimeLeft => GrowthTime > DateTime.Now ? (uint)(GrowthTime - DateTime.Now).TotalMilliseconds : 0; // TODO formula time of phase
        public bool cancelPhasing { get; set; }
        
        public uint CurrentPhaseId { get; set; }
        public uint OverridePhase { get; set; }

        public Doodad()
        {
            _scale = 1f;
            Position = new Point();
            PlantTime = DateTime.MinValue;
            AttachPoint = 255;
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
        public void Use(Unit unit, uint skillId)
        {
            _log.Debug("Using phase {0}", CurrentPhaseId);
            // Get all doodad_funcs
            var funcs = DoodadManager.Instance.GetFuncsForGroup(CurrentPhaseId);

            // Apply them
            var nextFunc = 0;
            var isUse = false;
            var startPhase = CurrentPhaseId;
            foreach (var func in funcs)
            {
                if (func.SkillId > 0 && func.SkillId == skillId)
                {
                    func.Use(unit, this, skillId, func.NextPhase);
                    if (func.NextPhase != 0) nextFunc = func.NextPhase;
                    if (func.FuncType == "DoodadFuncUse") isUse = true;
                }
                else if (func.SkillId == 0)
                {
                    func.Use(unit, this, skillId);
                    if (func.NextPhase != 0) nextFunc = func.NextPhase;
                    if (func.FuncType == "DoodadFuncUse") isUse = true;
                }
            }

            if (isUse)
                GoToPhaseAndUse(unit, nextFunc, skillId);
            else
                GoToPhase(unit, nextFunc);
        }

        /// <summary>
        /// This executes a doodad's phase. Phase functions start as soon as the doodad switches to a new phase.
        /// </summary>
        public void DoPhase(Unit unit)
        {
            _log.Debug("Doing phase {0}", CurrentPhaseId);
            var phaseFuncs = DoodadManager.Instance.GetPhaseFunc(CurrentPhaseId);

            OverridePhase = 0;
            foreach (var phaseFunc in phaseFuncs)
            {
                phaseFunc.Use(unit, this, 0);
                if (OverridePhase > 0)
                    break;
            }

            if (OverridePhase > 0)
            {
                CurrentPhaseId = OverridePhase;
                DoPhase(unit);
            }
        }

        /// <summary>
        /// Changes the doodad's phase
        /// </summary>
        /// <param name="unit">Unit who triggered the change</param>
        /// <param name="funcGroupId">New phase to go to</param>
        public void GoToPhase(Unit unit, int funcGroupId)
        {
            _log.Debug("Going to phase {0}", funcGroupId);
            if (funcGroupId == -1)
            {
                // Delete doodad
                // Delete();
            }
            else
            {
                CurrentPhaseId = (uint)funcGroupId;
                DoPhase(unit);
                BroadcastPacket(new SCDoodadPhaseChangedPacket(this), true);
            }
        }

        public void GoToPhaseAndUse(Unit unit, int funcGroupId, uint skillId)
        {
            _log.Debug("Going to phase {0}", funcGroupId);
            if (funcGroupId == -1)
            {
                // Delete doodad
                Delete();
            }
            else
            {
                CurrentPhaseId = (uint)funcGroupId;
                DoPhase(unit);
                BroadcastPacket(new SCDoodadPhaseChangedPacket(this), true);
                Use(unit, skillId);
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
            character.SendPacket(new SCDoodadRemovedPacket( ObjId ));
        }

        public PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(ObjId); //The object # in the list
            stream.Write(TemplateId); //The template id needed for that object, the client then uses the template configurations, not the server
            stream.WriteBc(OwnerObjId); //The creator of the object
            stream.WriteBc(ParentObjId); //Things like boats or cars,
            stream.Write(AttachPoint); // attachPoint, relative to the parentObj, (Door or window on a house)
            stream.WritePosition(Position.X, Position.Y, Position.Z); //self explanatory
            stream.Write(Helpers.ConvertRotation(Position.RotationX)); //''
            stream.Write(Helpers.ConvertRotation(Position.RotationY)); //''
            stream.Write(Helpers.ConvertRotation(Position.RotationZ)); //''
            stream.Write(Scale); //The size of the object
            stream.Write(false); // hasLootItem
            stream.Write(CurrentPhaseId); // doodad_func_group_id
            stream.Write(OwnerId); // characterId (Database relative)
            stream.Write(ItemId); // ?? must be ulong though (ItemId seems to be the only ulong)
            stream.Write(0u); //??type1
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
    }
}
