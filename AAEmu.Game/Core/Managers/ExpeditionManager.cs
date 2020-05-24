using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AAEmu.Commons.IO;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Expeditions;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Error;
using AAEmu.Game.Utils.DB;
using NLog;
using AAEmu.Game.Models.Game.Team;

namespace AAEmu.Game.Core.Managers
{
    public class ExpeditionManager : Singleton<ExpeditionManager>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        private ExpeditionConfig _config;
        private Regex _nameRegex;

        private Dictionary<uint, Expedition> _expeditions;

        public Expedition Create(string name, Character owner)
        {
            var expedition = new Expedition();
            expedition.Id = ExpeditionIdManager.Instance.GetNextId();
            expedition.MotherId = owner.Faction.Id;
            expedition.Name = name;
            expedition.OwnerId = owner.Id;
            expedition.OwnerName = owner.Name;
            expedition.UnitOwnerType = 0;
            expedition.PoliticalSystem = 1;
            expedition.Created = DateTime.Now;
            expedition.AggroLink = false;
            expedition.DiplomacyTarget = false;
            expedition.Members = new List<ExpeditionMember>();
            expedition.Policies = GetDefaultPolicies(expedition.Id);

            var member = GetMemberFromCharacter(expedition, owner, true);

            expedition.Members.Add(member);

            return expedition;
        }

        public void Load()
        {
            _expeditions = new Dictionary<uint, Expedition>();

            var contents = FileManager.GetFileContents($"{FileManager.AppPath}Data/expedition.json");
            if (string.IsNullOrWhiteSpace(contents))
                throw new IOException($"File {FileManager.AppPath}Data/expedition.json doesn't exists or is empty.");

            if (!JsonHelper.TryDeserializeObject(contents, out _config, out _)) // TODO here can out Exception
                throw new Exception(
                    $"ExpeditionManager: Parse {FileManager.AppPath}Data/expedition.json file");
            _nameRegex = new Regex(_config.NameRegex, RegexOptions.Compiled);

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
                            expedition.Id = reader.GetUInt32("id");
                            expedition.MotherId = reader.GetUInt32("mother");
                            expedition.Name = reader.GetString("name");
                            expedition.OwnerId = reader.GetUInt32("owner");
                            expedition.OwnerName = reader.GetString("owner_name");
                            expedition.UnitOwnerType = 0;
                            expedition.PoliticalSystem = 1;
                            expedition.Created = reader.GetDateTime("created_at");
                            expedition.AggroLink = false;
                            expedition.DiplomacyTarget = false;

                            _expeditions.Add(expedition.Id, expedition);
                        }
                    }
                }

                foreach (var expedition in _expeditions.Values)
                {
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "SELECT * FROM expedition_members WHERE expedition_id = @expedition_id";
                        command.Prepare();
                        command.Parameters.AddWithValue("@expedition_id", expedition.Id);
                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var member = new ExpeditionMember();
                                member.CharacterId = reader.GetUInt32("character_id");
                                member.ExpeditionId = reader.GetUInt32("expedition_id");
                                member.Role = reader.GetByte("role");
                                member.Memo = reader.GetString("memo");
                                member.LastWorldLeaveTime = reader.GetDateTime("last_leave_time");
                                member.Name = reader.GetString("name");
                                member.Level = reader.GetByte("level");
                                member.Abilities = new byte[]
                                {
                                    reader.GetByte("ability1"), reader.GetByte("ability2"), reader.GetByte("ability3")
                                };
                                member.IsOnline = false;
                                member.InParty = false;
                                expedition.Members.Add(member);
                            }
                        }
                    }

                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText =
                            "SELECT * FROM expedition_role_policies WHERE expedition_id = @expedition_id";
                        command.Prepare();
                        command.Parameters.AddWithValue("@expedition_id", expedition.Id);
                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var policy = new ExpeditionRolePolicy();
                                policy.Id = reader.GetUInt32("expedition_id");
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
                }
            }
        }

        public List<ExpeditionRolePolicy> GetDefaultPolicies(uint expeditionId)
        {
            var res = new List<ExpeditionRolePolicy>();
            foreach (var rolePolicy in _config.RolePolicies)
            {
                var policy = rolePolicy.Clone();
                policy.Id = expeditionId;
                res.Add(policy);
            }

            return res;
        }

        public Expedition GetExpedition(uint id)
        {
            if (_expeditions.ContainsKey(id))
                return _expeditions[id];
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
            var team = TeamManager.Instance.GetActiveTeamByUnit(owner.Id);
            if (team == null)// || !team.IsParty)
            {
                // We send the same error on number of party members when we don't have a party
                connection.ActiveChar.SendErrorMessage(ErrorMessageType.ExpeditionCreateMember);
                return;
            }

            // Check the number of members in the party that meet the requirements
            List<TeamMember> validMembers = new List<TeamMember>();
            List<TeamMember> teamMembers = new List<TeamMember>();
            teamMembers.AddRange(team.Members.ToList());

            foreach (var m in teamMembers)
            {
                if (m?.Character == null)
                    continue;

                if (m.Character.Level < _config.Create.Level)
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

            if (validMembers.Count < _config.Create.PartyMemberCount)
            {
                connection.ActiveChar.SendErrorMessage(ErrorMessageType.ExpeditionCreateMember);
                return;
            }

            if (owner.Money < _config.Create.Cost)
            {
                connection.ActiveChar.SendErrorMessage(ErrorMessageType.ExpeditionCreateMoney);
                return;
            }

            owner.Money -= _config.Create.Cost;
            owner.SendPacket(
                new SCItemTaskSuccessPacket(
                    ItemTaskType.ExpeditionCreation,
                    new List<ItemTask>
                    {
                        new MoneyChange(-_config.Create.Cost)
                    },
                    new List<ulong>())
            );
            // -----------------

            var expedition = Create(name, owner);
            _expeditions.Add(expedition.Id, expedition);

            owner.Expedition = expedition;

            owner.SendPacket(
                new SCFactionCreatedPacket(expedition, owner.ObjId, new[] { (owner.ObjId, owner.Id, owner.Name) })
            );

            WorldManager.Instance.BroadcastPacketToServer(new SCFactionListPacket(expedition));
            owner.BroadcastPacket(
                new SCUnitExpeditionChangedPacket(owner.ObjId, owner.Id, "", owner.Name, 0, expedition.Id, false),
                true
            );

            ChatManager.Instance.GetGuildChat(expedition).JoinChannel(owner);
            SendExpeditionInfo(owner);
            // owner.Save(); // Moved to SaveMananger

            foreach(var m in validMembers)
            {
                if (m.Character.Id == owner.Id)
                    continue;

                var invited = m.Character;
                var newMember = GetMemberFromCharacter(expedition, invited, false);

                invited.Expedition = expedition;
                expedition.Members.Add(newMember);

                invited.BroadcastPacket(
                    new SCUnitExpeditionChangedPacket(invited.ObjId, invited.Id, "", invited.Name, 0, expedition.Id, false),
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

            var invited = WorldManager.Instance.GetCharacter(invitedName);
            if (invited == null) return;
            if (invited.Expedition != null) return;

            invited.SendPacket(
                new SCExpeditionInvitationPacket(inviter.Id, inviter.Name, inviter.Expedition.Id,
                    inviter.Expedition.Name)
            );
        }

        public void ReplyInvite(GameConnection connection, uint id1, uint id2, bool reply)
        {
            var invited = connection.ActiveChar;
            if (!reply)
                return;

            var expedition = _expeditions[id1];
            var newMember = GetMemberFromCharacter(expedition, invited, false);

            invited.Expedition = expedition;
            expedition.Members.Add(newMember);

            invited.BroadcastPacket(
                new SCUnitExpeditionChangedPacket(invited.ObjId, invited.Id, "", invited.Name, 0, expedition.Id, false),
                true);
            SendExpeditionInfo(invited);
            expedition.OnCharacterLogin(invited);
            Save(expedition);
            // invited.Save(); // Moved to SaveMananger
        }

        public void ChangeExpeditionRolePolicy(GameConnection connection, ExpeditionRolePolicy policy)
        {
            var expedition = _expeditions[policy.Id];

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

        public void Leave(GameConnection connection)
        {
            var character = connection.ActiveChar;

            var expedition = character.Expedition;
            if (expedition == null) return;

            character.Expedition = null;
            expedition.RemoveMember(expedition.GetMember(character));
            var changedPacket = new SCUnitExpeditionChangedPacket(
                character.ObjId,
                character.Id,
                "",
                character.Name,
                expedition.Id,
                0,
                false
            );
            character.Expedition = null;
            character.BroadcastPacket(changedPacket, true);
            expedition.SendPacket(changedPacket);
            Save(expedition);
            // character.Save(); // Moved to SaveMananger
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

            var kickedChar = WorldManager.Instance.GetCharacterById(kickedId);

            var changedPacket = new SCUnitExpeditionChangedPacket(kickedChar?.ObjId ?? 0,
                kicked.CharacterId, character.Name, kicked.Name, expedition.Id, 0, true);

            if (kickedChar != null)
            {
                kickedChar.Expedition = null;
                kickedChar?.BroadcastPacket(changedPacket, true);
                // kickedChar.Save(); // Moved to SaveMananger
            }
            expedition.SendPacket(changedPacket);

            Save(expedition);
        }

        public void ChangeMemberRole(GameConnection connection, byte newRole, uint changedId)
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

        public void ChangeOwner(GameConnection connection, uint newOwnerId)
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
            for (int i = guild.Members.Count - 1; i >= 0; i--)
            {
                var c = WorldManager.Instance.GetCharacterById(guild.Members[i].CharacterId);
                if (c != null)
                {
                    if (c.IsOnline)
                        c.SendPacket(new SCExpeditionDismissedPacket(guild.Id, true));
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

        public void SendExpeditionInfo(Character character)
        {
            character.SendPacket(new SCExpeditionRolePolicyListPacket(character.Expedition.Policies));
            character.SendPacket(new SCExpeditionMemberListPacket(character.Expedition));
        }

        public void Save(Expedition expedition)
        {
            using (var connection = MySQL.CreateConnection())
            using (var transaction = connection.BeginTransaction())
            {
                expedition.Save(connection, transaction);
                transaction.Commit();
            }
        }

        public ExpeditionMember GetMemberFromCharacter(Expedition expedition, Character character, bool owner)
        {
            var member = new ExpeditionMember();
            member.IsOnline = true;
            member.Name = character.Name;
            member.Level = character.Level;
            member.Role = (byte)(owner ? 255 : 0);
            member.Memo = "";
            member.X = character.Position.X;
            member.Y = character.Position.Y;
            member.Z = character.Position.Z;
            member.ZoneId = (int)character.Position.ZoneId;
            member.Abilities = new[]
                {(byte)character.Ability1, (byte)character.Ability2, (byte)character.Ability3};
            member.ExpeditionId = expedition.Id;
            member.CharacterId = character.Id;

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
                    character.SendPacket(new SCFactionListPacket(temp));
                }
            }

            character.SendPacket(new SCExpeditionRolePolicyListPacket(new List<ExpeditionRolePolicy>()));
        }
    }
}
