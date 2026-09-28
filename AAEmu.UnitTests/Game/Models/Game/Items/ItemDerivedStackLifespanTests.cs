using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.UnitTests.Game.Models.Game.Items;

/// <summary>
/// A stack that is derived from an existing stack - a bag split, a trade split - takes the source's
/// remaining lifetime, not a fresh one from the template.
/// <para>
/// The factory arms a new item from its template, which is right for an item the server is handing
/// out and wrong for a piece of a stack somebody already owns: a stack with 10 minutes left would
/// split into a new stack with the template's full term, and the item would be worth more after being
/// split than before. Both split paths therefore overwrite the armed values from the source, and these
/// tests pin that they do.
/// </para>
/// </summary>
public class ItemDerivedStackLifespanTests
{
    private static readonly DateTime Created = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime SourceEnd = new(2026, 3, 1, 12, 30, 0, DateTimeKind.Utc);

    private static ItemTemplate Template() => new() { Id = 1, ExpAbsLifetime = 1440, ExpOnlineLifetime = 60 };

    private static Item ArmedSource()
    {
        var source = new Item(10, Template(), 5) { Id = 10, TemplateId = 1, CreateTime = Created };
        ItemLifetimeRules.ApplyNewItemLifespan(source, Created);
        return source;
    }

    [Test]
    public async Task TheFactoryArmsAFreshItemFromTheTemplateTerm()
    {
        var source = ArmedSource();

        // 1440 minutes from creation, and 60 minutes of online budget.
        await Assert.That(source.ExpirationTime).IsEqualTo(Created.AddMinutes(1440));
        await Assert.That(source.ExpirationOnlineMinutesLeft).IsEqualTo(60.0);
    }

    [Test]
    public async Task ATradeSplitInheritsTheSourceRemainingLifetime()
    {
        var source = ArmedSource();
        // The source has been sitting in the bag and is 30 minutes from the end of its term.
        source.ExpirationTime = SourceEnd;

        var split = new Item(11, Template(), 2) { Id = 11, TemplateId = 1, CreateTime = DateTime.UtcNow };
        ItemLifetimeRules.ApplyNewItemLifespan(split, DateTime.UtcNow);
        split.CopyPersistentStateFrom(source);

        // Not the template's full term again, and not the moment the split was made either.
        await Assert.That(split.ExpirationTime).IsEqualTo(SourceEnd);
    }

    [Test]
    public async Task ABagSplitInheritsTheSourceRemainingLifetime()
    {
        var source = ArmedSource();
        source.ExpirationTime = SourceEnd;
        source.ExpirationOnlineMinutesLeft = 12.5;

        var split = new Item(12, Template(), 3) { Id = 12, TemplateId = 1, CreateTime = DateTime.UtcNow };
        ItemLifetimeRules.ApplyNewItemLifespan(split, DateTime.UtcNow);
        ItemSplitRules.CopyStackFields(source, split);

        await Assert.That(split.ExpirationTime).IsEqualTo(SourceEnd);
        await Assert.That(split.ExpirationOnlineMinutesLeft).IsEqualTo(12.5);
    }

    [Test]
    public async Task RepeatedSplitsNeverExtendTheTerm()
    {
        var source = ArmedSource();
        source.ExpirationTime = SourceEnd;
        source.ExpirationOnlineMinutesLeft = 12.5;

        var first = new Item(13, Template(), 1) { Id = 13, TemplateId = 1, CreateTime = DateTime.UtcNow };
        ItemLifetimeRules.ApplyNewItemLifespan(first, DateTime.UtcNow);
        ItemSplitRules.CopyStackFields(source, first);

        var second = new Item(14, Template(), 1) { Id = 14, TemplateId = 1, CreateTime = DateTime.UtcNow };
        ItemLifetimeRules.ApplyNewItemLifespan(second, DateTime.UtcNow);
        ItemSplitRules.CopyStackFields(first, second);

        // A stack split twenty times is still worth exactly what its source was worth.
        await Assert.That(second.ExpirationTime).IsEqualTo(SourceEnd);
        await Assert.That(second.ExpirationOnlineMinutesLeft).IsEqualTo(12.5);
    }

    [Test]
    public async Task ASplitOfAnUntimedItemStaysUntimed()
    {
        // The overwhelming majority of the catalogue carries no lifespan column at all, and splitting
        // one must not invent a term for it.
        var untimed = new ItemTemplate { Id = 2 };
        var source = new Item(15, untimed, 4) { Id = 15, TemplateId = 2, CreateTime = Created };
        ItemLifetimeRules.ApplyNewItemLifespan(source, Created);

        var split = new Item(16, untimed, 2) { Id = 16, TemplateId = 2, CreateTime = DateTime.UtcNow };
        ItemLifetimeRules.ApplyNewItemLifespan(split, DateTime.UtcNow);
        ItemSplitRules.CopyStackFields(source, split);

        await Assert.That(split.ExpirationTime).IsEqualTo(DateTime.MinValue);
        await Assert.That(split.ExpirationOnlineMinutesLeft).IsEqualTo(0.0);
    }

    [Test]
    public async Task SplitStacksOfTheSameItemStillStackWithEachOther()
    {
        // CanStackWith compares the lifetime, so a split that kept a different one would leave two
        // stacks of the same item that can never be merged again.
        var source = ArmedSource();
        source.ExpirationTime = SourceEnd;

        var split = new Item(17, Template(), 1) { Id = 17, TemplateId = 1, CreateTime = DateTime.UtcNow };
        ItemLifetimeRules.ApplyNewItemLifespan(split, DateTime.UtcNow);

        // Fresh-armed, they are not the same stack...
        await Assert.That(split.CanStackWith(source)).IsFalse();

        // ...and once the split has taken the source's remaining term, they are.
        ItemSplitRules.CopyStackFields(source, split);
        await Assert.That(split.CanStackWith(source)).IsTrue();
    }
}
