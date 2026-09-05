using System.Linq;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;
using MySql.Data.MySqlClient;

namespace AAEmu.Game.Models.Game.Expeditions;

public class Expedition : SystemFaction
{
    private readonly List<uint> _removedMembers = [];

    public List<ExpeditionMember> Members { get; set; } = [];
    public List<ExpeditionRolePolicy> Policies { get; set; } = [];

    /// <summary>Guild level shown in the info panel - not tied to any progression system yet, starts at 1.</summary>
    public uint Level { get; set; } = 1;
    public uint Exp { get; set; }
    public string Notice { get; set; } = string.Empty;

    /// <summary>House.Id of this guild's placed Guild Residence (item_housings designs 830/831/832), or 0 if none placed yet - one per guild regardless of color/design.</summary>
    public uint ResidenceHouseId { get; set; }

    /// <summary>Recruitment-board interest bitmask shown as icons in the info panel and set via CSExpeditionInterestUpatePacket (X2Faction:SetMyExpeditionInterest). Carried by SCExpeditionDescPacket's "interest" field - previously always hardcoded to 0 since nothing could set it.</summary>
    public short Interest { get; set; }

    /// <summary>Highest purchased grade per expedition_buffs.id (prestige-shop perks), keyed by expedition_buff_id. Loaded from/persisted to expedition_buff_purchases.</summary>
    public Dictionary<uint, byte> PurchasedBuffGrades { get; set; } = [];

    /// <summary>
    /// Guild-level pooled prestige shown in the overview panel and checked by the prestige-shop
    /// affordability gate - computed live as the sum of every member's own ContributionPoint rather than
    /// a separately persisted field, so it can never drift out of sync with the member list.
    /// </summary>
    public uint TotalContributionPoint
    {
        get
        {
            var total = Members.Sum(m => (long)m.ContributionPoint);
            return (uint)Math.Clamp(total, 0, uint.MaxValue);
        }
    }

    /// <summary>
    /// Guild War state. Protection is a single blanket flag, not paired to a specific enemy, and covers
    /// two sources: the post-war cooldown, and the Ceasefire Agreement item (id 52121) - see
    /// ProtectionForExpedition.cs.
    /// </summary>
    public uint WarEnemyExpeditionId { get; set; }
    public DateTime? WarDeclaredAt { get; set; }
    /// <summary>While the war is active this is the scheduled end date; once it ends this is cleared.</summary>
    public DateTime? WarEndsAt { get; set; }
    /// <summary>Blanket "cannot declare or be declared upon" deadline - post-war cooldown or Ceasefire item.</summary>
    public DateTime? WarProtectedUntil { get; set; }
    /// <summary>Kills credited to this expedition's members against the current/last war's enemy.</summary>
    public uint WarKillScore { get; set; }

    /// <summary>Per-member kill breakdown for the current war (characterId -> kills). In-memory only,
    /// reset at DeclareWar - a mid-war restart keeps WarKillScore but loses this breakdown. Drives the
    /// per-row "kills" column of the war scoreboard (SCExpeditionWarKillScorePacket).</summary>
    public Dictionary<uint, uint> WarKillsByMember { get; } = new();

    /// <summary>True on the guild that DECLARED the current war, false on the guild that was declared
    /// upon. Only the declared-upon guild gets post-war protection.</summary>
    public bool WarIsDeclarer { get; set; }

    public bool IsAtWar => WarEndsAt.HasValue && WarEndsAt.Value > DateTime.UtcNow;

    public bool IsProtected => WarProtectedUntil.HasValue && WarProtectedUntil.Value > DateTime.UtcNow;

    public bool isDisbanded { get; set; } = false;

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
        ApplyBuffBonuses(character);
    }

    /// <summary>
    /// Recomputes one member's prestige-shop buff stat bonuses from <see cref="PurchasedBuffGrades"/> and
    /// pushes a fresh UnitState so the client's character sheet reflects them.
    /// TODO: an already-open character-info sheet only picks this up after a relog/reopen, not live -
    /// every trigger tried for a live refresh had an unwanted side effect (a spurious buff-bar icon, or
    /// risking wrong data in the actability-management panel). Not a data bug, just not instantaneous.
    /// </summary>
    public void ApplyBuffBonuses(Character character)
    {
        character.Bonuses[Buffs.ExpeditionBonusesIndex] = [];
        foreach (var (buffId, grade) in PurchasedBuffGrades)
        {
            foreach (var (attribute, modifierType, value) in ExpeditionBuffGameData.GetBonusEffects(buffId, grade))
            {
                var template = new BonusTemplate { Attribute = attribute, ModifierType = modifierType, Value = value };
                character.AddBonus(Buffs.ExpeditionBonusesIndex, new Bonus { Template = template, Value = value });
            }
        }
        character.SendPacket(new SCUnitStatePacket(character));
        // SCUnitState alone updates the client's cached Max Hp/Mp (the sheet's denominator) but does NOT
        // redraw the visible HP/MP bar - that only happens on SCUnitPointsPacket, per the same two-packet
        // pattern Character.ApplyLevelUpBenefits already uses for the exact same "max stat changed" case.
        // Current Hp/Mp are unchanged here (buffs raise the ceiling, not a free refill), just re-sent so
        // the bar redraws against the new max.
        character.BroadcastPacket(new SCUnitPointsPacket(character.ObjId, character.Hp, character.Mp), true);
    }

    /// <summary>Called after a buff purchase changes <see cref="PurchasedBuffGrades"/> - every online member's stats need the new total, not just the purchaser's.</summary>
    public void ApplyBuffBonusesToAllOnline()
    {
        foreach (var member in Members)
        {
            var character = WorldManager.Instance.GetCharacterById(member.CharacterId);
            if (character != null)
                ApplyBuffBonuses(character);
        }
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

                command.CommandText =
                    "REPLACE INTO expeditions(`id`,`owner`,`owner_name`,`name`,`mother`,`level`,`exp`,`notice`,`residence_house_id`,`interest`,`war_enemy_expedition_id`,`war_declared_at`,`war_protected_until`,`war_ends_at`,`war_kill_score`,`war_is_declarer`,`created_at`) " +
                    "VALUES (@id, @owner, @owner_name, @name, @mother, @level, @exp, @notice, @residence_house_id, @interest, @war_enemy_expedition_id, @war_declared_at, @war_protected_until, @war_ends_at, @war_kill_score, @war_is_declarer, @created_at)";
                command.Parameters.AddWithValue("@id", this.Id);
                command.Parameters.AddWithValue("@owner", this.OwnerId);
                command.Parameters.AddWithValue("@owner_name", this.OwnerName);
                command.Parameters.AddWithValue("@name", this.Name);
                command.Parameters.AddWithValue("@mother", this.MotherId);
                command.Parameters.AddWithValue("@level", this.Level);
                command.Parameters.AddWithValue("@exp", this.Exp);
                command.Parameters.AddWithValue("@notice", this.Notice);
                command.Parameters.AddWithValue("@residence_house_id", this.ResidenceHouseId);
                command.Parameters.AddWithValue("@interest", this.Interest);
                command.Parameters.AddWithValue("@war_enemy_expedition_id", this.WarEnemyExpeditionId);
                command.Parameters.AddWithValue("@war_declared_at", (object)this.WarDeclaredAt ?? DBNull.Value);
                command.Parameters.AddWithValue("@war_protected_until", (object)this.WarProtectedUntil ?? DBNull.Value);
                command.Parameters.AddWithValue("@war_ends_at", (object)this.WarEndsAt ?? DBNull.Value);
                command.Parameters.AddWithValue("@war_kill_score", this.WarKillScore);
                command.Parameters.AddWithValue("@war_is_declarer", this.WarIsDeclarer);
                command.Parameters.AddWithValue("@created_at", this.Created);
                command.ExecuteNonQuery();
            }

            foreach (var member in Members)
                member.Save(connection, transaction);

            foreach (var policy in Policies)
                policy.Save(connection, transaction);
        }
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
