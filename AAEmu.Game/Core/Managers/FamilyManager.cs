using System.Collections.Generic;

using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;

using NLog;

namespace AAEmu.Game.Core.Managers;

public class FamilyManager : Singleton<FamilyManager>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private Dictionary<uint, Family> _families;
    private Dictionary<uint, FamilyMember> _familyMembers;

    /// <summary>
    /// Load family data
    /// </summary>
    public void Load()
    {
        _families = new Dictionary<uint, Family>();
        _familyMembers = new Dictionary<uint, FamilyMember>();

        Logger.Info("Loading families...");
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

                        var family = new Family() { Id = familyId };
                        _families.Add(family.Id, family);

                        using (var connection2 = MySQL.CreateConnection())
                        {
                            family.Load(connection2); // TODO : Maybe find a prettier way
                        }

                        foreach (var member in family.Members)
                            _familyMembers.Add(member.Id, member);
                    }
                }
            }
        }

        Logger.Info($"Loaded {_families.Count} families");
    }

    /// <summary>
    /// Force save all families
    /// </summary>
    public void SaveAllFamilies()
    {
        using var connection = MySQL.CreateConnection();
        using var transaction = connection.BeginTransaction();

        foreach (var family in _families.Values)
            family.Save(connection, transaction);

        transaction.Commit(); // TODO try/catch
    }

    /// <summary>
    /// Save family data
    /// </summary>
    /// <param name="family"></param>
    public static void SaveFamily(Family family)
    {
        using var connection = MySQL.CreateConnection();
        using var transaction = connection.BeginTransaction();

        family.Save(connection, transaction);

        transaction.Commit(); // TODO: try/catch
    }

    /// <summary>
    /// Sends invite request
    /// </summary>
    /// <param name="inviter"></param>
    /// <param name="invitedCharacterName"></param>
    /// <param name="title"></param>
    public static void InviteToFamily(Character inviter, string invitedCharacterName, string title)
    {
        var invited = WorldManager.Instance.GetCharacter(invitedCharacterName);
        if (invited is { Family: 0 })
            invited.SendPacket(new SCFamilyInvitationPacket(inviter.Id, inviter.Name, 1, title));
    }

    /// <summary>
    /// Handle reply from a invite request
    /// </summary>
    /// <param name="invitorId"></param>
    /// <param name="invitedChar"></param>
    /// <param name="join"></param>
    /// <param name="title"></param>
    public void ReplyToInvite(uint invitorId, Character invitedChar, bool join, string title)
    {
        if (!join)
            return;

        var invitor = WorldManager.Instance.GetCharacterById(invitorId);
        if (invitor == null) return;

        if (invitor.Family == 0)
        {
            CreateFamily(invitor, invitedChar, title);
        }
        else
        {
            var family = _families[invitor.Family];

            AddFamilyMember(family, invitedChar, title);
            family.SendPacket(new SCFamilyMemberAddedPacket(family, family.Members.Count - 1));
            SaveFamily(family);
        }
    }

    private Family CreateFamily(Character invitor, Character invitedChar, string invitedCharTitle)
    {
        var family = new Family
        {
            Id = FamilyIdManager.Instance.GetNextId()
        };

        AddFamilyMember(family, invitor);
        AddFamilyMember(family, invitedChar, invitedCharTitle);

        _families.Add(family.Id, family);

        family.SendPacket(new SCFamilyCreatedPacket(family));

        SaveFamily(family);

        return family;
    }

    /// <summary>
    /// Adds a character to a family.
    /// </summary>
    /// <param name="family">The family to add the character to.</param>
    /// <param name="character">The character to add to the family.</param>
    /// <param name="title">The title given to the character by the family owner. Only used if the character is not also the owner.</param>
    /// <remarks>
    /// If the family is empty, the first call to this method will add the character as the owner of the family.
    /// The character is joined to the family chat channel.
    /// This method does not send any packets, and no checks are made as to whether the character is already in a family.
    /// </remarks>
    private void AddFamilyMember(Family family, Character character, string title = null)
    {
        var isOwner = family.Members.Count == 0;
        var ownerFlag = (byte)(isOwner ? 1 : 0);
        if (isOwner || title == null) title = "";

        var member = GetMemberForCharacter(character, ownerFlag, title);
        family.AddMember(member);
        _familyMembers.Add(member.Id, member);
        character.Family = family.Id;

        ChatManager.Instance.GetFamilyChat(family.Id)?.JoinChannel(character);
    }

    /// <summary>
    /// Called by a character when logging in. Sends the character a family description packet. Sends every other family member an Online update packet.
    /// </summary>
    /// <param name="character"></param>
    public void OnCharacterLogin(Character character)
    {
        var family = _families.GetValueOrDefault(character.Family);
        var member = _familyMembers.GetValueOrDefault(character.Id);
        if (family == null || member == null)
        {
            // Family no longer valid
            character.Family = 0;
        }
        else
        {
            // Update Member field and send family packets
            member.Character = character;

            ChatManager.Instance.GetFamilyChat(family.Id)?.JoinChannel(character);
            character.SendPacket(new SCFamilyDescPacket(family));
            family.SendPacket(new SCFamilyMemberOnlinePacket(family.Id, member.Id, true));
        }
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

        ChatManager.Instance.GetFamilyChat(family.Id)?.LeaveChannel(character);
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
        ChatManager.Instance.GetFamilyChat(family.Id)?.LeaveChannel(character);

        if (family.Members.Count < 2)
            DisbandFamily(family);
        else
            SaveFamily(family); // TODO: need to think how to do right
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
                ChatManager.Instance.GetFamilyChat(family.Id)?.LeaveChannel(member.Character);
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
        }
        else
        {
            kickedCharacter = Character.Load(kickedId);
        }

        if (kickedCharacter == null) return;

        // Remove kicked character (if online, packet)
        kickedCharacter.Family = 0;
        family.RemoveMember(kickedCharacter);
        _familyMembers.Remove(kickedCharacter.Id);

        if (isOnline)
        {
            ChatManager.Instance.GetFamilyChat(family.Id)?.LeaveChannel(kickedCharacter);
            kickedCharacter.SendPacket(new SCFamilyRemovedPacket(family.Id));
        }

        family.SendPacket(new SCFamilyMemberRemovedPacket(family.Id, true, kickedCharacter.Id));

        if (family.Members.Count < 2)
            DisbandFamily(family);
        else
            SaveFamily(family);
    }

    /// <summary>
    /// Changes the title of a member
    /// </summary>
    /// <param name="owner"></param>
    /// <param name="memberId"></param>
    /// <param name="newTitle"></param>
    public void ChangeTitle(Character owner, uint memberId, string newTitle)
    {
        if (owner.Family == 0) return;
        var family = _families[owner.Family];

        var ownerMember = family.GetMember(owner);
        if (ownerMember.Role != 1) return; // Only the steward can change titles

        var member = _familyMembers[memberId];
        member.Title = newTitle;

        family.SendPacket(new SCFamilyTitleChangedPacket(family.Id, memberId, newTitle));
    }

    /// <summary>
    /// Changes the Steward of a Family
    /// </summary>
    /// <param name="previousOwner"></param>
    /// <param name="memberId"></param>
    public void ChangeOwner(Character previousOwner, uint memberId)
    {
        if (previousOwner.Family == 0) return;
        var family = _families[previousOwner.Family];

        var previousOwnerMember = family.GetMember(previousOwner);
        if (previousOwnerMember.Role != 1) return; // Only the steward can change owner

        var member = _familyMembers[memberId];
        member.Role = 1;
        previousOwnerMember.Role = 0;

        family.SendPacket(new SCFamilyOwnerChangedPacket(family.Id, memberId));
        family.SendPacket(new SCFamilyDescPacket(family));
    }

    /// <summary>
    /// Get Family by Id
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public Family GetFamily(uint id)
    {
        return _families[id];
    }

    /// <summary>
    /// Creates a Member object from a Character
    /// </summary>
    /// <param name="character"></param>
    /// <param name="owner">Is Owner Flag (role)</param>
    /// <param name="title"></param>
    /// <returns></returns>
    private static FamilyMember GetMemberForCharacter(Character character, byte owner, string title)
    {
        return new FamilyMember {
            Character = character,
            Id = character.Id,
            Name = character.Name,
            Role = owner,
            Title = title
        };
    }

    /// <summary>
    /// Gets FamilyId of an offline or online character
    /// </summary>
    /// <param name="characterId"></param>
    /// <returns></returns>
    public uint GetFamilyOfCharacter(uint characterId)
    {
        foreach (var family in _families.Values)
        foreach (var member in family.Members)
            if (member.Id == characterId)
                return family.Id;

        return 0;
    }
}
