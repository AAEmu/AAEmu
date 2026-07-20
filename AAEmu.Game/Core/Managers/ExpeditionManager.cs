using System.Numerics;
using System.Text.RegularExpressions;

using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Expeditions;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Team;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Core.Managers;

public class ExpeditionManager(IExpeditionIdManager expeditionIdManager, ITeamManager teamManager, IWorldManager worldManager, IChatManager chatManager) : Singleton<ExpeditionManager>, IExpeditionManager
{
    //private ExpeditionConfig _config;
    private Regex _nameRegex;

    private Dictionary<FactionsEnum, Expedition> _expeditions;

    public IEnumerable<Expedition> Expeditions { get => _expeditions.Values; }

    private Expedition Create(string name, Character owner)
    {
        var expedition = new Expedition
        {
            Id = (FactionsEnum)expeditionIdManager.GetNextId(),
            MotherId = owner.Faction.Id,
            Name = name,
            OwnerId = owner.Id,
            OwnerName = owner.Name,
            UnitOwnerType = 0,
            PoliticalSystem = 1,
            Created = DateTime.UtcNow,
            AggroLink = false,
            DiplomacyTarget = false,
            Members = []
        };
        expedition.Policies = GetDefaultPolicies(expedition.Id);

        var member = GetMemberFromCharacter(expedition, owner, true);

        expedition.Members.Add(member);

        return expedition;
    }

    public void Load()
    {
        _expeditions = [];
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
                        var expedition = new Expedition
                        {
                            Id = (FactionsEnum)reader.GetUInt32("id"),
                            MotherId = (FactionsEnum)reader.GetUInt32("mother"),
                            Name = reader.GetString("name"),
                            OwnerId = reader.GetUInt32("owner"),
                            OwnerName = reader.GetString("owner_name"),
                            UnitOwnerType = 0,
                            PoliticalSystem = 1,
                            Created = reader.GetDateTime("created_at"),
                            AggroLink = false,
                            DiplomacyTarget = false
                        };

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
                            var member = new ExpeditionMember
                            {
                                CharacterId = reader.GetUInt32("character_id"),
                                ExpeditionId = (FactionsEnum)reader.GetUInt32("expedition_id"),
                                Role = reader.GetByte("role"),
                                Memo = reader.GetString("memo"),
                                LastWorldLeaveTime = reader.GetDateTime("last_leave_time"),
                                Name = reader.GetString("name"),
                                Level = reader.GetByte("level"),
                                Abilities =
                                [
                                    reader.GetByte("ability1"), reader.GetByte("ability2"), reader.GetByte("ability3")
                                ],
                                IsOnline = false,
                                InParty = false
                            };
                            expedition.Members.Add(member);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText =
                        "SELECT * FROM expedition_role_policies WHERE expedition_id = @expedition_id";
                    command.Parameters.AddWithValue("@expedition_id", expedition.Id);
                    command.Prepare();
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var policy = new ExpeditionRolePolicy
                            {
                                ExpeditionId = (FactionsEnum)reader.GetUInt32("expedition_id"),
                                Role = reader.GetByte("role"),
                                Name = reader.GetString("name"),
                                DominionDeclare = reader.GetBoolean("dominion_declare"),
                                Invite = reader.GetBoolean("invite"),
                                Expel = reader.GetBoolean("expel"),
                                Promote = reader.GetBoolean("promote"),
                                Dismiss = reader.GetBoolean("dismiss"),
                                Chat = reader.GetBoolean("chat"),
                                ManagerChat = reader.GetBoolean("manager_chat"),
                                SiegeMaster = reader.GetBoolean("siege_master"),
                                JoinSiege = reader.GetBoolean("join_siege")
                            };
                            expedition.Policies.Add(policy);
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
            if (name.Equals(exp.Name))
            {
                connection.ActiveChar.SendErrorMessage(ErrorMessageType.ExpeditionNameExist);
                return;
            }

        // ----------------- Conditions, can change this...
        var team = teamManager.GetActiveTeamByUnit(owner.Id);
        if (team == null)// || !team.IsParty)
        {
            // We send the same error on number of party members when we don't have a party
            connection.ActiveChar.SendErrorMessage(ErrorMessageType.ExpeditionCreateMember);
            return;
        }

        // Check the number of members in the party that meet the requirements
        List<TeamMember> validMembers = [];
        List<TeamMember> teamMembers = [.. team.Members.ToList()];

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
                [
                    new MoneyChange(-AppConfiguration.Instance.Expedition.Create.Cost)
                ],
                [])
        );
        // -----------------

        var expedition = Create(name, owner);
        _expeditions.Add(expedition.Id, expedition);

        owner.Expedition = expedition;

        owner.SendPacket(
            new SCFactionCreatedPacket(expedition, owner.ObjId, [(owner.ObjId, owner.Id, owner.Name)])
        );

        worldManager.BroadcastPacketToServer(new SCSystemFactionListPacket(expedition));
        owner.BroadcastPacket(
            new SCUnitExpeditionChangedPacket(owner.ObjId, owner.Id, "", owner.Name, 0, (uint)expedition.Id, false),
            true
        );

        chatManager.GetGuildChat(expedition).JoinChannel(owner);
        SendExpeditionInfo(owner);
        // owner.Save(); // Moved to SaveMananger

        foreach (var m in validMembers)
        {
            if (m.Character.Id == owner.Id)
                continue;

            var invited = m.Character;
            var newMember = GetMemberFromCharacter(expedition, invited, false);

            invited.Expedition = expedition;
            expedition.Members.Add(newMember);

            invited.BroadcastPacket(
                new SCUnitExpeditionChangedPacket(invited.ObjId, invited.Id, "", invited.Name, 0, (uint)expedition.Id, false),
                true);
            SendExpeditionInfo(invited);
            expedition.OnCharacterLogin(invited);
            // invited.Save(); // Moved to SaveMananger
        }
        Save(expedition);
    }

    public void Invite(GameConnection connection, string invitedName)
    {
        var inviter = connection.ActiveChar;

        var inviterMember = inviter.Expedition?.GetMember(inviter);
        if (inviterMember == null || !inviter.Expedition.GetPolicyByRole(inviterMember.Role).Invite)
            return;

        var invited = worldManager.GetCharacter(invitedName);
        if (invited == null) return;
        if (invited.Expedition != null) return;

        invited.SendPacket(
            new SCExpeditionInvitationPacket(inviter.Id, inviter.Name, (uint)inviter.Expedition.Id,
                inviter.Expedition.Name)
        );
    }

    public void ReplyInvite(GameConnection connection, FactionsEnum id1, uint id2, bool reply)
    {
        var invited = connection.ActiveChar;
        if (!reply)
            return;

        var expedition = _expeditions[id1];
        var newMember = GetMemberFromCharacter(expedition, invited, false);

        invited.Expedition = expedition;
        expedition.Members.Add(newMember);

        invited.BroadcastPacket(
            new SCUnitExpeditionChangedPacket(invited.ObjId, invited.Id, "", invited.Name, 0, (uint)expedition.Id, false),
            true);
        SendExpeditionInfo(invited);
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
        var changedPacket = new SCUnitExpeditionChangedPacket(
            character.ObjId,
            character.Id,
            "",
            character.Name,
            (uint)expedition.Id,
            0,
            false
        );
        character.Expedition = null;
        character.BroadcastPacket(changedPacket, true);
        expedition.SendPacket(changedPacket);
        Save(expedition);
    }

    public void Kick(GameConnection connection, uint kickedId)
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

        var kickedChar = worldManager.GetCharacterById(kickedId);

        var changedPacket = new SCUnitExpeditionChangedPacket(kickedChar?.ObjId ?? 0,
            kicked.CharacterId, character.Name, kicked.Name, (uint)expedition.Id, 0, true);

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
        expedition.SendPacket(
            new SCExpeditionRoleChangedPacket(changedMember.CharacterId, changedMember.Role, changedMember.Name)
        );
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

        expedition.SendPacket(
            new SCExpeditionOwnerChangedPacket(
                ownerMember.CharacterId,
                newOwnerMember.CharacterId,
                newOwnerMember.Name
            )
        );
        expedition.SendPacket(
            new SCExpeditionRoleChangedPacket(ownerMember.CharacterId, ownerMember.Role, ownerMember.Name)
        );
        expedition.SendPacket(
            new SCExpeditionRoleChangedPacket(newOwnerMember.CharacterId, newOwnerMember.Role, newOwnerMember.Name)
        );
        Save(expedition);
    }

    public bool Disband(Character owner)
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
            var c = worldManager.GetCharacterById(guild.Members[i].CharacterId);
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
        guild.isDisbanded = true;
        Save(guild);
        return true;
    }

    public static void SendExpeditionInfo(Character character)
    {
        var members = character.Expedition.Members;
        var total = (uint)members.Count;
        var id = character.Expedition.Id;

        character.SendPacket(new SCExpeditionRolePolicyListPacket(character.Expedition.Policies));

        for (var i = 0; i < members.Count; i += 20)
        {
            var block = members.Skip(i).Take(20).ToList();
            character.SendPacket(new SCExpeditionMemberListPacket(total, (uint)id, block));
        }
    }

    public static void Save(Expedition expedition)
    {
        using (var connection = MySQL.CreateConnection())
        using (var transaction = connection.BeginTransaction())
        {
            expedition.Save(connection, transaction);
            transaction.Commit();
        }
    }

    public static ExpeditionMember GetMemberFromCharacter(Expedition expedition, Character character, bool owner)
    {
        var member = new ExpeditionMember
        {
            IsOnline = true,
            Name = character.Name,
            Level = character.Level,
            Role = (byte)(owner ? 255 : 0),
            Memo = "",
            Position = new Vector3(character.Transform.World.Position.X, character.Transform.World.Position.Y, character.Transform.World.Position.Z),
            ZoneId = character.Transform.ZoneId,
            Abilities = [(byte)character.Ability1, (byte)character.Ability2, (byte)character.Ability3],
            ExpeditionId = expedition.Id,
            CharacterId = character.Id,
            LastWorldLeaveTime = DateTime.UtcNow
        };

        return member;
    }

    public void SendExpeditions(Character character)
    {
        if (_expeditions.Values.Count > 0)
        {
            var expeditions = _expeditions.Values.ToArray();
            for (var i = 0; i < expeditions.Length; i += 20)
            {
                var temp = new SystemFaction[expeditions.Length - i <= 20 ? expeditions.Length - i : 20];
                Array.Copy(expeditions, i, temp, 0, temp.Length);
                character.SendPacket(new SCSystemFactionListPacket(temp));
            }
        }

        character.SendPacket(new SCExpeditionRolePolicyListPacket([]));
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
}
