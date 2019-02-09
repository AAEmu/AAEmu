using System.Collections.Generic;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.DB;
using MySql.Data.MySqlClient;
using NLog;

namespace AAEmu.Game.Core.Managers
{
    public class FamilyManager : Singleton<FamilyManager>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        public Dictionary<uint, Family> _families;
        public Dictionary<uint, FamilyMember> _familyMembers;

        public void Load()
        {
            _families = new Dictionary<uint, Family>();

            _log.Info("Loading families...");
            using (var connection = MySQL.CreateConnection())
            {
                var temp = new Dictionary<uint, byte>();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT DISTINCT family FROM characters";
                    command.Prepare();
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            //Create the family
                            var familyId = reader.GetUInt32("family");
                            if(familyId == 0) continue;

                            Family familyTemplate = new Family();
                            familyTemplate.Id = familyId;
                            using (var connection2 = MySQL.CreateConnection())
                            {
                                using (var command2 = connection2.CreateCommand())
                                {
                                    command2.CommandText = "SELECT * FROM family_members WHERE family_id=@family_id";
                                    command2.Prepare();
                                    command2.Parameters.AddWithValue("family_id", familyTemplate.Id);
                                    using (var reader2 = command2.ExecuteReader())
                                    {
                                        while (reader2.Read())
                                        {
                                            FamilyMember memberTemplate = new FamilyMember();
                                            memberTemplate.Id = reader2.GetUInt32("character_id");
                                            memberTemplate.Name = reader2.GetString("name");
                                            memberTemplate.Role = reader2.GetByte("role");
                                            memberTemplate.Title = reader2.GetString("title");
                                            memberTemplate.Online = false;
                                            familyTemplate.AddMember(memberTemplate);
                                            _familyMembers.Add(memberTemplate.Id, memberTemplate);
                                        }
                                    }
                                }
                            }
                            
                            _families.Add(familyTemplate.Id, familyTemplate);
                        }
                    }
                }
            }

            _log.Info("Loaded {0} families", _families.Count);
        }

        public void SaveAllFamilies()
        {
            using (var connection = MySQL.CreateConnection()) {
                using (var transaction = connection.BeginTransaction()) {
                    foreach (Family family in _families.Values) {
                        SaveFamily(family, connection, transaction);
                    }
                }
            }
        }

        public void SaveFamily(Family family)
        {
            using (var connection = MySQL.CreateConnection()) {
                using (var transaction = connection.BeginTransaction()) {
                    SaveFamily(family, connection, transaction);
                }
            }
        }

        public void SaveFamily(Family family, MySqlConnection connection, MySqlTransaction transaction)
        {
            using (var command = connection.CreateCommand()) {
                command.Connection = connection;
                command.Transaction = transaction;
                foreach (var member in family.Members) {
                    command.CommandText = "REPLACE INTO " +
                                          "family_members(`character_id`,`family_id`,`name`,`role`,`title`)" +
                                          " VALUES " +
                                          "(@character_id,@family_id,@name,@role,@title)";
                    command.Parameters.AddWithValue("@character_id", member.Id);
                    command.Parameters.AddWithValue("@family_id", family.Id);
                    command.Parameters.AddWithValue("@name", member.Name);
                    command.Parameters.AddWithValue("@role", member.Role);
                    command.Parameters.AddWithValue("@title", member.Title);
                    command.ExecuteNonQuery();
                    command.Parameters.Clear();
                }

                command.Transaction.Commit();
            }
        }

        public void RemoveMemberFromDb(uint characterId) {
            using (var connection = MySQL.CreateConnection()) {
                using (var transaction = connection.BeginTransaction()) {
                    using (var command = connection.CreateCommand()) {
                        command.Connection = connection;
                        command.Transaction = transaction;
                        command.CommandText = "DELETE FROM family_members WHERE character_id = @character_id";
                        command.Parameters.AddWithValue("@character_id", characterId);
                        command.ExecuteNonQuery();
                        command.Parameters.Clear();
                        command.Transaction.Commit();
                    }
                }
            }
        }

        public void InviteToFamily(Character inviter, string invitedCharacterName, string title) {
            Character invited = CharacterManager.Instance.GetCharacterByName(invitedCharacterName); 
            if (invited != null && invited.Family == 0) {
                SCFamilyInvitationPacket invitePacket = new SCFamilyInvitationPacket(inviter.Id, inviter.Name, 1, title);
                invited.SendPacket(invitePacket);
            }
        }

        public void ReplyToInvite(uint invitorId, Character invitedChar, bool join, string title) {
            if (!join) return;

            Character invitor = CharacterManager.Instance.GetCharacterById(invitorId);
            if (invitor == null) return;

            if (invitor.Family == 0) {
                Family newFamily = new Family();
                newFamily.Id = FamilyIdManager.Instance.GetNextId();
                newFamily.AddMember(GetMemberForCharacter(invitor, 1, ""));

                _families.Add(newFamily.Id, newFamily);
                invitor.Family = newFamily.Id;
                SCFamilyCreatedPacket familyCreatedPacket = new SCFamilyCreatedPacket(newFamily);
                invitor.SendPacket(familyCreatedPacket);
            }

            Family family = _families[invitor.Family];
            family.AddMember(GetMemberForCharacter(invitedChar, 0, title));
            invitedChar.Family = invitor.Family;
            SCFamilyMemberAddedPacket familyMemberAddedPacket = new SCFamilyMemberAddedPacket(family, family.Members.Count - 1);
            
            foreach(FamilyMember member in family.Members) {
                CharacterManager.Instance.GetCharacterById(member.Id)?.SendPacket(familyMemberAddedPacket);
            }

            SaveFamily(family);
        }

        public FamilyMember GetMemberForCharacter(Character character, byte owner, string title)
        {
            FamilyMember member = new FamilyMember();
            member.Id = character.Id;
            member.Name = character.Name;
            member.Online = true;
            member.Role = owner;
            member.Title = title;
            return member;
        }

        /*
            Called by a character when logging in. Sends the character a family description packet. Sends every other family member an Online update packet.
         */
        public void OnCharacterLogin(Character character) {
            Family family = _families[character.Family];
            FamilyMember familyMember = _familyMembers[character.Id];
            familyMember.Online = true;

            SCFamilyDescPacket familyDescPacket = new SCFamilyDescPacket(family);
            character.SendPacket(familyDescPacket);

            SCFamilyMemberOnlinePacket familyMemberOnlinePacket = new SCFamilyMemberOnlinePacket(family.Id, familyMember.Id, true);
            foreach (FamilyMember member in family.Members) {
                CharacterManager.Instance.GetCharacterById(member.Id)?.SendPacket(familyMemberOnlinePacket);
            }
        }

        /*
            Called when a player logs out. Sends an update to every family member to mark him as offline.
         */
        public void OnCharacterLogout(Character character) {
            Family family = _families[character.Family];
            FamilyMember offlineMember = family.GetMember(character);
            offlineMember.Online = false;

            SCFamilyMemberOnlinePacket familyMemberOnlinePacket = new SCFamilyMemberOnlinePacket(family.Id, character.Id, false);
            foreach (FamilyMember member in family.Members) {
                if(member.Id != character.Id) //No need to tell our own player he's offline
                    CharacterManager.Instance.GetCharacterById(member.Id)?.SendPacket(familyMemberOnlinePacket);
            }
        }

        /*
            Called when a player wants to leave a family. Removes him from the family, saves it, and updates family members.
         */
        public void LeaveFamily(Character character) {
            Family family = _families[character.Family];
            character.Family = 0;
            family.RemoveMember(character);

            RemoveMemberFromDb(character.Id);

            SCFamilyRemovedPacket familyRemovedPacket = new SCFamilyRemovedPacket(family.Id);
            character.SendPacket(familyRemovedPacket);

            SCFamilyMemberRemovedPacket memberRemovedPacket = new SCFamilyMemberRemovedPacket(family.Id, false, character.Id);
            foreach (FamilyMember member in family.Members) {
                CharacterManager.Instance.GetCharacterById(member.Id)?.SendPacket(memberRemovedPacket);
            }

            SaveFamily(family);

            if (family.Members.Count < 2) {
                DisbandFamily(family);
            }
        }
 
        /*
            Called when a family is disbanded (when they have less than 2 members)
         */
        public void DisbandFamily(Family family) {
            SCFamilyRemovedPacket familyRemovedPacket = new SCFamilyRemovedPacket(family.Id);

            for (int i = 0; i < family.Members.Count; i++) {
                FamilyMember member = family.Members[i];
                family.RemoveMember(member);
                Character character = CharacterManager.Instance.GetCharacterById(member.Id);
                if (character != null) {
                    character.SendPacket(familyRemovedPacket);
                    character.Family = 0;
                }   

                RemoveMemberFromDb(member.Id);
            }

            _families.Remove(family.Id);
        }

        /* TODO : 
            - Kick
            - ChangeOwner
            - ChangeTitle
        */
    }
}