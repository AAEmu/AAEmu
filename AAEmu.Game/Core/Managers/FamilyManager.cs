using System.Collections.Generic;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.DB;
using NLog;

namespace AAEmu.Game.Core.Managers
{
    public class FamilyManager : Singleton<FamilyManager>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        private Dictionary<uint, Family> _families;
        private Dictionary<uint, FamilyMember> _familyMembers;

        public void Load()
        {
            _families = new Dictionary<uint, Family>();
            _familyMembers = new Dictionary<uint, FamilyMember>();

            _log.Info("Loading families...");
            using (var connection = MySQL.CreateConnection())
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT DISTINCT family FROM characters";
                    command.Prepare();
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var familyId = reader.GetUInt32("family");
                            if (familyId == 0)
                                continue;

                            var family = new Family();
                            family.Id = familyId;
                            _families.Add(family.Id, family);

                            using (var connection2 = MySQL.CreateConnection()) {
                                family.Load(connection2); // TODO : Maybe find a prettier way
                            }

                            foreach (var member in family.Members)
                                _familyMembers.Add(member.Id, member);
                        }
                    }
                }
            }

            _log.Info("Loaded {0} families", _families.Count);
        }

        public void SaveAllFamilies()
        {
            using (var connection = MySQL.CreateConnection())
            using (var transaction = connection.BeginTransaction())
            {
                foreach (var family in _families.Values)
                    family.Save(connection, transaction);

                transaction.Commit(); // TODO try/catch
            }
        }

        public void SaveFamily(Family family)
        {
            using (var connection = MySQL.CreateConnection())
            using (var transaction = connection.BeginTransaction())
            {
                family.Save(connection, transaction);

                transaction.Commit(); // TODO try/catch
            }
        }

        public void InviteToFamily(Character inviter, string invitedCharacterName, string title)
        {
            var invited = WorldManager.Instance.GetCharacter(invitedCharacterName);
            if (invited != null && invited.Family == 0)
                invited.SendPacket(new SCFamilyInvitationPacket(inviter.Id, inviter.Name, 1, title));
        }

        public void ReplyToInvite(uint invitorId, Character invitedChar, bool join, string title)
        {
            if (!join)
                return;

            var invitor = WorldManager.Instance.GetCharacterById(invitorId);
            if (invitor == null) return;

            if (invitor.Family == 0)
            {
                var newFamily = new Family();
                newFamily.Id = FamilyIdManager.Instance.GetNextId();
                FamilyMember owner = GetMemberForCharacter(invitor, 1, "");
                newFamily.AddMember(owner);
                _familyMembers.Add(owner.Id, owner);

                _families.Add(newFamily.Id, newFamily);
                invitor.Family = newFamily.Id;
                invitor.SendPacket(new SCFamilyCreatedPacket(newFamily));
            }

            var family = _families[invitor.Family];
            FamilyMember member = GetMemberForCharacter(invitedChar, 0, title);
            family.AddMember(member);
            _familyMembers.Add(member.Id, member);
            invitedChar.Family = invitor.Family;

            family.SendPacket(new SCFamilyMemberAddedPacket(family, family.Members.Count - 1));

            SaveFamily(family);
        }

        /// <summary>
        /// Called by a character when logging in. Sends the character a family description packet. Sends every other family member an Online update packet.
        /// </summary>
        /// <param name="character"></param>
        public void OnCharacterLogin(Character character)
        {
            var family = _families[character.Family];
            var member = _familyMembers[character.Id];
            member.Character = character;

            character.SendPacket(new SCFamilyDescPacket(family));
            family.SendPacket(new SCFamilyMemberOnlinePacket(family.Id, member.Id, true));
        }

        /// <summary>
        /// Called when a player logs out. Sends an update to every family member to mark him as offline.
        /// </summary>
        /// <param name="character"></param>
        public void OnCharacterLogout(Character character)
        {
            var family = _families[character.Family];
            var member = family.GetMember(character);
            member.Character = null;

            family.SendPacket(new SCFamilyMemberOnlinePacket(family.Id, character.Id, false), character.Id);
        }

        /// <summary>
        /// Called when a player wants to leave a family. Removes him from the family, saves it, and updates family members.
        /// </summary>
        /// <param name="character"></param>
        public void LeaveFamily(Character character)
        {
            var family = _families[character.Family];
            character.Family = 0;
            family.RemoveMember(character);
            _familyMembers.Remove(character.Id);

            character.SendPacket(new SCFamilyRemovedPacket(family.Id));
            family.SendPacket(new SCFamilyMemberRemovedPacket(family.Id, false, character.Id));

            if (family.Members.Count < 2)
                DisbandFamily(family);
            else
                SaveFamily(family); // TODO need to think how to do right
        }

        /// <summary>
        /// Called when a family is disbanded (when they have less than 2 members)
        /// </summary>
        /// <param name="family"></param>
        private void DisbandFamily(Family family)
        {
            var removed = new SCFamilyRemovedPacket(family.Id);

            for (var i = family.Members.Count - 1; i > -1; i--)
            {
                var member = family.Members[i];
                if (member.Character != null)
                {
                    member.Character.SendPacket(removed);
                    member.Character.Family = 0;
                }

                family.RemoveMember(member);
                _familyMembers.Remove(member.Id);
            }

            SaveFamily(family);
            _families.Remove(family.Id);
        }

        /// <summary>
        /// Called when a family member is kicked. Disbands the family if it has 2 members.
        /// </summary>
        /// <param name="kicker"></param>
        /// <param name="kickedId"></param>
        public void KickMember(Character kicker, uint kickedId) 
        {
            if (kicker.Family == 0) return;
            var family = _families[kicker.Family];

            var kickerMember = family.GetMember(kicker);
            if (kickerMember.Role != 1) return; // Only the steward can kick

            // Load kicked character
            var kickedCharacter = WorldManager.Instance.GetCharacterById(kickedId);
            var isOnline = false;
            if (kickedCharacter != null) 
            {
                isOnline = true;
            } else {
                kickedCharacter = Character.Load(kickedId);
            }

            if (kickedCharacter == null) return;

            // Remove kicked character (if online, packet)
            kickedCharacter.Family = 0;
            family.RemoveMember(kickedCharacter);
            _familyMembers.Remove(kickedCharacter.Id);

            if (isOnline) 
            {
                kickedCharacter.SendPacket(new SCFamilyRemovedPacket(family.Id));
            }

            family.SendPacket(new SCFamilyMemberRemovedPacket(family.Id, true, kickedCharacter.Id));

            if (family.Members.Count < 2)
                DisbandFamily(family);
            else
                SaveFamily(family);
        }

        public void ChangeTitle(Character owner, uint memberId, string newTitle)
        {
            if (owner.Family == 0) return;
            Family family = _families[owner.Family];

            FamilyMember ownerMember = family.GetMember(owner);
            if (ownerMember.Role != 1) return; // Only the steward can change titles

            FamilyMember member = _familyMembers[memberId];
            member.Title = newTitle;

            family.SendPacket(new SCFamilyTitleChangedPacket(family.Id, memberId, newTitle));
        }

        public void ChangeOwner(Character previousOwner, uint memberId)
        {
            if (previousOwner.Family == 0) return;
            Family family = _families[previousOwner.Family];

            FamilyMember previousOwnerMember = family.GetMember(previousOwner);
            if (previousOwnerMember.Role != 1) return; // Only the steward can change owner

            FamilyMember member = _familyMembers[memberId];
            member.Role = 1;
            previousOwnerMember.Role = 0;

            family.SendPacket(new SCFamilyOwnerChangedPacket(family.Id, memberId));
            family.SendPacket(new SCFamilyDescPacket(family));
        }

        public Family GetFamily(uint id) 
        {
            return _families[id];
        }

        private static FamilyMember GetMemberForCharacter(Character character, byte owner, string title)
        {
            var member = new FamilyMember();
            member.Character = character;
            member.Id = character.Id;
            member.Name = character.Name;
            member.Role = owner;
            member.Title = title;
            return member;
        }
    }
}
