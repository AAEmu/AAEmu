using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.World.Zones;

using NLog;

namespace AAEmu.Game.Core.Managers.World;

/// <summary>
/// Turns faction-scoring runtime changes into client packets. Everything it sends is derived from
/// a published change, so no packet is produced from a guess about what the client expects.
/// </summary>
/// <remarks>
/// The delivery target is a callback rather than a concrete character so a caller decides the
/// audience: a competition score is a server-wide fact, while a zone score belongs to the zone
/// group that owns the kind.
/// </remarks>
public sealed class FactionScoringNotifier
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly Action<GamePacket> _deliver;

    public FactionScoringNotifier(Action<GamePacket> deliver)
    {
        _deliver = deliver ?? throw new ArgumentNullException(nameof(deliver));
    }

    /// <summary>
    /// Publishes a zone-score change. The packet's kind field carries the
    /// <c>zone_score_kinds</c> id and its delta the score the change actually credited, so a capped
    /// change reaches the client as the reduced delta it produced rather than the one requested.
    /// </summary>
    public void PublishZoneScoreChange(ZoneScoreApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);
        if (application.AppliedDelta == 0)
            return;

        Send(new SCZoneScoreUpdatePacket(application.KindId, application.AppliedDelta));
    }

    /// <summary>
    /// Publishes a zone-score reset. A reset always clears the entry, so it is sent even when the
    /// score was already zero and there was no delta to report.
    /// </summary>
    public void PublishZoneScoreReset(uint kindId)
    {
        Send(new SCZoneScoreResetPacket(kindId));
    }

    /// <summary>
    /// Publishes a zone group's full score list, ordered by the kind's <c>ui_order</c> and then by
    /// kind id, so the client's list reads in the order the content authored. Kinds are read from
    /// the catalog, so an entry whose kind no longer loads is a loud failure rather than a silently
    /// dropped row. No client-side list bound is applied: the zone group sends what it owns.
    /// </summary>
    public void PublishZoneScoreList(FactionScoringGameData gameData, IReadOnlyList<ZoneScoreRuntimeEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(gameData);
        ArgumentNullException.ThrowIfNull(entries);

        var payload = entries
            .OrderBy(entry => gameData.GetZoneScoreKind(entry.KindId).UiOrder)
            .ThenBy(entry => entry.KindId)
            .Select(entry => new ZoneScoreListEntry(entry.KindId, ClampToInt(entry.Score)))
            .ToArray();
        if (payload.Length == 0)
            return;

        Send(new SCZoneScoreListPacket(payload));
    }

    /// <summary>
    /// Publishes a competition score change. The two id fields carry the competition and the
    /// faction it was scored for, and the point field the applied delta.
    /// </summary>
    public void PublishCompetitionPoint(FactionCompetitionScoreApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);
        if (application.AppliedDelta == 0)
            return;

        Send(new SCFactionCompetitionUpdatePointPacket(
            (ushort)application.CompetitionId,
            application.FactionId,
            application.AppliedDelta));
    }

    private void Send(GamePacket packet)
    {
        try
        {
            _deliver(packet);
        }
        catch (Exception exception)
        {
            // A failed send must not roll back the score that produced it, and must not stop the
            // remaining changes from being published.
            Logger.Error(exception, "Failed to publish {0}", packet.GetType().Name);
        }
    }

    /// <summary>
    /// Narrows a stored score to the signed int the wire carries. A competition or zone score
    /// beyond that range cannot be represented, so it is clamped to the range's bound rather than
    /// silently wrapping to the opposite sign.
    /// </summary>
    private static int ClampToInt(long score) => (int)Math.Clamp(score, int.MinValue, int.MaxValue);
}
