using System;
using System.Collections.Generic;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Faction;
using MySql.Data.MySqlClient;

namespace AAEmu.Game.Models.Game.Expeditions;

public class Expedition : SystemFaction
{
    private List<uint> _removedMembers;

    public List<ExpeditionMember> Members { get; set; }
    public List<ExpeditionRolePolicy> Policies { get; set; }
    public List<ExpeditionRecruitment> Recruitments { get; set; }

    public uint Level { get; set; }
    public bool isDisbanded { get; set; }
    
    public Expedition()
    {
        _removedMembers = new List<uint>();
        Members = new List<ExpeditionMember>();
        Policies = new List<ExpeditionRolePolicy>();
        isDisbanded = false;
    }

    public void RemoveMember(ExpeditionMember member)
    {
        var character = WorldManager.Instance.GetCharacterById(member.CharacterId);
        ChatManager.Instance.GetGuildChat(this).LeaveChannel(character);
        Members.Remove(member);
        _removedMembers.Add(member.CharacterId);
    }

    public void OnCharacterLogin(Character character)
    {
        var member = GetMember(character);
        if (member == null)
            return;

        member.Refresh(character);

        SendPacket(new SCExpeditionMemberStatusChangedPacket(member, 0));
        ChatManager.Instance.GetGuildChat(this).JoinChannel(character);
    }

    public void OnCharacterLogout(Character character)
    {
        var member = GetMember(character);
        if (member != null)
        {
            member.IsOnline = false;
            member.LastWorldLeaveTime = DateTime.UtcNow;

            SendPacket(new SCExpeditionMemberStatusChangedPacket(member, 0));
        }
        ChatManager.Instance.GetGuildChat(this).LeaveChannel(character);
    }

    public ExpeditionRolePolicy GetPolicyByRole(byte role)
    {
        foreach (var policy in Policies)
            if (policy.Role == role)
                return policy;

        return null;
    }

    public ExpeditionMember GetMember(Character character)
    {
        foreach (var member in Members)
            if (member.CharacterId == character.Id)
                return member;
        return null;
    }

    public ExpeditionMember GetMember(uint characterId)
    {
        foreach (var member in Members)
            if (member.CharacterId == characterId)
                return member;
        return null;
    }

    public void SendPacket(GamePacket packet)
    {
        foreach (var member in Members)
            WorldManager.Instance.GetCharacterById(member.CharacterId)?.SendPacket(packet);
    }

    public void Save(MySqlConnection connection, MySqlTransaction transaction)
    {
        if (_removedMembers.Count > 0)
        {
            var removedMembers = string.Join(",", _removedMembers);

            using (var command = connection.CreateCommand())
            {
                command.Connection = connection;
                command.Transaction = transaction;

                command.CommandText = $"DELETE FROM expedition_members WHERE character_id IN ({removedMembers})";
                command.Prepare();
                command.ExecuteNonQuery();
            }

            using (var command = connection.CreateCommand())
            {
                command.Connection = connection;
                command.Transaction = transaction;

                command.CommandText = $"UPDATE characters SET expedition_id = 0 WHERE `characters`.`id` IN ({removedMembers})";
                command.Prepare();
                command.ExecuteNonQuery();
            }

            _removedMembers.Clear();
        }

        if (isDisbanded)
        {
            using (var command = connection.CreateCommand())
            {
                command.Connection = connection;
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM expeditions WHERE `id` = @id";
                command.Parameters.AddWithValue("@id", this.Id);
                command.ExecuteNonQuery();
            }

            using (var command = connection.CreateCommand())
            {
                command.Connection = connection;
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM expedition_role_policies WHERE `expedition_id` = @id";
                command.Parameters.AddWithValue("@id", this.Id);
                command.ExecuteNonQuery();
            }

            using (var command = connection.CreateCommand())
            {
                command.Connection = connection;
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM expedition_members WHERE `expedition_id` = @id";
                command.Parameters.AddWithValue("@id", this.Id);
                command.ExecuteNonQuery();
            }
        }
        else
        {
            using (var command = connection.CreateCommand())
            {
                command.Connection = connection;
                command.Transaction = transaction;

                command.CommandText = "REPLACE INTO expeditions(`id`,`owner`,`owner_name`,`name`,`mother`,`created_at`,`level`) VALUES (@id, @owner, @owner_name, @name, @mother, @created_at, @level)";
                command.Parameters.AddWithValue("@id", this.Id);
                command.Parameters.AddWithValue("@owner", this.OwnerId);
                command.Parameters.AddWithValue("@owner_name", this.OwnerName);
                command.Parameters.AddWithValue("@name", this.Name);
                command.Parameters.AddWithValue("@mother", this.MotherId);
                command.Parameters.AddWithValue("@created_at", this.Created);
                command.Parameters.AddWithValue("@level", this.Level);
                command.ExecuteNonQuery();
            }

            foreach (var member in Members)
                member.Save(connection, transaction);

            foreach (var policy in Policies)
                policy.Save(connection, transaction);

            foreach (var recruitment in Recruitments)
                recruitment.Save(connection, transaction);
        }
    }

    public PacketStream WriteInfo(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(Level);           // type
        stream.Write(0);               // exp
        stream.Write(DateTime.UtcNow + TimeSpan.FromDays(1)); // protectDate
        stream.Write(0);               // warDeposit
        stream.Write(0);               // dailyExp
        stream.Write(DateTime.UtcNow); // lastExpUpdateTime
        stream.Write(false);           // isLevelUpdate
        stream.Write((short)63);       // interest
        stream.Write("ipsus lipsus");  // motdTitle
        stream.Write("mopus dopus");   // motdContent
        stream.Write(0);   // win
        stream.Write(0);   // lose
        stream.Write(0);   // draw
        stream.Write(0);   // type
        stream.Write(0u);  // type
        return stream;
    }
}
