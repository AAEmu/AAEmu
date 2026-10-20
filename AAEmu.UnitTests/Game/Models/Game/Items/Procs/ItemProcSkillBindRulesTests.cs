using AAEmu.Game.Models.Game.Items.Procs;
using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.UnitTests.Game.Models.Game.Items.Procs;

public class ItemProcSkillBindRulesTests
{
    [Test]
    public async Task Bind_ResolvesSkillTemplatesBySkillId()
    {
        var skill = new SkillTemplate { Id = 10 };
        var proc = new ItemProcTemplate { Id = 1, SkillId = 10 };

        var missing = ItemProcSkillBindRules.Bind([proc], id => id == 10 ? skill : null);

        await Assert.That(missing).IsEqualTo(0);
        await Assert.That(proc.SkillTemplate).IsSameReferenceAs(skill);
    }

    [Test]
    public async Task Bind_CountsMissingSkills()
    {
        var known = new SkillTemplate { Id = 1 };
        var procs = new[]
        {
            new ItemProcTemplate { Id = 1, SkillId = 1 },
            new ItemProcTemplate { Id = 2, SkillId = 999 },
            new ItemProcTemplate { Id = 3, SkillId = 999 }
        };

        var missing = ItemProcSkillBindRules.Bind(procs, id => id == 1 ? known : null);

        await Assert.That(missing).IsEqualTo(2);
        await Assert.That(procs[0].SkillTemplate).IsSameReferenceAs(known);
        await Assert.That(procs[1].SkillTemplate).IsNull();
        await Assert.That(procs[2].SkillTemplate).IsNull();
    }

    [Test]
    public async Task Bind_IsIdempotentWhenCalledAgain()
    {
        var skill = new SkillTemplate { Id = 10 };
        var proc = new ItemProcTemplate { Id = 5, SkillId = 10 };

        ItemProcSkillBindRules.Bind([proc], _ => skill);
        var missing = ItemProcSkillBindRules.Bind([proc], _ => skill);

        await Assert.That(missing).IsEqualTo(0);
        await Assert.That(proc.SkillTemplate).IsSameReferenceAs(skill);
    }

    [Test]
    public async Task Bind_AfterSkillsCatalogReadyWithoutEvent_ResolvesTemplates()
    {
        // Models the missed-event race: skills are already resolvable, but no OnSkillsLoaded
        // listener ran. A post-load Bind (ItemManager.Load) is what fills SkillTemplate.
        var catalog = new Dictionary<uint, SkillTemplate> { [10] = new() { Id = 10 } };
        var proc = new ItemProcTemplate { Id = 1, SkillId = 10 };

        await Assert.That(proc.SkillTemplate).IsNull();

        var missing = ItemProcSkillBindRules.Bind([proc], id => catalog.GetValueOrDefault(id));

        await Assert.That(missing).IsEqualTo(0);
        await Assert.That(proc.SkillTemplate).IsSameReferenceAs(catalog[10]);
    }
}
