using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Families;

using NLog;
using System.Text;

namespace AAEmu.Game.Core.Managers;

public class FamilyManager(IWorldManager worldManager, IChatManager chatManager, IFamilyIdManager familyIdManager,
    IFamilyPurchaseService familyPurchaseService, IGameDataManager gameDataManager) : Singleton<FamilyManager>, IFamilyManager
{
    public const long RoleChangeCooldownSeconds = 604800;
    public const int MaximumTitleUtf8Bytes = 104;
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private Dictionary<uint, Family> _families = [];
    private Dictionary<uint, FamilyMember> _familyMembers = [];
    private readonly Dictionary<uint, PendingFamilyInvitation> _pendingInvitations = [];
    private readonly object _familyMutationLock = new();
    private readonly Action<Family> _persistFamily = SaveFamily;
    private readonly Func<long> _unixTime = () => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    private readonly Func<Character, bool> _isCurrentSession = character =>
        character?.Connection != null &&
        ReferenceEquals(character.Connection.ActiveChar, character) &&
        ReferenceEquals(worldManager.GetCharacterById(character.Id), character);
    // Ordering-only dependency: Family.Load reads HeirGameData while constructing its offline roster.
    private readonly IGameDataManager _gameDataManager = gameDataManager;

    public FamilyManager(IWorldManager worldManager, IChatManager chatManager, IFamilyIdManager familyIdManager)
        : this(worldManager, chatManager, familyIdManager, null, null)
    {
    }

    public FamilyManager(IWorldManager worldManager, IChatManager chatManager, IFamilyIdManager familyIdManager,
        IFamilyPurchaseService familyPurchaseService)
        : this(worldManager, chatManager, familyIdManager, familyPurchaseService, null)
    {
    }

    private sealed record PendingFamilyInvitation(Character Inviter, Character Invitee, uint FamilyId, string Title);

    private sealed class PersistenceOperation : IDisposable
    {
        private readonly bool _ownsGate;

        public PersistenceOperation()
        {
            _ownsGate = !PersistenceGate.IsOperationHeld && !PersistenceGate.IsSaveHeld;
            if (_ownsGate)
                PersistenceGate.EnterOperation();
        }

        public void Dispose()
        {
            if (_ownsGate)
                PersistenceGate.ExitOperation();
        }
    }

    public IDisposable AcquireCharacterDeletionLock()
    {
        Monitor.Enter(_familyMutationLock);
        return new FamilyDeletionLease(_familyMutationLock);
    }

    public bool CanDeleteCharacterLocked(uint characterId, uint storedFamilyId)
    {
        if (!_familyMembers.TryGetValue(characterId, out var member))
            return storedFamilyId == 0;
        var family = _families.Values.FirstOrDefault(candidate => candidate.Members.Contains(member));
        return family != null && family.Id == storedFamilyId && member.Role != 1;
    }

    private sealed class FamilyDeletionLease(object sync) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Monitor.Exit(sync);
        }
    }

    internal FamilyManager(IWorldManager worldManager, IChatManager chatManager, IFamilyIdManager familyIdManager,
        Action<Family> persistFamily) : this(worldManager, chatManager, familyIdManager)
    {
        _persistFamily = persistFamily;
    }

    internal FamilyManager(IWorldManager worldManager, IChatManager chatManager, IFamilyIdManager familyIdManager,
        IFamilyPurchaseService familyPurchaseService, Action<Family> persistFamily, Func<long> unixTime = null,
        Func<Character, bool> isCurrentSession = null)
        : this(worldManager, chatManager, familyIdManager, familyPurchaseService, null)
    {
        _persistFamily = persistFamily;
        _unixTime = unixTime ?? _unixTime;
        _isCurrentSession = isCurrentSession ?? _isCurrentSession;
    }

    /// <summary>
    /// Load family data
    /// </summary>
    public void Load()
    {
        _families = [];
        _familyMembers = [];

        Logger.Info("Loading families...");
        using (var connection = MySQL.CreateConnection())
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT id FROM families";
                command.Prepare();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var familyId = reader.GetUInt32("id");
                        if (familyId == 0)
                            continue;

                        var family = new Family { Id = familyId };
                        _families.Add(family.Id, family);

                        using (var connection2 = MySQL.CreateConnection())
                        {
                            family.Load(connection2); // TODO : Maybe find a prettier way
                        }

                        if (family.Members.Count == 0)
                        {
                            Logger.Warn("Ignoring orphan family aggregate {0}; no character currently references its roster", family.Id);
                            _families.Remove(family.Id);
                            continue;
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
        lock (_familyMutationLock)
        {
        using var connection = MySQL.CreateConnection();
        using var transaction = connection.BeginTransaction();
        try
        {
            foreach (var family in _families.Values)
                family.Save(connection, transaction);

            transaction.Commit();
            foreach (var family in _families.Values)
                family.ConfirmSave();
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            Logger.Error(ex, "Failed to save all families; transaction rolled back");
            throw;
        }
        }
    }

    /// <summary>
    /// Save family data
    /// </summary>
    /// <param name="family"></param>
    public static void SaveFamily(Family family)
    {
        using var connection = MySQL.CreateConnection();
        using var transaction = connection.BeginTransaction();
        try
        {
            family.Save(connection, transaction);
            transaction.Commit();
            family.ConfirmSave();
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            Logger.Error(ex, "Failed to save family {0}; transaction rolled back", family.Id);
            throw;
        }
    }

    /// <summary>
    /// Sends invite request
    /// </summary>
    /// <param name="inviter"></param>
    /// <param name="invitedCharacterName"></param>
    /// <param name="title"></param>
    public void InviteToFamily(Character inviter, string invitedCharacterName, string title)
    {
        Character invitedForPublication = null;
        string invitationTitle = null;
        uint invitationFamilyId = 0;
        FamilyPurchaseResult purchase = default;
        using (var persistence = new PersistenceOperation())
        {
        lock (_familyMutationLock)
        {
        if (!_isCurrentSession(inviter))
            return;
        var invited = worldManager.GetCharacter(invitedCharacterName);
        var now = _unixTime();
        title ??= string.Empty;
        if (invited == null || !_isCurrentSession(invited) || invited.Id == inviter.Id || invited.Family != 0 ||
            invited.FamilyRejoinUntil > now ||
            inviter.Family == 0 && inviter.FamilyRejoinUntil > now ||
            Encoding.UTF8.GetByteCount(title) > MaximumTitleUtf8Bytes)
            return;
        if (_pendingInvitations.ContainsKey(invited.Id))
            return;

        if (inviter.Family != 0)
        {
            if (!_families.TryGetValue(inviter.Family, out var family) ||
                family.GetMember(inviter)?.Role != 1 || family.Members.Count >= family.MemberLimit)
                return;
        }

        if (familyPurchaseService != null)
        {
            purchase = familyPurchaseService.ConsumeInvitation(inviter);
            if (!purchase.Success)
                return;
        }

        _pendingInvitations[invited.Id] = new PendingFamilyInvitation(inviter, invited, inviter.Family, title);
        invitedForPublication = invited;
        invitationTitle = title;
        invitationFamilyId = inviter.Family;
        }
        }
        purchase.PublishDeferred();
        invitedForPublication.SendPacket(new SCFamilyInvitationPacket(
            inviter.Id, inviter.Name, invitationFamilyId, invitationTitle));
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
        using var persistence = new PersistenceOperation();
        lock (_familyMutationLock)
        {
        if (!_isCurrentSession(invitedChar))
            return;
        if (!_pendingInvitations.TryGetValue(invitedChar.Id, out var invitation) || invitation.Inviter.Id != invitorId ||
            !ReferenceEquals(invitation.Invitee, invitedChar))
            return;
        _pendingInvitations.Remove(invitedChar.Id);

        var now = _unixTime();
        if (!join || invitedChar.Family != 0 || invitedChar.FamilyRejoinUntil > now)
            return;

        var invitor = worldManager.GetCharacterById(invitorId);
        if (invitor == null || !_isCurrentSession(invitor) || !ReferenceEquals(invitation.Inviter, invitor) ||
            invitor.Family != invitation.FamilyId ||
            invitor.Family == 0 && invitor.FamilyRejoinUntil > now)
            return;

        if (invitor.Family == 0)
        {
            CreateFamily(invitor, invitedChar, invitation.Title);
        }
        else
        {
            if (!_families.TryGetValue(invitor.Family, out var family) ||
                family.GetMember(invitor)?.Role != 1 || family.Members.Count >= family.MemberLimit)
                return;

            AddFamilyMember(family, invitedChar, invitation.Title);
            try
            {
                _persistFamily(family);
            }
            catch
            {
                family.RemoveMember(invitedChar);
                _familyMembers.Remove(invitedChar.Id);
                invitedChar.Family = 0;
                throw;
            }
            invitedChar.FamilyRejoinUntil = 0;
            chatManager.GetFamilyChat(family.Id)?.JoinChannel(invitedChar);
            ApplyLevelBuff(invitedChar, family.Level);
            family.SendPacket(new SCFamilyMemberAddedPacket(family, (uint)(family.Members.Count - 1)));
        }
        }
    }

    private Family CreateFamily(Character invitor, Character invitedChar, string invitedCharTitle)
    {
        var family = new Family
        {
            Id = familyIdManager.GetNextId()
        };

        AddFamilyMember(family, invitor);
        AddFamilyMember(family, invitedChar, invitedCharTitle);

        _families.Add(family.Id, family);

        try
        {
            _persistFamily(family);
        }
        catch
        {
            _families.Remove(family.Id);
            foreach (var member in family.Members)
            {
                _familyMembers.Remove(member.Id);
                if (member.Character != null)
                {
                    member.Character.Family = 0;
                    chatManager.GetFamilyChat(family.Id)?.LeaveChannel(member.Character);
                }
            }
            throw;
        }
        foreach (var member in family.Members)
            if (member.Character != null)
            {
                member.Character.FamilyRejoinUntil = 0;
                chatManager.GetFamilyChat(family.Id)?.JoinChannel(member.Character);
                ApplyLevelBuff(member.Character, family.Level);
            }
        family.SendPacket(new SCFamilyCreatedPacket(family));

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

    }

    /// <summary>
    /// Called by a character when logging in. Sends the character a family description packet. Sends every other family member an Online update packet.
    /// </summary>
    /// <param name="character"></param>
    public void OnCharacterLogin(Character character)
    {
        Family familyForPublication = null;
        FamilyMember memberForPublication = null;
        using (var persistence = new PersistenceOperation())
        {
        lock (_familyMutationLock)
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
            member.Level = character.Level;
            member.HeirLevel = character.HeirLevel;
            var now = _unixTime();
            if (FamilyProgressionRules.IsNewUtcDay(member.LoginRewardTime, now))
            {
                var oldExp = family.Exp;
                var oldLoginRewardTime = member.LoginRewardTime;
                family.Exp = uint.MaxValue - family.Exp < FamilyContentConfig.LoginExp
                    ? uint.MaxValue
                    : family.Exp + FamilyContentConfig.LoginExp;
                member.LoginRewardTime = now;
                try { _persistFamily(family); }
                catch (Exception ex)
                {
                    family.Exp = oldExp;
                    member.LoginRewardTime = oldLoginRewardTime;
                    Logger.Error(ex, "Failed to persist family login experience for character {0}", character.Id);
                }
            }

            familyForPublication = SnapshotForPacket(family);
            memberForPublication = member;
        }
        }
        }
        if (familyForPublication == null)
            return;
        using var publicationPersistence = new PersistenceOperation();
        lock (_familyMutationLock)
        {
            if (!_families.TryGetValue(familyForPublication.Id, out var currentFamily) ||
                !_familyMembers.TryGetValue(character.Id, out var currentMember) ||
                !ReferenceEquals(currentMember, memberForPublication) ||
                !ReferenceEquals(currentMember.Character, character) || character.Family != currentFamily.Id)
                return;
            familyForPublication = SnapshotForPacket(currentFamily);
            chatManager.GetFamilyChat(currentFamily.Id)?.JoinChannel(character);
            ApplyLevelBuff(character, currentFamily.Level);
            character.SendPacket(new SCFamilyDescPacket(familyForPublication));
            familyForPublication.SendPacket(new SCFamilyMemberOnlinePacket(
                currentFamily.Id, currentMember.Id, true, character.Level, character.HeirLevel));
        }
    }

    /// <summary>
    /// Called when a player logs out. Sends an update to every family member to mark him as offline.
    /// </summary>
    /// <param name="character"></param>
    public void OnCharacterLogout(Character character)
    {
        Family familyForPublication = null;
        FamilyMember memberForPublication = null;
        using (var persistence = new PersistenceOperation())
        {
        lock (_familyMutationLock)
        {
        if (_pendingInvitations.TryGetValue(character.Id, out var receivedInvitation) &&
            ReferenceEquals(receivedInvitation.Invitee, character))
            _pendingInvitations.Remove(character.Id);
        foreach (var inviteeId in _pendingInvitations.Where(x => ReferenceEquals(x.Value.Inviter, character)).Select(x => x.Key).ToArray())
            _pendingInvitations.Remove(inviteeId);

        if (!_families.TryGetValue(character.Family, out var family))
            return;

        var member = family.GetMember(character);
        if (member == null || !ReferenceEquals(member.Character, character))
            return;

        member.Character = null;
        familyForPublication = SnapshotForPacket(family);
        memberForPublication = member;
        }
        }
        lock (_familyMutationLock)
        {
            if (!_families.TryGetValue(familyForPublication.Id, out var currentFamily) ||
                !_familyMembers.TryGetValue(character.Id, out var currentMember) ||
                !ReferenceEquals(currentMember, memberForPublication) || currentMember.Character != null)
                return;
            familyForPublication = SnapshotForPacket(currentFamily);
            chatManager.GetFamilyChat(currentFamily.Id)?.LeaveChannel(character);
            familyForPublication.SendPacket(new SCFamilyMemberOnlinePacket(
                currentFamily.Id, character.Id, false, currentMember.Level,
                currentMember.HeirLevel), character.Id);
        }
    }

    /// <summary>Refreshes cached roster levels after a normal or ancestral level change.</summary>
    public void OnCharacterRefresh(Character character)
    {
        using var persistence = new PersistenceOperation();
        lock (_familyMutationLock)
        {
            if (!_families.TryGetValue(character.Family, out var family) ||
                !_familyMembers.TryGetValue(character.Id, out var member) ||
                !ReferenceEquals(member.Character, character) || family.GetMember(character) != member)
                return;
            if (member.Level == character.Level && member.HeirLevel == character.HeirLevel)
                return;

            member.Level = character.Level;
            member.HeirLevel = character.HeirLevel;
            family.SendPacket(new SCFamilyChangeMemberLevelPacket(
                unchecked((int)family.Id), member.Id, unchecked((sbyte)member.Level), unchecked((sbyte)member.HeirLevel)));
        }
    }

    /// <summary>Sends family chat only while the sender remains an authoritative member.</summary>
    public bool SendChatMessage(Character character, string message, int ability, byte languageType)
    {
        using var persistence = new PersistenceOperation();
        lock (_familyMutationLock)
        {
            if (!_isCurrentSession(character))
                return false;
            if (!_families.TryGetValue(character.Family, out var family) ||
                !_familyMembers.TryGetValue(character.Id, out var member) ||
                !ReferenceEquals(member.Character, character) || family.GetMember(character) != member)
                return false;
            chatManager.GetFamilyChat(family.Id)?.SendMessage(character, message, ability, languageType);
            return true;
        }
    }

    /// <summary>
    /// Called when a player wants to leave a family. Charges the configured certificate to the leaving member,
    /// removes them from the family in the same transaction, and updates the remaining members.
    /// </summary>
    /// <param name="character"></param>
    public void LeaveFamily(Character character)
        => RemoveFamilyMember(character, requireCurrentSession: true);

    /// <summary>Removes a character during the serialized durable character-deletion workflow. No certificate is charged.</summary>
    public void RemoveDeletedCharacter(Character character)
        => RemoveFamilyMember(character, requireCurrentSession: false);

    private void RemoveFamilyMember(Character character, bool requireCurrentSession)
    {
        var familyId = character.Family;
        List<Character> disbandedMembers = null;
        Family changedFamily = null;
        FamilyPurchaseResult purchase = default;
        using (var persistence = new PersistenceOperation())
        {
        lock (_familyMutationLock)
        {
        if (requireCurrentSession && !_isCurrentSession(character))
            return;
        if (!_families.TryGetValue(character.Family, out var family))
            return;

        var leavingMember = family.GetMember(character);
        if (leavingMember == null || leavingMember.Role == 1 && family.Members.Count > 2)
            return;

        // A voluntary leave pays the certificate; the character-deletion workflow does not.
        var payer = requireCurrentSession ? character : null;
        var oldRejoinUntil = character.FamilyRejoinUntil;
        character.Family = 0;
        character.FamilyRejoinUntil = _unixTime() + FamilyContentConfig.RejoinDelaySeconds;
        family.RemovedMemberRejoinUntil = character.FamilyRejoinUntil;
        family.RemoveMember(leavingMember);
        _familyMembers.Remove(character.Id);
        void Restore()
        {
            family.RestoreMember(leavingMember);
            _familyMembers[leavingMember.Id] = leavingMember;
            character.Family = family.Id;
            character.FamilyRejoinUntil = oldRejoinUntil;
        }

        bool persisted;
        try { persisted = TryPersistRemoval(family, payer, out purchase, out disbandedMembers); }
        catch
        {
            Restore();
            throw;
        }
        if (!persisted)
        {
            Restore();
            return;
        }
        if (disbandedMembers == null)
            changedFamily = SnapshotForPacket(family);
        character.SendPacket(new SCFamilyRemovedPacket(family.Id));
        family.SendPacket(new SCFamilyMemberRemovedPacket(family.Id, false, character.Id));
        chatManager.GetFamilyChat(family.Id)?.LeaveChannel(character);
        RemoveLevelBuff(character);
        }
        }
        purchase.PublishDeferred();
        PublishDisband(familyId, disbandedMembers);
        if (changedFamily != null)
            PublishExperience(changedFamily.Id, character.Id);
    }

    /// <summary>
    /// Persists a removal that is already applied to the roster. A family left with fewer than two members is
    /// disbanded; otherwise the configured departure EXP loss is applied. When a payer is given, the
    /// configured certificate is consumed in the same transaction. Returns false, or throws, with the family
    /// EXP unchanged when nothing was persisted; the caller restores the removed member.
    /// </summary>
    private bool TryPersistRemoval(Family family, Character payer, out FamilyPurchaseResult purchase,
        out List<Character> disbandedMembers)
    {
        disbandedMembers = null;
        if (family.Members.Count < 2)
            return TryDisbandFamily(family, payer, out purchase, out disbandedMembers);

        var oldExp = family.Exp;
        family.Exp = FamilyProgressionRules.ApplyDepartureExperienceLoss(
            family.Exp, FamilyContentConfig.LeaveExpPercent);
        bool persisted;
        try { persisted = TryPersistDeparture(payer, family, out purchase); }
        catch
        {
            family.Exp = oldExp;
            throw;
        }
        if (!persisted)
            family.Exp = oldExp;
        return persisted;
    }

    private bool TryPersistDeparture(Character payer, Family family, out FamilyPurchaseResult purchase)
    {
        purchase = default;
        if (payer == null || familyPurchaseService == null)
        {
            _persistFamily(family);
            return true;
        }

        purchase = familyPurchaseService.ConsumeDeparture(payer, family);
        return purchase.Success;
    }

    /// <summary>
    /// Called when a family is disbanded (when they have less than 2 members)
    /// </summary>
    private bool TryDisbandFamily(Family family, Character payer, out FamilyPurchaseResult purchase,
        out List<Character> removedOnlineMembers)
    {
        removedOnlineMembers = null;
        var removedMembers = family.Members.ToArray();
        for (var i = family.Members.Count - 1; i > -1; i--)
        {
            var member = family.Members[i];
            family.RemoveMember(member);
            _familyMembers.Remove(member.Id);
        }
        void Restore()
        {
            foreach (var member in removedMembers)
            {
                family.RestoreMember(member);
                _familyMembers[member.Id] = member;
            }
        }

        bool persisted;
        try { persisted = TryPersistDeparture(payer, family, out purchase); }
        catch
        {
            Restore();
            throw;
        }
        if (!persisted)
        {
            Restore();
            return false;
        }
        _families.Remove(family.Id);
        var online = removedMembers.Where(x => x.Character != null).Select(x => x.Character).ToList();
        foreach (var character in online)
        {
            character.Family = 0;
            character.FamilyRejoinUntil = family.RemovedMemberRejoinUntil;
        }
        removedOnlineMembers = online;
        return true;
    }

    private void PublishDisband(uint familyId, IReadOnlyList<Character> members)
    {
        if (members == null)
            return;
        using var persistence = new PersistenceOperation();
        lock (_familyMutationLock)
        {
            var removed = new SCFamilyRemovedPacket(familyId);
            foreach (var member in members)
            {
                if (member.Family != 0)
                    continue;
                chatManager.GetFamilyChat(familyId)?.LeaveChannel(member);
                member.SendPacket(removed);
                RemoveLevelBuff(member);
            }
        }
    }

    /// <summary>
    /// Called when a family member is kicked. Charges the configured certificate to the owner in the same
    /// transaction as the removal. Disbands the family if it has 2 members.
    /// </summary>
    /// <param name="kicker"></param>
    /// <param name="kickedId"></param>
    public void KickMember(Character kicker, uint kickedId)
    {
        var familyId = kicker.Family;
        List<Character> disbandedMembers = null;
        Family changedFamily = null;
        FamilyPurchaseResult purchase = default;
        using (var persistence = new PersistenceOperation())
        {
        lock (_familyMutationLock)
        {
        if (!_isCurrentSession(kicker))
            return;
        if (kicker.Family == 0) return;
        if (!_families.TryGetValue(kicker.Family, out var family)) return;

        var kickerMember = family.GetMember(kicker);
        if (kickerMember?.Role != 1) return; // Only the steward can kick

        var kickedMember = family.Members.FirstOrDefault(x => x.Id == kickedId);
        if (kickedMember == null || kickedMember.Id == kicker.Id) return;

        var kickedCharacter = worldManager.GetCharacterById(kickedId);
        var isOnline = kickedCharacter != null && ReferenceEquals(kickedMember.Character, kickedCharacter);
        var oldRejoinUntil = isOnline ? kickedCharacter.FamilyRejoinUntil : 0;
        var rejoinUntil = _unixTime() + FamilyContentConfig.RejoinDelaySeconds;
        family.RemovedMemberRejoinUntil = rejoinUntil;
        if (isOnline)
        {
            kickedCharacter.Family = 0;
            kickedCharacter.FamilyRejoinUntil = rejoinUntil;
        }
        family.RemoveMember(kickedMember);
        _familyMembers.Remove(kickedMember.Id);
        void Restore()
        {
            family.RestoreMember(kickedMember);
            _familyMembers[kickedMember.Id] = kickedMember;
            if (!isOnline)
                return;
            kickedCharacter.Family = family.Id;
            kickedCharacter.FamilyRejoinUntil = oldRejoinUntil;
        }

        bool persisted;
        try { persisted = TryPersistRemoval(family, kicker, out purchase, out disbandedMembers); }
        catch
        {
            Restore();
            throw;
        }
        if (!persisted)
        {
            Restore();
            return;
        }
        if (disbandedMembers == null)
            changedFamily = SnapshotForPacket(family);
        if (isOnline)
        {
            chatManager.GetFamilyChat(family.Id)?.LeaveChannel(kickedCharacter);
            kickedCharacter.SendPacket(new SCFamilyRemovedPacket(family.Id));
            RemoveLevelBuff(kickedCharacter);
        }
        family.SendPacket(new SCFamilyMemberRemovedPacket(family.Id, true, kickedMember.Id));
        }
        }
        purchase.PublishDeferred();
        PublishDisband(familyId, disbandedMembers);
        if (changedFamily != null)
            PublishExperience(changedFamily.Id, kickedId);
    }

    /// <summary>
    /// Changes the title of a member
    /// </summary>
    /// <param name="owner"></param>
    /// <param name="memberId"></param>
    /// <param name="newTitle"></param>
    public void ChangeTitle(Character owner, uint memberId, string newTitle)
    {
        if (newTitle == null || Encoding.UTF8.GetByteCount(newTitle) > MaximumTitleUtf8Bytes)
            return;
        using var persistence = new PersistenceOperation();
        lock (_familyMutationLock)
        {
        if (!_isCurrentSession(owner))
            return;
        if (owner.Family == 0) return;
        if (!_families.TryGetValue(owner.Family, out var family)) return;

        var ownerMember = family.GetMember(owner);
        if (ownerMember?.Role != 1) return; // Only the steward can change titles

        var member = family.Members.FirstOrDefault(x => x.Id == memberId);
        if (member == null || member.Role == 1) return;
        var oldTitle = member.Title;
        member.Title = newTitle;
        try { _persistFamily(family); }
        catch { member.Title = oldTitle; throw; }
        family.SendPacket(new SCFamilyTitleChangedPacket(family.Id, memberId, newTitle));
        }
    }

    /// <summary>
    /// Changes the Steward of a Family
    /// </summary>
    /// <param name="previousOwner"></param>
    /// <param name="memberId"></param>
    public void ChangeOwner(Character previousOwner, uint memberId)
    {
        using var persistence = new PersistenceOperation();
        lock (_familyMutationLock)
        {
        if (!_isCurrentSession(previousOwner))
            return;
        if (previousOwner.Family == 0) return;
        if (!_families.TryGetValue(previousOwner.Family, out var family)) return;

        var previousOwnerMember = family.GetMember(previousOwner);
        if (previousOwnerMember?.Role != 1) return; // Only the steward can change owner

        var member = family.Members.FirstOrDefault(x => x.Id == memberId);
        if (member == null || member.Id == previousOwner.Id || member.Role == 1) return;
        var oldMemberRole = member.Role;
        member.Role = 1;
        previousOwnerMember.Role = 0;
        try { _persistFamily(family); }
        catch
        {
            member.Role = oldMemberRole;
            previousOwnerMember.Role = 1;
            throw;
        }
        family.SendPacket(new SCFamilyOwnerChangedPacket(family.Id, memberId, previousOwner.Id));
        family.SendPacket(new SCFamilyDescPacket(family));
        }
    }

    public void SetName(Character owner, string name)
    {
        Family renamedFamily = null;
        FamilyPurchaseResult purchase = default;
        name = name?.Trim();
        using (var persistence = new PersistenceOperation())
        {
        lock (_familyMutationLock)
        {
            if (!TryGetOwnedFamily(owner, out var family) || !IsValidFamilyName(name) ||
                string.Equals(family.Name, name, StringComparison.Ordinal))
                return;

            if (!string.IsNullOrEmpty(family.Name))
            {
                var now = _unixTime();
                if (family.ChangeNameTime > 0 && now - family.ChangeNameTime < FamilyContentConfig.NameChangeDelaySeconds)
                    return;
                purchase = familyPurchaseService?.Rename(owner, family, name, now) ?? default;
                if (!purchase.Success)
                    return;
                renamedFamily = SnapshotForPacket(family);
            }
            else
            {
                var oldName = family.Name;
                family.Name = name;
                try { _persistFamily(family); }
                catch { family.Name = oldName; throw; }
                renamedFamily = SnapshotForPacket(family);
            }
        }
        }
        purchase.PublishDeferred();
        if (renamedFamily != null)
            PublishDescriptor(renamedFamily.Id);
    }

    public void IncreaseMemberLimit(Character owner)
    {
        Family expandedFamily = null;
        FamilyPurchaseResult purchase = default;
        using (var persistence = new PersistenceOperation())
        {
        lock (_familyMutationLock)
        {
            if (!TryGetOwnedFamily(owner, out var family) || familyPurchaseService == null)
                return;
            var next = FamilyGameData.Instance.GetNextMemberLimit(family.MemberLimit);
            if (next == null || next.Count != family.MemberLimit + 1 || next.ItemId == 0 || next.ItemCount <= 0)
                return;
            purchase = familyPurchaseService.Expand(owner, family, next.ItemId, next.ItemCount);
            if (!purchase.Success)
                return;
            expandedFamily = SnapshotForPacket(family);
        }
        }

        purchase.PublishDeferred();
        PublishDescriptor(expandedFamily.Id);
    }

    public void SetNotice(Character owner, string notice)
    {
        Family changedFamily = null;
        using (var persistence = new PersistenceOperation())
        {
        lock (_familyMutationLock)
        {
            if (!TryGetOwnedFamily(owner, out var family) || notice == null || Encoding.UTF8.GetByteCount(notice) > 800)
                return;
            var oldNotice = family.Notice;
            family.Notice = notice;
            try { _persistFamily(family); }
            catch { family.Notice = oldNotice; throw; }
            changedFamily = SnapshotForPacket(family);
        }
        }
        if (changedFamily != null)
            PublishDescriptor(changedFamily.Id);
    }

    public void ChangeMemberRole(Character owner, uint memberId, uint roleId)
    {
        Family changedFamily = null;
        using (var persistence = new PersistenceOperation())
        {
        lock (_familyMutationLock)
        {
            if (!TryGetOwnedFamily(owner, out var family) || roleId == 0 || roleId > byte.MaxValue)
                return;
            var role = FamilyGameData.Instance.GetRole(roleId);
            var member = family.Members.FirstOrDefault(x => x.Id == memberId);
            if (role == null || member == null || member.Role == 1 || roleId == 1 ||
                family.Members.Count(x => x.Role == roleId) >= role.RoleCount)
                return;
            var oldRole = member.Role;
            var oldRoleUpdateTime = member.RoleUpdateTime;
            var now = _unixTime();
            if (!FamilyProgressionRules.CanChangeRole(member.RoleUpdateTime, now, RoleChangeCooldownSeconds))
                return;
            member.Role = (byte)roleId;
            member.RoleUpdateTime = now;
            try { _persistFamily(family); }
            catch
            {
                member.Role = oldRole;
                member.RoleUpdateTime = oldRoleUpdateTime;
                throw;
            }
            changedFamily = SnapshotForPacket(family);
        }
        }
        if (changedFamily != null)
            PublishMemberRole(changedFamily.Id, memberId);
    }

    public void AddExperience(Character source, uint amount)
    {
        Family changedFamily = null;
        using (var persistence = new PersistenceOperation())
        {
        lock (_familyMutationLock)
        {
            if (!_families.TryGetValue(source.Family, out var family) || family.GetMember(source) == null || amount == 0)
                return;
            var oldExp = family.Exp;
            family.Exp = uint.MaxValue - family.Exp < amount ? uint.MaxValue : family.Exp + amount;
            try { _persistFamily(family); }
            catch { family.Exp = oldExp; throw; }
            changedFamily = SnapshotForPacket(family);
        }
        }
        PublishExperience(changedFamily.Id, source.Id, source, amount);
    }

    public bool TryLevelUp(Character owner, uint targetLevel)
    {
        Family changedFamily = null;
        using (var persistence = new PersistenceOperation())
        {
        lock (_familyMutationLock)
        {
            if (!TryGetOwnedFamily(owner, out var family) || targetLevel != family.Level + 1)
                return false;
            if (targetLevel > FamilyContentConfig.MaximumLevel)
                return false;
            var target = FamilyGameData.Instance.GetLevel(targetLevel);
            if (target == null || family.Exp < target.Exp)
                return false;
            var oldLevel = family.Level;
            family.Level = targetLevel;
            try { _persistFamily(family); }
            catch { family.Level = oldLevel; throw; }
            foreach (var member in family.Members)
                if (member.Character != null)
                    ApplyLevelBuff(member.Character, family.Level);
            changedFamily = SnapshotForPacket(family);
        }
        }
        PublishDescriptor(changedFamily.Id);
        return true;
    }

    private void PublishDescriptor(uint familyId)
    {
        using var persistence = new PersistenceOperation();
        lock (_familyMutationLock)
            if (_families.TryGetValue(familyId, out var family))
                family.SendPacket(new SCFamilyDescPacket(family));
    }

    private void PublishMemberRole(uint familyId, uint memberId)
    {
        using var persistence = new PersistenceOperation();
        lock (_familyMutationLock)
            if (_families.TryGetValue(familyId, out var family) &&
                family.Members.FirstOrDefault(x => x.Id == memberId) is { } member)
                family.SendPacket(new SCFamilyChangeMemberRolePacket(
                    unchecked((int)family.Id), member.Id, member.Role));
    }

    private void PublishExperience(uint familyId, uint sourceId, Character drySource = null, uint dryAmount = 0)
    {
        using var persistence = new PersistenceOperation();
        lock (_familyMutationLock)
        {
            if (!_families.TryGetValue(familyId, out var family))
                return;
            if (drySource != null && ReferenceEquals(family.GetMember(drySource)?.Character, drySource))
                drySource.SendPacket(new SCFamilyExpChangeDryNotifyPacket(dryAmount));
            family.SendPacket(new SCFamilyExpChangeNotifyPacket(
                unchecked((int)family.Id), sourceId, family.Level, family.Exp));
        }
    }

    private bool TryGetOwnedFamily(Character owner, out Family family)
    {
        if (_isCurrentSession(owner) && _families.TryGetValue(owner.Family, out family) &&
            family.GetMember(owner)?.Role == 1)
            return true;
        family = null;
        return false;
    }

    private static bool IsValidFamilyName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;
        var runes = name.EnumerateRunes().ToArray();
        if (runes.Length > 12 || runes.Any(x => !Rune.IsLetter(x) && x.Value != ' '))
            return false;
        var hasEnglish = runes.Any(x => x.Value is >= 'A' and <= 'Z' or >= 'a' and <= 'z');
        var hasLocal = runes.Any(x => Rune.IsLetter(x) && x.Value > 0x7f);
        if (hasEnglish && hasLocal)
            return false;
        return runes.Length >= (hasEnglish ? 3 : 2);
    }

    private static void ApplyLevelBuff(Character character, uint level)
    {
        RemoveLevelBuff(character);
        var buffId = FamilyGameData.Instance.GetLevel(level)?.BuffId ?? 0;
        if (buffId != 0)
            character.Buffs.AddBuff(buffId, character);
    }

    private static void RemoveLevelBuff(Character character)
    {
        for (uint level = 1; level <= FamilyGameData.Instance.MaxLevel; level++)
        {
            var buffId = FamilyGameData.Instance.GetLevel(level)?.BuffId ?? 0;
            if (buffId != 0)
                character.Buffs.RemoveBuff(buffId);
        }
    }

    private static Family SnapshotForPacket(Family family)
    {
        var snapshot = new Family
        {
            Id = family.Id,
            Name = family.Name,
            Notice = family.Notice,
            Level = family.Level,
            Exp = family.Exp,
            IncreasedMemberCount = family.IncreasedMemberCount,
            ResetTime = family.ResetTime,
            ChangeNameTime = family.ChangeNameTime
        };
        foreach (var (type, endTime) in family.ActSanctions)
            snapshot.ActSanctions[type] = endTime;
        foreach (var member in family.Members)
        {
            snapshot.AddMember(new FamilyMember
            {
                Character = member.Character,
                Id = member.Id,
                Name = member.Name,
                Level = member.Character?.Level ?? member.Level,
                HeirLevel = member.Character?.HeirLevel ?? member.HeirLevel,
                Role = member.Role,
                Title = member.Title,
                RoleUpdateTime = member.RoleUpdateTime,
                LoginRewardTime = member.LoginRewardTime
            });
        }
        return snapshot;
    }

    /// <summary>
    /// Get Family by Id
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public Family GetFamily(uint id)
    {
        lock (_familyMutationLock)
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
        return new FamilyMember
        {
            Character = character,
            Id = character.Id,
            Name = character.Name,
            Level = character.Level,
            HeirLevel = character.HeirLevel,
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
        lock (_familyMutationLock)
        {
        foreach (var family in _families.Values)
            foreach (var member in family.Members)
                if (member.Id == characterId)
                    return family.Id;

        return 0;
        }
    }
}
