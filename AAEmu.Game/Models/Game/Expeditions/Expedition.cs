using System;
using System.Collections.Generic;
using System.Linq;

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
    //public List<ExpeditionRecruitment> Recruitments { get; set; }
    //public List<Applicant> Pretenders { get; set; }

    public uint Level { get; set; }
    public uint Exp { get; set; }
    public DateTime ProtectTime { get; set; }
    public uint WarDeposit { get; set; }
    public uint DailyExp { get; set; }
    public DateTime LastExpUpdateTime { get; set; }
    public bool IsLevelUpdate { get; set; }
    public short Interest { get; set; }
    public string MotdTitle { get; set; }
    public string MotdContent { get; set; }
    public uint Win { get; set; }
    public uint Lose { get; set; }
    public uint Draw { get; set; }
    public bool IsDisbanded { get; set; }

    public Expedition()
    {
        _removedMembers = new List<uint>();
        Members = new List<ExpeditionMember>();
        Policies = new List<ExpeditionRolePolicy>();
        //Recruitments = new List<ExpeditionRecruitment>();
        //Pretenders = new List<Applicant>();
        IsDisbanded = false;
        IsLevelUpdate = false;
        MotdTitle = "";
        MotdContent = "";
    }

    /// <summary>
    /// Удалить члена гильдии
    /// </summary>
    /// <param name="member">член гильдии</param>
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

    /// <summary>
    /// Получить члена гильдии
    /// </summary>
    /// <param name="character">искомый член гильдии</param>
    /// <returns></returns>
    public ExpeditionMember GetMember(Character character)
    {
        foreach (var member in Members)
            if (member.CharacterId == character.Id)
                return member;
        return null;
    }

    /// <summary>
    /// Получить члена гильдии
    /// </summary>
    /// <param name="characterId">Id искомого члена гильдии</param>
    /// <returns></returns>
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

        if (IsDisbanded)
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

            //using (var command = connection.CreateCommand())
            //{
            //    command.Connection = connection;
            //    command.Transaction = transaction;
            //    command.CommandText = "DELETE FROM expedition_recruitments WHERE `expedition_id` = @id";
            //    command.Parameters.AddWithValue("@id", this.Id);
            //    command.ExecuteNonQuery();
            //}

            //using (var command = connection.CreateCommand())
            //{
            //    command.Connection = connection;
            //    command.Transaction = transaction;
            //    command.CommandText = "DELETE FROM expedition_applicants WHERE `expedition_id` = @id";
            //    command.Parameters.AddWithValue("@id", this.Id);
            //    command.ExecuteNonQuery();
            //}
        }
        else
        {
            using (var command = connection.CreateCommand())
            {
                command.Connection = connection;
                command.Transaction = transaction;

                command.CommandText = "REPLACE INTO expeditions(`id`,`owner`,`owner_name`,`name`,`mother`,`created_at`,`level`,`exp`,`protect_time`,`war_deposit`,`daily_exp`,`last_exp_update_time`,`is_level_update`,`interest`,`motd_title`,`motd_content`,`win`,`lose`,`draw`)" +
                                      " VALUES (@id, @owner, @owner_name, @name, @mother, @created_at, @level, @exp, @protect_time, @war_deposit, @daily_exp, @last_exp_update_time, @is_level_update, @interest, @motd_title, @motd_content, @win, @lose, @draw)";
                command.Parameters.AddWithValue("@id", this.Id);
                command.Parameters.AddWithValue("@owner", this.OwnerId);
                command.Parameters.AddWithValue("@owner_name", this.OwnerName);
                command.Parameters.AddWithValue("@name", this.Name);
                command.Parameters.AddWithValue("@mother", this.MotherId);
                command.Parameters.AddWithValue("@created_at", this.Created);
                command.Parameters.AddWithValue("@level", this.Level);
                command.Parameters.AddWithValue("@exp", this.Exp);
                command.Parameters.AddWithValue("@protect_time", this.ProtectTime);
                command.Parameters.AddWithValue("@war_deposit", this.WarDeposit);
                command.Parameters.AddWithValue("@daily_exp", this.DailyExp);
                command.Parameters.AddWithValue("@last_exp_update_time", this.LastExpUpdateTime);
                command.Parameters.AddWithValue("@is_level_update", this.IsLevelUpdate);
                command.Parameters.AddWithValue("@interest", this.Interest);
                command.Parameters.AddWithValue("@motd_title", this.MotdTitle);
                command.Parameters.AddWithValue("@motd_content", this.MotdContent);
                command.Parameters.AddWithValue("@win", this.Win);
                command.Parameters.AddWithValue("@lose", this.Lose);
                command.Parameters.AddWithValue("@draw", this.Draw);
                command.ExecuteNonQuery();
            }

            foreach (var member in Members)
                member.Save(connection, transaction);

            foreach (var policy in Policies)
                policy.Save(connection, transaction);
        }
    }

    /// <summary>
    /// Шлем члену гильдии информацию о гильдии
    /// </summary>
    /// <param name="stream"></param>
    /// <returns></returns>
    public PacketStream WriteInfo(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(Level);             // type
        stream.Write(Exp);               // exp
        stream.Write(ProtectTime);       // protectDate
        stream.Write(WarDeposit);        // warDeposit
        stream.Write(DailyExp);          // dailyExp
        stream.Write(LastExpUpdateTime); // lastExpUpdateTime
        stream.Write(IsLevelUpdate);     // isLevelUpdate
        stream.Write(Interest);          // interest
        stream.Write(MotdTitle);         // motdTitle
        stream.Write(MotdContent);       // motdContent
        stream.Write(Win);               // win
        stream.Write(Lose);              // lose
        stream.Write(Draw);              // draw
        stream.Write(0);                 // type
        stream.Write(0u);                // type
        return stream;
    }

    public void OnCharacterRefresh(Character character)
    {
        var member = GetMember(character);
        if (member == null)
            return;
        member.Refresh(character);
        SendPacket(new SCExpeditionMemberStatusChangedPacket(member, 0));
    }
}
