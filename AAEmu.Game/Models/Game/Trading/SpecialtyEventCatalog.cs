using System.Collections.ObjectModel;

namespace AAEmu.Game.Models.Game.Trading;

public enum SpecialtyEventType
{
    CallNpcSpawner = 1,
    SaleRate = 2,
    OverchargeRate = 3,
    StockChange = 4
}

public enum SpecialtyEventTriggerType
{
    StockCount = 1,
    QuestGroupDone = 2,
    SellBackpackCount = 3,
    ZoneConflictState = 4
}

public enum SpecialtyEventObjectType
{
    Item,
    ItemSet
}

public enum SpecialtyEventTriggerSubjectType
{
    Item,
    ItemSet,
    QuestContextGroup,
    EnumHonorPointWarState
}

public enum SpecialtyEventMessageScope
{
    Zone = 1,
    World = 2
}

internal enum SpecialtyEventActivationSource
{
    Manual,
    ZoneConflict,
    StockCount
}

public sealed class SpecialtyEventTriggerDescriptor
{
    public uint Id { get; init; }
    public uint ZoneGroupId { get; init; }
    public SpecialtyEventTriggerType Type { get; init; }
    public int Value1 { get; init; }
    public int Value2 { get; init; }
    public int CheckTime { get; init; }
    public int EventRate { get; init; }
    public int EventTime { get; init; }
    public SpecialtyEventMessageScope? MessageScope { get; init; }
    public string StartMessage { get; init; }
    public string EndMessage { get; init; }
    public uint SubjectId { get; init; }
    public SpecialtyEventTriggerSubjectType SubjectType { get; init; }
    public IReadOnlyList<uint> SubjectItemIds { get; init; }
}

public sealed class SpecialtyEventDescriptor
{
    public uint Id { get; init; }
    public SpecialtyEventType Type { get; init; }
    public int Value { get; init; }
    public uint ObjectId { get; init; }
    public SpecialtyEventObjectType ObjectType { get; init; }
    public string TooltipText { get; init; }
    public SpecialtyEventTriggerDescriptor Trigger { get; init; }
    public IReadOnlyList<uint> TargetItemIds { get; init; }
}

public sealed record ActiveSpecialtyEvent(
    uint EventId,
    DateTimeOffset StartedAt,
    DateTimeOffset ExpiresAt,
    uint ActivatedByCharacterId);

public sealed class SpecialtyEventCatalog
{
    public static SpecialtyEventCatalog Empty { get; } = new(
        new Dictionary<uint, SpecialtyEventTriggerDescriptor>(),
        new Dictionary<uint, SpecialtyEventDescriptor>());

    internal SpecialtyEventCatalog(
        IDictionary<uint, SpecialtyEventTriggerDescriptor> triggers,
        IDictionary<uint, SpecialtyEventDescriptor> events)
    {
        Triggers = new ReadOnlyDictionary<uint, SpecialtyEventTriggerDescriptor>(
            new Dictionary<uint, SpecialtyEventTriggerDescriptor>(triggers));
        Events = new ReadOnlyDictionary<uint, SpecialtyEventDescriptor>(
            new Dictionary<uint, SpecialtyEventDescriptor>(events));
    }

    public IReadOnlyDictionary<uint, SpecialtyEventTriggerDescriptor> Triggers { get; }
    public IReadOnlyDictionary<uint, SpecialtyEventDescriptor> Events { get; }
}
