using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;

using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Expeditions;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Team;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World.Transform;
using MySql.Data.MySqlClient;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Core.Managers;

public class ExpeditionManager : Singleton<ExpeditionManager>
{
    private Regex _nameRegex;

    private List<uint> _removedMembers;
    private Dictionary<FactionsEnum, Expedition> _expeditions;
    public List<ExpeditionRecruitment> _recruitments { get; set; }
    public List<Applicant> _pretenders { get; set; }
    public IEnumerable<Expedition> Expeditions { get => _expeditions.Values; }

    public static Expedition Create(string name, Character owner)
    {
        var expedition = new Expedition();
        expedition.Id = (FactionsEnum)ExpeditionIdManager.Instance.GetNextId();
        expedition.Level = 1;
        expedition.MotherId = owner.Faction.Id;
        expedition.Name = name;
        expedition.OwnerId = owner.Id;
        expedition.OwnerName = owner.Name;
        expedition.UnitOwnerType = 0;
        expedition.PoliticalSystem = 1;
        expedition.Created = DateTime.UtcNow;
        expedition.AggroLink = false;
        expedition.DiplomacyTarget = false;
        expedition.Members = new List<ExpeditionMember>();
        expedition.Policies = GetDefaultPolicies(expedition.Id);
        //expedition.Recruitments = new List<ExpeditionRecruitment>();

        var member = GetMemberFromCharacter(expedition, owner, true);
        member.LastWorldLeaveTime = DateTime.UtcNow;

        expedition.Members.Add(member);

        return expedition;
    }

    public void Load()
    {
        _removedMembers = new List<uint>();
        _recruitments = new List<ExpeditionRecruitment>();
        _pretenders = new List<Applicant>();
        _expeditions = new();
        _nameRegex = new Regex(AppConfiguration.Instance.Expedition.NameRegex, RegexOptions.Compiled);

        using (var connection = MySQL.CreateConnection())
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM expeditions";
                command.Prepare();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var expedition = new Expedition();
                        expedition.Id = (FactionsEnum)reader.GetUInt32("id");
                        expedition.MotherId = (FactionsEnum)reader.GetUInt32("mother");
                        expedition.Name = reader.GetString("name");
                        expedition.OwnerId = reader.GetUInt32("owner");
                        expedition.OwnerName = reader.GetString("owner_name");
                        expedition.UnitOwnerType = 0;
                        expedition.PoliticalSystem = 1;
                        expedition.Created = reader.GetDateTime("created_at");
                        expedition.AggroLink = false;
                        expedition.DiplomacyTarget = false;
                        expedition.Level = reader.GetUInt32("level");
                        expedition.Exp = reader.GetUInt32("exp");
                        expedition.ProtectTime = reader.GetDateTime("protect_time");
                        expedition.WarDeposit = reader.GetUInt32("war_deposit");
                        expedition.DailyExp = reader.GetUInt32("daily_exp");
                        expedition.LastExpUpdateTime = reader.GetDateTime("last_exp_update_time");
                        expedition.IsLevelUpdate = reader.GetBoolean("is_level_update");
                        expedition.Interest = reader.GetInt16("interest");
                        expedition.MotdTitle = reader.GetString("motd_title");
                        expedition.MotdContent = reader.GetString("motd_content");
                        expedition.Win = reader.GetUInt32("win");
                        expedition.Lose = reader.GetUInt32("lose");
                        expedition.Draw = reader.GetUInt32("draw");

                        expedition.Members = new List<ExpeditionMember>();
                        expedition.Policies = new List<ExpeditionRolePolicy>();
                        //expedition.Recruitments = new List<ExpeditionRecruitment>();

                        _expeditions.Add(expedition.Id, expedition);
                    }
                }
            }

            foreach (var expedition in _expeditions.Values)
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM expedition_members WHERE expedition_id = @expedition_id";
                    command.Parameters.AddWithValue("@expedition_id", expedition.Id);
                    command.Prepare();
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var member = new ExpeditionMember();
                            member.CharacterId = reader.GetUInt32("character_id");
                            member.ExpeditionId = (FactionsEnum)reader.GetUInt32("expedition_id");
                            member.Role = reader.GetByte("role");
                            member.Memo = reader.GetString("memo");
                            member.LastWorldLeaveTime = reader.GetDateTime("last_leave_time");
                            member.Name = reader.GetString("name");
                            member.Level = reader.GetByte("level");
                            member.Abilities = [reader.GetByte("ability1"), reader.GetByte("ability2"), reader.GetByte("ability3")];
                            member.IsOnline = false;
                            member.InParty = false;
                            expedition.Members.Add(member);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM expedition_role_policies WHERE expedition_id = @expedition_id";
                    command.Parameters.AddWithValue("@expedition_id", expedition.Id);
                    command.Prepare();
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var policy = new ExpeditionRolePolicy();
                            policy.ExpeditionId = (FactionsEnum)reader.GetUInt32("expedition_id");
                            policy.Role = reader.GetByte("role");
                            policy.Name = reader.GetString("name");
                            policy.DominionDeclare = reader.GetBoolean("dominion_declare");
                            policy.Invite = reader.GetBoolean("invite");
                            policy.Expel = reader.GetBoolean("expel");
                            policy.Promote = reader.GetBoolean("promote");
                            policy.Dismiss = reader.GetBoolean("dismiss");
                            policy.Chat = reader.GetBoolean("chat");
                            policy.ManagerChat = reader.GetBoolean("manager_chat");
                            policy.SiegeMaster = reader.GetBoolean("siege_master");
                            policy.JoinSiege = reader.GetBoolean("join_siege");
                            expedition.Policies.Add(policy);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM expedition_recruitments WHERE expedition_id = @expedition_id";
                    command.Parameters.AddWithValue("@expedition_id", expedition.Id);
                    command.Prepare();
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var recruitment = new ExpeditionRecruitment();
                            recruitment.ExpeditionId = (FactionsEnum)reader.GetUInt32("expedition_id");
                            recruitment.Name = reader.GetString("name");
                            recruitment.Level = reader.GetUInt32("level");
                            recruitment.OwnerName = reader.GetString("owner_name");
                            recruitment.Introduce = reader.GetString("introduce");
                            recruitment.RegTime = reader.GetDateTime("reg_time");
                            recruitment.EndTime = reader.GetDateTime("end_time");
                            recruitment.Interest = reader.GetUInt16("interest");
                            recruitment.MemberCount = reader.GetInt32("member_count");
                            recruitment.Apply = reader.GetBoolean("apply");
                            _recruitments.Add(recruitment);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM expedition_applicants WHERE expedition_id = @expedition_id";
                    command.Parameters.AddWithValue("@expedition_id", expedition.Id);
                    command.Prepare();
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var pretender = new Applicant();
                            pretender.ExpeditionId = (FactionsEnum)reader.GetUInt32("expedition_id");
                            pretender.CharacterId = reader.GetUInt32("character_id");
                            pretender.CharacterName = reader.GetString("character_name");
                            pretender.CharacterLevel = reader.GetByte("character_level");
                            pretender.Memo = reader.GetString("memo");
                            pretender.RegTime = reader.GetDateTime("reg_time");
                            _pretenders.Add(pretender);
                        }
                    }
                }
            }
        }
    }

    public static List<ExpeditionRolePolicy> GetDefaultPolicies(FactionsEnum expeditionId)
    {
        var res = new List<ExpeditionRolePolicy>();
        foreach (var rolePolicy in AppConfiguration.Instance.Expedition.RolePolicies)
        {
            var policy = rolePolicy.Clone();
            policy.ExpeditionId = expeditionId;
            res.Add(policy);
        }

        return res;
    }

    public Expedition GetExpedition(FactionsEnum id)
    {
        if (_expeditions.TryGetValue(id, out var expedition))
            return expedition;
        return null;
    }

    public List<Expedition> GetExpeditions()
    {
        return _expeditions.Values.ToList();
    }

    public void CreateExpedition(string name, GameConnection connection)
    {
        var owner = connection.ActiveChar;
        if (owner.Expedition != null)
        {
            connection.ActiveChar.SendErrorMessage(ErrorMessageType.ExpeditionAlreadyMember);
            return;
        }

        if (name.Length > 32)
        {
            connection.ActiveChar.SendErrorMessage(ErrorMessageType.ExpeditionNameLength);
            return;
        }

        if (!_nameRegex.IsMatch(name))
        {
            connection.ActiveChar.SendErrorMessage(ErrorMessageType.ExpeditionNameCharacter);
            return;
        }

        foreach (var exp in _expeditions.Values)
        {
            if (name.Equals(exp.Name))
            {
                connection.ActiveChar.SendErrorMessage(ErrorMessageType.ExpeditionNameExist);
                return;
            }
        }

        // ----------------- Conditions, can change this...
        var team = TeamManager.Instance.GetActiveTeamByUnit(owner.Id);
        if (team == null)// || !team.IsParty)
        {
            // We send the same error on number of party members when we don't have a party
            connection.ActiveChar.SendErrorMessage(ErrorMessageType.ExpeditionCreateMember);
            return;
        }

        // Check the number of members in the party that meet the requirements
        var validMembers = new List<TeamMember>();
        var teamMembers = new List<TeamMember>();
        teamMembers.AddRange(team.Members.ToList());

        foreach (var m in teamMembers)
        {
            if (m?.Character == null)
                continue;

            if (m.Character.Level < AppConfiguration.Instance.Expedition.Create.Level)
            {
                connection.ActiveChar.SendErrorMessage(ErrorMessageType.ExpeditionCreateLevel);
                return;
            }
            if (m.Character.Expedition != null)
            {
                connection.ActiveChar.SendErrorMessage(ErrorMessageType.ExpeditionCreateMemberExpedition);
                return;
            }
            if (m.Character.Faction.MotherId != owner.Faction.MotherId)
            {
                connection.ActiveChar.SendErrorMessage(ErrorMessageType.ExpeditionCreateFaction);
                return;
            }
            validMembers.Add(m);
        }

        if (validMembers.Count < AppConfiguration.Instance.Expedition.Create.PartyMemberCount)
        {
            connection.ActiveChar.SendErrorMessage(ErrorMessageType.ExpeditionCreateMember);
            return;
        }

        if (owner.Money < AppConfiguration.Instance.Expedition.Create.Cost)
        {
            connection.ActiveChar.SendErrorMessage(ErrorMessageType.ExpeditionCreateMoney);
            return;
        }

        owner.Money -= AppConfiguration.Instance.Expedition.Create.Cost;
        owner.SendPacket(
            new SCItemTaskSuccessPacket(
                ItemTaskType.ExpeditionCreation,
                new List<ItemTask>
                {
                    new MoneyChange(-AppConfiguration.Instance.Expedition.Create.Cost)
                },
                new List<ulong>())
        );
        // -----------------

        var expedition = Create(name, owner);
        _expeditions.Add(expedition.Id, expedition);

        owner.Expedition = expedition;
        owner.Expedition.ProtectTime = DateTime.UtcNow + TimeSpan.FromDays(30); // TODO вставить правильное количество дней

        var membersList = new List<(uint memberObjId, uint memberId, string name)>(validMembers.Count);
        foreach (var tm in validMembers)
        {
            if (tm.Character.Id == owner.Id) { continue; }
            membersList.Add((tm.Character.ObjId, tm.Character.Id, tm.Character.Name));
        }
        var members = membersList.ToArray();

        //owner.SendPacket(new SCFactionCreatedPacket(expedition, owner.ObjId, new[] { (owner.ObjId, owner.Id, owner.Name) }));
        owner.SendPacket(new SCExpeditionCreatedPacket(expedition, owner.ObjId, members));
        WorldManager.Instance.BroadcastPacketToServer(new SCSystemFactionListPacket(expedition));
        owner.BroadcastPacket(new SCUnitExpeditionChangedPacket(owner.ObjId, owner.Id, "", owner.Name, 0, expedition.Id, false), true);

        foreach (var m in validMembers)
        {
            if (m.Character.Id == owner.Id)
            {
                owner.BroadcastPacket(new SCUnitExpeditionChangedPacket(owner.ObjId, owner.Id, "", owner.Name, 0, expedition.Id, false), true);
                SendMyExpeditionInfo(owner);
                ChatManager.Instance.GetGuildChat(expedition).JoinChannel(owner);
                continue;
            }

            var invited = m.Character;
            var newMember = GetMemberFromCharacter(expedition, invited, false);

            invited.Expedition = expedition;
            expedition.Members.Add(newMember);

            invited.BroadcastPacket(new SCUnitExpeditionChangedPacket(invited.ObjId, invited.Id, "", invited.Name, 0, expedition.Id, false), true);
            SendMyExpeditionInfo(invited);
            expedition.OnCharacterLogin(invited);
        }

        SendMyExpeditionDescInfo(owner);

        // закомментируйте, это для проверки работы "Набор игроков"
        AddRecruitment(owner, 63, 3, "Welcome!");

        Save(expedition);
    }

    /// <summary>
    /// Пригласить в гильдию по имени
    /// </summary>
    /// <param name="connection">Это тот. Кто приглашает.</param>
    /// <param name="invitedName">Имя приглашенного</param>
    public static void Invite(GameConnection connection, string invitedName)
    {
        var inviter = connection.ActiveChar;

        var inviterMember = inviter.Expedition?.GetMember(inviter);
        if (inviterMember == null || !inviter.Expedition.GetPolicyByRole(inviterMember.Role).Invite)
            return;

        var invited = WorldManager.Instance.GetCharacter(invitedName);
        if (invited == null) return;
        if (invited.Expedition != null) return;

        invited.SendPacket(new SCExpeditionInvitationPacket(inviter.Id, inviter.Name, inviter.Expedition.Id, inviter.Expedition.Name));
    }

    /// <summary>
    /// Согласие или отказ на приглашение в гильдию
    /// </summary>
    /// <param name="connection"></param>
    /// <param name="expeditionId">Id гильдии</param>
    /// <param name="id2"></param>
    /// <param name="reply">Согласие или отказ</param>
    public void ReplyInvite(GameConnection connection, FactionsEnum expeditionId, FactionsEnum id2, bool reply)
    {
        var invited = connection.ActiveChar;
        if (!reply)
            return;

        var expedition = _expeditions[expeditionId];
        var newMember = GetMemberFromCharacter(expedition, invited, false);

        invited.Expedition = expedition;
        expedition.Members.Add(newMember);

        invited.BroadcastPacket(new SCUnitExpeditionChangedPacket(invited.ObjId, invited.Id, "", invited.Name, 0, expedition.Id, false), true);
        SendMyExpeditionInfo(invited);
        expedition.OnCharacterLogin(invited);
        Save(expedition);
        // invited.Save(); // Moved to SaveMananger
    }

    public void ChangeExpeditionRolePolicy(GameConnection connection, ExpeditionRolePolicy policy)
    {
        var expedition = _expeditions[policy.ExpeditionId];

        var characterMember = expedition.GetMember(connection.ActiveChar);
        if (characterMember == null) return;

        if (!expedition.GetPolicyByRole(characterMember.Role).Expel) return;

        var currentPolicy = expedition.GetPolicyByRole(policy.Role);
        currentPolicy.Name = policy.Name;
        currentPolicy.Invite = policy.Invite;
        currentPolicy.JoinSiege = policy.JoinSiege;
        currentPolicy.Promote = policy.Promote;
        currentPolicy.Expel = policy.Expel;

        expedition.SendPacket(new SCExpeditionRolePolicyChangedPacket(policy, true));
        Save(expedition);
    }

    /// <summary>
    /// Removes a character from their Guild
    /// </summary>
    /// <param name="character"></param>
    public static void Leave(Character character)
    {
        var expedition = character.Expedition;
        if (expedition == null) return;

        expedition.RemoveMember(expedition.GetMember(character));
        var changedPacket = new SCUnitExpeditionChangedPacket(character.ObjId, character.Id, "", character.Name, expedition.Id, 0, false);
        character.Expedition = null;
        character.BroadcastPacket(changedPacket, true);
        expedition.SendPacket(changedPacket);

        // TODO Добавить штраф на вступление в гильдии. Вынести настройку штрафа в конфиг.
    }

    public static void Kick(GameConnection connection, uint kickedId)
    {
        var character = connection.ActiveChar;
        var expedition = character.Expedition;

        var characterMember = expedition?.GetMember(character);
        if (characterMember == null || !expedition.GetPolicyByRole(characterMember.Role).Expel)
            return;

        var kicked = expedition.GetMember(kickedId);
        if (kicked == null)
            return;

        expedition.RemoveMember(kicked);

        var kickedChar = WorldManager.Instance.GetCharacterById(kickedId);

        var changedPacket = new SCUnitExpeditionChangedPacket(kickedChar?.ObjId ?? 0, kicked.CharacterId, character.Name, kicked.Name, expedition.Id, FactionsEnum.Invalid, true);

        if (kickedChar is not null)
        {
            kickedChar.Expedition = null;
            kickedChar.BroadcastPacket(changedPacket, true);
        }
        expedition.SendPacket(changedPacket);
        Save(expedition);
    }

    public static void ChangeMemberRole(GameConnection connection, byte newRole, uint changedId)
    {
        var character = connection.ActiveChar;
        var expedition = character.Expedition;

        var changerMember = expedition?.GetMember(character);
        if (changerMember == null ||
            changerMember.Role <= newRole ||
            !expedition.GetPolicyByRole(changerMember.Role).Promote)
            return;

        var changedMember = expedition.GetMember(changedId);
        if (changedMember == null)
            return;

        changedMember.Role = newRole;
        expedition.SendPacket(new SCExpeditionMemberRoleChangedPacket(changedMember.CharacterId, changedMember.Role, changedMember.Name));
        Save(expedition);
    }

    public static void ChangeOwner(GameConnection connection, uint newOwnerId)
    {
        var owner = connection.ActiveChar;
        var expedition = owner.Expedition;

        var ownerMember = expedition?.GetMember(owner);
        if (ownerMember == null || ownerMember.Role != 255)
            return;

        var newOwnerMember = expedition.GetMember(newOwnerId);
        if (newOwnerMember == null) return;

        newOwnerMember.Role = 255;
        ownerMember.Role = 0;

        expedition.OwnerId = newOwnerId;

        expedition.SendPacket(new SCExpeditionOwnerChangedPacket(ownerMember.CharacterId, newOwnerMember.CharacterId, newOwnerMember.Name));
        expedition.SendPacket(new SCExpeditionMemberRoleChangedPacket(ownerMember.CharacterId, ownerMember.Role, ownerMember.Name));
        expedition.SendPacket(new SCExpeditionMemberRoleChangedPacket(newOwnerMember.CharacterId, newOwnerMember.Role, newOwnerMember.Name));
        Save(expedition);
    }

    public static bool Disband(Character owner)
    {
        var guild = owner.Expedition;
        if (guild == null)
        {
            // Error, not in a guild
            owner.SendErrorMessage(ErrorMessageType.OnlyExpeditionMember);
            return false;
        }
        if (guild.OwnerId != owner.Id)
        {
            // Error, only guild owner can disband
            owner.SendErrorMessage(ErrorMessageType.OnlyExpeditionOwner);
            return false;
        }
        for (var i = guild.Members.Count - 1; i >= 0; i--)
        {
            var c = WorldManager.Instance.GetCharacterById(guild.Members[i].CharacterId);
            if (c != null)
            {
                if (c.IsOnline)
                    c.SendPacket(new SCExpeditionDismissedPacket((uint)guild.Id, true));
                c.Expedition = null;
            }
            guild.RemoveMember(guild.Members[i]);
        }
        guild.Name = "$deleted-guild-" + guild.Id;
        guild.OwnerId = 0;
        guild.IsDisbanded = true;

        Instance.RemoveRecruitments(guild);
        Instance.RemovePretenders(guild);

        // TODO Добавить штраф на создание гильдии. Вынести настройку штрафа в конфиг.
        Save(guild);

        return true;
    }

    public static void SendMyExpeditionInfo(Character character)
    {
        if (character.Expedition == null)
        {
            character.SendPacket(new SCExpeditionRolePolicyListPacket([]));
            character.SendPacket(new SCExpeditionMemberListPacket(0, FactionsEnum.Invalid, []));
            character.SendPacket(new SCExpeditionMemberListEndPacket(0, FactionsEnum.Invalid));
        }
        else
        {
            var total = (uint)character.Expedition.Members.Count;
            character.SendPacket(new SCExpeditionRolePolicyListPacket(character.Expedition.Policies));
            var dividedLists = Helpers.SplitList(character.Expedition.Members, 20); // Разделяем список на списки по 20 записей
            foreach (var members in dividedLists)
            {
                character.SendPacket(new SCExpeditionMemberListPacket(total, character.Expedition.Id, members));
            }
            character.SendPacket(new SCExpeditionMemberListEndPacket(total, character.Expedition.Id));

            character.SendPacket(new SCExpeditionDescReceivedPacket(character.Expedition));
        }
    }

    public static void SendMyExpeditionDescInfo(Character character)
    {
        if (character.Expedition != null)
        {
            character.SendPacket(new SCExpeditionDescReceivedPacket(character.Expedition));
        }
    }

    private static void Save(Expedition expedition)
    {
        using var connection = MySQL.CreateConnection();
        using var transaction = connection.BeginTransaction();

        Instance.Save(connection, transaction);

        foreach (var recruitment in Instance._recruitments)
            recruitment.Save(connection, transaction);

        foreach (var pretender in Instance._pretenders)
            pretender.Save(connection, transaction);

        expedition.Save(connection, transaction);
        transaction.Commit();
    }

    private void Save(MySqlConnection connection, MySqlTransaction transaction)
    {
        if (_removedMembers.Count <= 0) { return; }
        var removedMembers = string.Join(",", _removedMembers);

        using (var command = connection.CreateCommand())
        {
            command.Connection = connection;
            command.Transaction = transaction;

            command.CommandText = $"DELETE FROM expedition_recruitments WHERE expedition_id IN ({removedMembers})";
            command.Prepare();
            command.ExecuteNonQuery();
        }

        using (var command = connection.CreateCommand())
        {
            command.Connection = connection;
            command.Transaction = transaction;

            command.CommandText = $"DELETE FROM expedition_applicants WHERE expedition_id IN ({removedMembers})";
            command.Prepare();
            command.ExecuteNonQuery();
        }

        _removedMembers.Clear();
    }

    private static ExpeditionMember GetMemberFromCharacter(Expedition expedition, Character character, bool owner)
    {
        var member = new ExpeditionMember();
        member.IsOnline = true;
        member.InParty = true;
        member.Name = character.Name;
        member.Level = character.Level;
        member.Role = (byte)(owner ? 255 : 0);
        member.Memo = "";
        member.Position = new Vector3(character.Transform.World.Position.X, character.Transform.World.Position.Y, character.Transform.World.Position.Z);
        member.ZoneId = character.Transform.ZoneId;
        member.Abilities = [(byte)character.Ability1, (byte)character.Ability2, (byte)character.Ability3];
        member.ExpeditionId = expedition.Id;
        member.CharacterId = character.Id;

        return member;
    }

    public static ExpeditionMember GetMemberFromCharacter(uint characterId)
    {
        var character = WorldManager.Instance.GetCharacterById(characterId);
        var expedition = character.Expedition;
        var member = new ExpeditionMember();
        member.IsOnline = true;
        member.Name = character.Name;
        member.Level = character.Level;
        member.Role = (byte)(expedition.OwnerId == character.Id ? 255 : 0);
        member.Memo = "";
        member.Position = new Vector3(character.Transform.World.Position.X, character.Transform.World.Position.Y, character.Transform.World.Position.Z);
        member.ZoneId = character.Transform.ZoneId;
        member.Abilities = [(byte)character.Ability1, (byte)character.Ability2, (byte)character.Ability3];
        member.ExpeditionId = expedition.Id;
        member.CharacterId = character.Id;
        member.LastWorldLeaveTime = DateTime.UtcNow;

        return member;
    }

    public void SendExpeditions(Character character)
    {
        if (_expeditions.Values.Count > 0)
        {
            var expeditions = _expeditions.Values.ToArray();
            var dividedArrays = Helpers.SplitArray(expeditions, 20); // Разделяем массив на массивы по 20 значений
            foreach (SystemFaction[] expedition in dividedArrays)
                character.SendPacket(new SCSystemFactionListPacket(expedition));
        }
    }

    public void SendExpeditionProtect(GameConnection connection)
    {
        if (connection.ActiveChar is { Expedition: not null })
        {
            connection.ActiveChar.SendPacket(new SCProtectFactionPacket(1, connection.ActiveChar.Expedition.ProtectTime));
        }
        else
        {
            connection.SendPacket(new SCProtectFactionPacket(1, DateTime.MinValue));
        }
    }

    public FactionsEnum GetExpeditionOfCharacter(uint characterId)
    {
        return (from guild in _expeditions.Values from member in guild.Members where member.CharacterId == characterId select guild.Id).FirstOrDefault();
    }

    public void SendMyExpeditionRecruitmentsInfo(Character character, bool my, short page, ushort interest, uint levelFrom, uint levelTo, string name, byte sortType)
    {
        var expeditions = GetExpeditions();

        var expeditionRecruitments = new List<ExpeditionRecruitment>();
        var total = expeditions.Count;

        if (expeditions is { Count: not 0 })
        {
            //foreach (var expedition in expeditions)
            {
                foreach (var er in _recruitments)
                {
                    if (my)
                    {
                        if (er.OwnerName != character.Name) { continue; }

                        expeditionRecruitments.Add(er);
                    }
                    else if (er.Interest == interest)
                    {
                        if (er.Level < levelFrom || er.Level > levelTo) { continue; }

                        if (er.Name != string.Empty && er.Name == name)
                        {
                            expeditionRecruitments.Add(er);
                        }
                        else
                        {
                            expeditionRecruitments.Add(er);
                        }
                    }
                }
            }
        }

        var dividedLists = Helpers.SplitList(expeditionRecruitments, 15); // Разделяем список на списки по 15 записей
        foreach (var recruitments in dividedLists)
        {
            var count = recruitments.Count;
            // Дополнение списка пустыми элементами до 15, если требуется
            var emptySlots = 15 - recruitments.Count;
            if (emptySlots <= 0) { return; }
            for (var i = 0; i < emptySlots; i++)
            {
                var newRecruitment = new ExpeditionRecruitment
                {
                    ExpeditionId = 0,
                    Name = "",
                    Level = 0,
                    OwnerName = "",
                    Interest = 0,
                    Introduce = "",
                    MemberCount = 0,
                    Apply = false,
                    RegTime = DateTime.MinValue,
                    EndTime = DateTime.MinValue
                };
                recruitments.Add(newRecruitment);
            }
            character.SendPacket(new SCExpeditionRecruitmentsGetPacket((uint)total, (uint)page, (uint)count, recruitments));
        }
    }

    public void SendMyExpeditionApplicantsInfo(Character character)
    {
        var expedition = GetExpedition(GetExpeditionOfCharacter(character.Id));
        var expeditionPretenders = GetPretenders(expedition.Id);
        if (expeditionPretenders is not { Count: not 0 }) { return; }
        var total = expeditionPretenders.Count;
        var dividedLists = Helpers.SplitList(expeditionPretenders, 50); // Разделяем список на списки по 50 записей
        foreach (var pretenders in dividedLists)
        {
            var count = pretenders.Count;
            var emptySlots = 50 - pretenders.Count; // Дополнение списка пустыми элементами до 50, если требуется
            if (emptySlots <= 0) { return; }
            for (var i = 0; i < emptySlots; i++)
            {
                var newPretender = new Applicant
                {
                    ExpeditionId = 0,
                    Memo = "",
                    CharacterId = 0,
                    CharacterName = "",
                    CharacterLevel = 0,
                    RegTime = DateTime.MinValue,
                };
                pretenders.Add(newPretender);
            }
            character.SendPacket(new SCExpeditionApplicantsGetPacket(total, count, pretenders));
        }
    }

    /// <summary>
    /// Информация о "наборе персонала".
    /// Должна быть только одна заявка от гильдии.
    /// </summary>
    /// <param name="expeditionId">Id гильдии</param>
    /// <param name="owner">глава гильдии</param>
    /// <param name="interest">интересы гильдии</param>
    /// <param name="day">продолжительность рекламы</param>
    /// <param name="introduce">информация для претендентов</param>
    /// <returns></returns>
    public void AddRecruitment(Character owner, ushort interest, int day, string introduce)
    {
        if (owner.Expedition == null)
            return;

        var expedition = GetExpedition(owner.Expedition.Id);

        if (_recruitments == null || _recruitments.Count == 0)
            _recruitments = new List<ExpeditionRecruitment>();

        var newRecruitment = new ExpeditionRecruitment
        {
            ExpeditionId = expedition.Id,
            Name = expedition.Name,
            Level = expedition.Level,
            OwnerName = expedition.OwnerName,
            Interest = interest,
            Introduce = introduce,
            MemberCount = expedition.Members.Count,
            Apply = false,
            RegTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow + TimeSpan.FromDays(day)
        };

        _recruitments.Add(newRecruitment);
        owner.SendPacket(new SCExpeditionRecruitmentAddPacket());
    }

    /// <summary>
    /// Удалить информацию о "наборе персонала".
    /// </summary>
    /// <param name="expeditionId">Id гильдии</param>
    /// <param name="connection"></param>
    /// <returns></returns>
    public void RemoveRecruitment(GameConnection connection)
    {
        if (connection.ActiveChar.Expedition == null)
            return;

        var expedition = GetExpedition(connection.ActiveChar.Expedition.Id);

        if (_recruitments == null || _recruitments.Count == 0)
            return;

        _recruitments = new List<ExpeditionRecruitment>();
        connection.ActiveChar.SendPacket(new SCExpeditionRecruitmentDeletePacket());
    }

    /// <summary>
    /// Удалить информацию о "наборе персонала" при расформировании гильдии.
    /// </summary>
    /// <param name="expedition">гильдия</param>
    /// <returns></returns>
    private void RemoveRecruitments(Expedition expedition)
    {
        _recruitments.RemoveAll(recruitment => recruitment.ExpeditionId == expedition.Id);
        _removedMembers.Add((uint)expedition.Id);
    }

    /// <summary>
    /// Получить информацию о "наборе персонала".
    /// Должна быть только одна заявка от гильдии.
    /// </summary>
    /// <param name="expeditionId">Id гильдии</param>
    /// <returns></returns>
    private ExpeditionRecruitment GetRecruitment(FactionsEnum expeditionId)
    {
        return _recruitments.FirstOrDefault(recruitment => recruitment.ExpeditionId == expeditionId);
    }

    public void AddPretender(Character character, FactionsEnum expeditionId, string memo)
    {
        var expedition = GetExpedition(expeditionId);
        var pretender = new Applicant
        {
            ExpeditionId = expeditionId,
            Memo = memo,
            CharacterId = character.Id,
            CharacterName = character.Name,
            CharacterLevel = character.Level,
            RegTime = DateTime.UtcNow
        };
        AddPretender(pretender);
        character.SendPacket(new SCExpeditionApplicantAddPacket(expeditionId));
    }

    public void RemovePretender(Character character, FactionsEnum expeditionId)
    {
        var expedition = GetExpedition(expeditionId);
        var pretender = GetPretender(expeditionId, character.Id);
        RemovePretender(pretender);
        character.SendPacket(new SCExpeditionApplicantDeletePacket(expeditionId));
    }

    /// <summary>
    /// Удалить всех претендентов в гильдию
    /// </summary>
    /// <param name="expedition">гильдия</param>
    private void RemovePretenders(Expedition expedition)
    {
        _pretenders.RemoveAll(pretender => pretender.ExpeditionId == expedition.Id);
        _removedMembers.Add((uint)expedition.Id);
    }

    /// <summary>
    /// Удалить претендента
    /// </summary>
    /// <param name="character">претендент</param>
    /// <param name="expedition">гильдия</param>
    private void RemovePretender(Character character, Expedition expedition)
    {
        var pretender = GetPretender(expedition.Id, character.Id);
        if (pretender == null)
        {
            return;
        }
        //RemovePretender(pretender);
        var r = GetRecruitment(pretender.ExpeditionId);
        if (r != null)
        {
            r.Apply = false;
        }
        //_pretenders.Remove(pretender);

        // Находим объект по имени
        var personToRemove = _pretenders.Find(p => p.ExpeditionId == expedition.Id && p.CharacterId == character.Id);
        if (personToRemove != null)
        {
            // Удаляем найденный объект
            _pretenders.Remove(personToRemove);
        }
        character.SendPacket(new SCExpeditionApplicantDeletePacket(expedition.Id));
    }

    /// <summary>
    /// Добавить претендента
    /// может быть только 5 заявок от претендента в разные гильдии
    /// </summary>
    /// <param name="pretender">претендент</param>
    private void AddPretender(Applicant pretender)
    {
        // оставляем только одну запись на гильдию
        var p = GetPretender(pretender.ExpeditionId, pretender.CharacterId);
        if (p == null)
        {
            _pretenders.Add(pretender);
        }
        else
        {
            p.ExpeditionId = pretender.ExpeditionId;
            p.Memo = pretender.Memo;
            p.CharacterId = pretender.CharacterId;
        }

        var r = GetRecruitment(pretender.ExpeditionId);
        if (r != null)
        {
            r.Apply = true;
        }
    }

    /// <summary>
    /// Удалить претендента
    /// </summary>
    /// <param name="pretender">претендент</param>
    private void RemovePretender(Applicant pretender)
    {
        var r = GetRecruitment(pretender.ExpeditionId);
        if (r != null)
        {
            r.Apply = false;
        }
        _pretenders.Remove(pretender);
    }

    /// <summary>
    /// Получить претендента на вступление в гильдию
    /// </summary>
    /// <param name="expeditionId">Id гильдии</param>
    /// <param name="characterId">Id претендента на вступлению в гильдию</param>
    /// <returns></returns>
    private Applicant GetPretender(FactionsEnum expeditionId, uint characterId)
    {
        return _pretenders.FirstOrDefault(pretender => pretender.ExpeditionId == expeditionId && pretender.CharacterId == characterId);
    }

    /// <summary>
    /// Получить всех претендентов на вступление в гильдию
    /// </summary>
    /// <param name="expeditionId">Id гильдии</param>
    /// <returns></returns>
    private List<Applicant> GetPretenders(FactionsEnum expeditionId)
    {
        return _pretenders.Where(pretender => pretender.ExpeditionId == expeditionId).ToList();
    }

    public List<Applicant> GetAllPretenders()
    {
        return _pretenders;
    }

    public void SetMotd(Character character, string motdTitle, string motdContent)
    {
        var expedition = GetExpedition(character.Expedition.Id);
        expedition.MotdTitle = motdTitle;
        expedition.MotdContent = motdContent;

        SendMyExpeditionDescInfo(character);
    }

    public void ExpeditionApplicantAccept(Character character, List<uint> pretenderIds)
    {
        var expedition = GetExpedition(GetExpeditionOfCharacter(character.Id));
        foreach (var pretenderId in pretenderIds)
        {
            var pretender = WorldManager.Instance.GetCharacterById(pretenderId) ?? GetOfflineCharacterInfo(pretenderId);

            if (pretender == null) { return; }

            pretender.Expedition = expedition;
            var newMember = GetMemberFromCharacter(expedition, pretender, false);
            expedition.Members.Add(newMember);

            // отсылаем информацию главе клана
            character.SendPacket(new SCExpeditionApplicantAcceptPacket(pretenderId));

            // отсылаем информацию претенденту
            var changedPacket = new SCUnitExpeditionChangedPacket(pretender.ObjId, pretenderId, "", pretender.Name, 0, expedition.Id, false);
            pretender.BroadcastPacket(changedPacket, true);
            expedition.SendPacket(changedPacket);
            SendMyExpeditionInfo(pretender);
            expedition.OnCharacterLogin(pretender);

            RemovePretender(pretender, expedition);
        }
    }

    public void ExpeditionApplicantReject(Character character, List<uint> pretenderIds)
    {
        var expedition = GetExpedition(GetExpeditionOfCharacter(character.Id));
        foreach (var characterId in pretenderIds)
        {
            var pretender = WorldManager.Instance.GetCharacterById(characterId);
            var newMember = GetMemberFromCharacter(expedition, pretender, false);

            // отсылаем информацию главе клана
            character.SendPacket(new SCExpeditionApplicantRejectPacket(characterId));

            // отсылаем информацию претенденту
            //pretender.Expedition = expedition;
            //expedition.Members.Add(newMember);

            //pretender.BroadcastPacket(new SCUnitExpeditionChangedPacket(pretender.ObjId, pretender.Id, "", pretender.Name, 0, expedition.Id, false), true);
            //SendMyExpeditionInfo(pretender);
            //expedition.OnCharacterLogin(pretender);
        }
    }

    public Character GetOfflineCharacterInfo(uint characterId)
    {
        var characterInfo = new Character(new UnitCustomModelParams());
        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM characters WHERE id IN(" + string.Join(",", characterId) + ")";
        command.Prepare();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            characterInfo.Id = reader.GetUInt32("id");
            characterInfo.Name = reader.GetString("name");
            characterInfo.Level = reader.GetByte("level");
            var expeditionId = (FactionsEnum)reader.GetUInt32("expedition_id");
            if (expeditionId != 0)
            {
                var expedition = GetExpedition(expeditionId);
                characterInfo.Expedition = expedition;
            }
            characterInfo.Ability1 = (AbilityType)reader.GetByte("ability1");
            characterInfo.Ability2 = (AbilityType)reader.GetByte("ability2");
            characterInfo.Ability3 = (AbilityType)reader.GetByte("ability3");
            var position = new Transform(null, null, reader.GetUInt32("world_id"), reader.GetUInt32("zone_id"), 1, reader.GetFloat("x"), reader.GetFloat("y"), reader.GetFloat("z"), 0, 0, 0);
            characterInfo.Transform = position;
            characterInfo.Transform.ZoneId = position.ZoneId;
            characterInfo.InParty = false;
            characterInfo.IsOnline = false;
        }

        return characterInfo;
    }
}
