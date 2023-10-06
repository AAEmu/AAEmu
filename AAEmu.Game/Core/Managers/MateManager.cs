﻿using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Mate;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils.DB;

using NLog;

namespace AAEmu.Game.Core.Managers;

public class MateManager : Singleton<MateManager>
{
    private static Logger Logger = LogManager.GetCurrentClassLogger();
    private Regex _nameRegex;

    private Dictionary<uint, NpcMountSkills> _slaveMountSkills;
    private Dictionary<uint, MountSkills> _mountSkills;
    private Dictionary<uint, MountAttachedSkills> _mountAttachedSkills;
    private Dictionary<uint, Mate> _activeMates; // ownerObjId, Mount

    public Mate GetActiveMate(uint ownerObjId)
    {
        return _activeMates.TryGetValue(ownerObjId, out var mate) ? mate : null;
    }

    public Mate GetActiveMateByTlId(uint tlId)
    {
        foreach (var mate in _activeMates.Values)
        {
            if (mate.TlId == tlId) return mate;
        }

        return null;
    }

    public Mate GetActiveMateByMateObjId(uint mateObjId)
    {
        foreach (var mate in _activeMates.Values)
        {
            if (mate.ObjId == mateObjId) return mate;
        }

        return null;
    }

    public Mate GetIsMounted(uint objId, out AttachPointKind attachPoint)
    {
        attachPoint = AttachPointKind.System;
        foreach (var mate in _activeMates.Values)
            foreach (var ati in mate.Passengers)
                if (ati.Value._objId == objId)
                {
                    attachPoint = ati.Key;
                    return mate;
                }

        return null;
    }

    public void ChangeStateMate(GameConnection connection, uint tlId, byte newState)
    {
        var owner = connection.ActiveChar;
        var mateInfo = GetActiveMate(owner.ObjId);
        if (mateInfo?.TlId != tlId) return;

        mateInfo.UserState = newState; // TODO - Maybe verify range
        //owner.BroadcastPacket(new SCMateStatePacket(), );
    }

    public void ChangeTargetMate(GameConnection connection, uint tlId, uint objId)
    {
        var owner = connection.ActiveChar;
        var mateInfo = GetActiveMateByTlId(tlId);
        if (mateInfo == null) return;
        mateInfo.CurrentTarget = objId > 0 ? WorldManager.Instance.GetUnit(objId) : null;
        owner.BroadcastPacket(new SCTargetChangedPacket(mateInfo.ObjId, mateInfo.CurrentTarget?.ObjId ?? 0), true);

        Logger.Debug("ChangeTargetMate. tlId: {0}, objId: {1}, targetObjId: {2}", mateInfo.TlId, mateInfo.ObjId, objId);
    }

    public Mate RenameMount(GameConnection connection, uint tlId, string newName)
    {
        var owner = connection.ActiveChar;
        if (string.IsNullOrWhiteSpace(newName) || newName.Length == 0 || !_nameRegex.IsMatch(newName)) return null;
        var mateInfo = GetActiveMate(owner.ObjId);
        if (mateInfo == null || mateInfo.TlId != tlId) return null;
        mateInfo.Name = newName.FirstCharToUpper();
        owner.BroadcastPacket(new SCUnitNameChangedPacket(mateInfo.ObjId, newName), true);
        return mateInfo;
    }

    public void MountMate(GameConnection connection, uint tlId, AttachPointKind attachPoint, AttachUnitReason reason)
    {
        var character = connection.ActiveChar;
        var mateInfo = GetActiveMateByTlId(tlId);
        if (mateInfo == null) return;

        // Request seat position
        if (mateInfo.Passengers.TryGetValue(attachPoint, out var seatInfo))
        {
            // If first seat, check if it's the owner
            if ((attachPoint == AttachPointKind.Driver) && (mateInfo.OwnerObjId != character.ObjId))
            {
                Logger.Warn("MountMate. Non-owner {0} ({1}) tried to take the first seat on mount {2} ({3})",
                    character.Name, character.ObjId, mateInfo.Name, mateInfo.ObjId);
                return;
            }

            // Check if seat is empty
            if (seatInfo._objId == 0)
            {
                character.BroadcastPacket(new SCUnitAttachedPacket(character.ObjId, attachPoint, reason, mateInfo.ObjId), true);
                seatInfo._objId = character.ObjId;
                seatInfo._reason = reason;

                character.Transform.Local.SetPosition(mateInfo.Transform.Local.Position); // correct the position of the character
                character.Transform.Parent = mateInfo.Transform;
                character.IsRiding = true;
                //character.Transform.StickyParent = mateInfo.Transform;

                character.IsVisible = true; // When we're on a horse, you can see us
            }
        }
        else
        {
            Logger.Warn("MountMate. Player {0} ({1}) tried to take a invalid seat {4} on mount {2} ({3})",
                character.Name, character.ObjId, mateInfo.Name, mateInfo.ObjId, attachPoint);
            return;
        }

        character.Buffs.TriggerRemoveOn(BuffRemoveOn.Mount);
        Logger.Debug("MountMate. mountTlId: {0}, attachPoint: {1}, reason: {2}, seats: {3}",
            mateInfo.TlId, attachPoint, reason, string.Join(", ", mateInfo.Passengers.Values.ToList()));
    }

    public void UnMountMate(Character character, uint tlId, AttachPointKind attachPoint, AttachUnitReason reason)
    {
        var mateInfo = GetActiveMateByTlId(tlId);
        if (mateInfo == null) return;

        mateInfo.StopUpdateXp();

        // Request seat position
        Character targetObj = null;
        if (mateInfo.Passengers.TryGetValue(attachPoint, out var seatInfo))
        {
            // Check if seat is taken by player
            if (seatInfo._objId != 0)
            {
                targetObj = WorldManager.Instance.GetCharacterByObjId(seatInfo._objId);
                seatInfo._objId = 0;
                seatInfo._reason = 0;
            }
        }

        if (targetObj != null)
        {
            //targetObj.Transform.StickyParent = null;
            character.Transform.Parent = null;
            character.IsRiding = false;

            character.BroadcastPacket(new SCUnitDetachedPacket(targetObj.ObjId, reason), true);

            character.Events.OnUnmount(character, new OnUnmountArgs { });

            mateInfo.Buffs.TriggerRemoveOn(BuffRemoveOn.Unmount);
            character.Buffs.TriggerRemoveOn(BuffRemoveOn.Unmount);
            Logger.Debug("UnMountMate. mountTlId: {0}, targetObjId: {1}, attachPoint: {2}, reason: {3}", mateInfo.TlId,
                targetObj.ObjId, attachPoint, reason);
        }
        else
        {
            Logger.Warn("UnMountMate. No valid seat entry, mountTlId: {0}, characterObjId: {1}, attachPoint: {2}, reason: {3}", mateInfo.TlId,
                character?.ObjId ?? 0, attachPoint, reason);
        }
    }

    public void AddActiveMateAndSpawn(Character owner, Mate mate, Item item)
    {
        if (_activeMates.TryGetValue(owner.ObjId, out var activeMate))
        {
            owner.Mates.DespawnMate(activeMate.TlId);
            return;
        }

        _activeMates.Add(owner.ObjId, mate);

        owner.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.UpdateSummonMateItem, new List<ItemTask> { new ItemUpdate(item) },
            new List<ulong>())); // TODO - maybe update details
        owner.SendPacket(new SCMateSpawnedPacket(mate));
        mate.Spawn();

        Logger.Debug("Mount spawned. ownerObjId: {0}, tlId: {1}, mateObjId: {2}", owner.ObjId, mate.TlId, mate.ObjId);
    }

    public void RemoveActiveMateAndDespawn(Character owner, uint tlId)
    {
        if (!_activeMates.TryGetValue(owner.ObjId, out var mateInfo)) return;
        if (mateInfo.TlId != tlId) return; // skip if invalid tlId

        foreach (var ati in mateInfo.Passengers)
            UnMountMate(WorldManager.Instance.GetCharacterByObjId(ati.Value._objId), mateInfo.TlId, ati.Key, AttachUnitReason.SlaveBinding);

        _activeMates[owner.ObjId].Delete();
        _activeMates.Remove(owner.ObjId);
        ObjectIdManager.Instance.ReleaseId(mateInfo.ObjId);
        TlIdManager.Instance.ReleaseId(mateInfo.TlId);

        Logger.Debug("Mount removed. ownerObjId: {0}, tlId: {1}, mateObjId: {2}", owner.ObjId, mateInfo.TlId, mateInfo.ObjId);
    }

    /// <summary>
    /// Remove all mounts that are in the world and owned by character
    /// </summary>
    /// <param name="character"></param>
    public void RemoveAndDespawnAllActiveOwnedMates(Character character)
    {
        if (character == null) return;
        foreach (var mate in _activeMates)
            if (mate.Value.OwnerObjId == character.ObjId)
                RemoveActiveMateAndDespawn(character, mate.Value.TlId);
    }

    public List<uint> GetMateSkills(uint id)
    {
        var template = new List<uint>();

        foreach (var value in _slaveMountSkills.Values)
            if (value.NpcId == id && !template.Contains(value.MountSkillId))
                template.Add(value.MountSkillId);

        return template;
    }

    public uint GetMountAttachedSkills(uint mountSkill)
    {
        var id = 0u;
        var skill = 0u;

        foreach (var ms in _mountSkills)
        {
            if (ms.Value.SkillId != mountSkill) { continue; }
            id = ms.Key;
            break;
        }
        foreach (var mas in _mountAttachedSkills)
        {
            if (mas.Value.MountSkillId != id) { continue; }
            skill = mas.Value.SkillId;
            break;
        }

        return skill;
    }

    public void Load()
    {
        _nameRegex = new Regex(AppConfiguration.Instance.CharacterNameRegex, RegexOptions.Compiled);
        _slaveMountSkills = new Dictionary<uint, NpcMountSkills>();
        _mountSkills = new Dictionary<uint, MountSkills>();
        _mountAttachedSkills = new Dictionary<uint, MountAttachedSkills>();
        _activeMates = new Dictionary<uint, Mate>();

        #region SQLite

        using (var connection = SQLite.CreateConnection())
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM npc_mount_skills";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new NpcMountSkills()
                        {
                            Id = reader.GetUInt32("id"),
                            NpcId = reader.GetUInt32("npc_id"),
                            MountSkillId = reader.GetUInt32("mount_skill_id")
                        };
                        _slaveMountSkills.Add(template.Id, template);
                    }
                }
            }
        }

        using (var connection = SQLite.CreateConnection())
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM mount_skills";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new MountSkills();
                        template.Id = reader.GetUInt32("id");
                        template.Name = reader.GetString("name", "");
                        template.SkillId = reader.GetUInt32("skill_id");
                        _mountSkills.Add(template.Id, template);
                    }
                }
            }
        }

        using (var connection = SQLite.CreateConnection())
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM mount_attached_skills";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new MountAttachedSkills();
                        template.Id = reader.GetUInt32("id");
                        template.MountSkillId = reader.GetUInt32("mount_skill_id");
                        template.AttachPointId = (AttachPointKind)reader.GetUInt32("attach_point_id");
                        template.SkillId = reader.GetUInt32("skill_id");
                        _mountAttachedSkills.Add(template.Id, template);
                    }
                }
            }
        }

        #endregion
    }
}
