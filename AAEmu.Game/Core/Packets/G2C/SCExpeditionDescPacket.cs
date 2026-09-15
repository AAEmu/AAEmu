using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Expeditions;
using AAEmu.Game.Models.Game.Faction;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// The guild info panel's descriptor - level/exp/notice and the rest of X2::ExpeditionDesc. No
/// client-side request opcode exists for this data, so it must be server-pushed, matching the
/// existing SendExpeditionInfo pattern (login, create, invite-accept).
/// </summary>
public sealed class SCExpeditionDescPacket : GamePacket
{
    private readonly SystemFaction _faction;
    private readonly int _level;
    private readonly int _exp;
    private readonly DateTime _protectDate;
    private readonly uint _warDeposit;
    private readonly uint _dailyExp;
    private readonly DateTime _lastExpUpdateTime;
    private readonly short _interest;
    private readonly string _notice;
    private readonly uint _warWins;
    private readonly uint _warLosses;
    private readonly uint _warDraws;
    private readonly long _recipientContributionPoint;
    private readonly uint _dailyContributionPoint;
    private readonly DateTime _lastContributionPointAdded;
    private readonly DateTime _lastAssignmentUpdateTime;

    public SCExpeditionDescPacket(Expedition expedition, uint recipientContributionPoint)
        : base(SCOffsets.SCExpeditionDescPacket, 1)
    {
        _faction = new SystemFaction
        {
            Id = expedition.Id, MotherId = expedition.MotherId, Name = expedition.Name,
            OwnerId = expedition.OwnerId, OwnerName = expedition.OwnerName,
            UnitOwnerType = expedition.UnitOwnerType, PoliticalSystem = expedition.PoliticalSystem,
            Created = expedition.Created, AggroLink = expedition.AggroLink,
            DiplomacyTarget = expedition.DiplomacyTarget, AllowChangeName = expedition.AllowChangeName,
            RenameTime = expedition.RenameTime, IntegrationFaction = expedition.IntegrationFaction
        };
        _level = checked((int)expedition.Level);
        _exp = checked((int)expedition.Exp);
        _protectDate = expedition.WarEndsAt ?? expedition.WarProtectedUntil ?? DateTime.UtcNow;
        _warDeposit = expedition.WarDeposit;
        _dailyExp = expedition.DailyExp;
        _lastExpUpdateTime = expedition.LastExpUpdateTime;
        _interest = expedition.Interest;
        _notice = ExpeditionTextRules.TruncateUtf8(expedition.Notice, ExpeditionTextRules.MaximumNoticeUtf8Bytes);
        _warWins = expedition.WarWins;
        _warLosses = expedition.WarLosses;
        _warDraws = expedition.WarDraws;
        _recipientContributionPoint = recipientContributionPoint;
        _dailyContributionPoint = expedition.DailyContributionPoint;
        _lastContributionPointAdded = expedition.LastContributionPointAdded;
        _lastAssignmentUpdateTime = expedition.LastAssignmentUpdateTime;
    }

    public override PacketStream Write(PacketStream stream)
    {
        // FactionDesc base portion (id, motherId, name, owner, ..., integrationFaction) - shared with
        // SCExpeditionListPacket/SCFactionCreatedPacket.
        stream.Write(_faction);

        stream.Write(_level);
        stream.Write(_exp);
        // TODO: do not reorder these 4 fields (protectDate/warDeposit/dailyExp/lastExpUpdateTime) without
        // live-debugged evidence - a prior reorder attempt broke the guild protection-time display.
        stream.Write(_protectDate);
        stream.Write(_warDeposit);
        stream.Write(_dailyExp);
        stream.Write(_lastExpUpdateTime);
        stream.Write(_interest);
        stream.Write(_notice);
        stream.Write(_warWins);
        stream.Write(_warLosses);
        stream.Write(_warDraws);
        stream.Write(0xff);                // unconfirmed field
        stream.Write(0L);                  // unconfirmed field
        stream.Write(_recipientContributionPoint);
        stream.Write(_dailyContributionPoint);
        stream.Write(_lastContributionPointAdded);
        stream.Write(_lastAssignmentUpdateTime);
        return stream;
    }
}
