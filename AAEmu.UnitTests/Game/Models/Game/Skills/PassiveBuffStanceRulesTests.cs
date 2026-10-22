using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class PassiveBuffStanceRulesTests
{
    [Test]
    public async Task HasStanceGatedStartedTrigger_OwnerBuffTag_OnStarted_IsTrue()
    {
        var triggers = new[]
        {
            new BuffTriggerTemplate
            {
                Kind = BuffEventTriggerKind.Started,
                OwnerBuffTagId = 4845
            }
        };

        await Assert.That(PassiveBuffStanceRules.HasStanceGatedStartedTrigger(triggers)).IsTrue();
    }

    [Test]
    public async Task HasStanceGatedStartedTrigger_OwnerNoBuffTag_OnStarted_IsTrue()
    {
        var triggers = new[]
        {
            new BuffTriggerTemplate
            {
                Kind = BuffEventTriggerKind.Started,
                OwnerNoBuffTagId = 4845
            }
        };

        await Assert.That(PassiveBuffStanceRules.HasStanceGatedStartedTrigger(triggers)).IsTrue();
    }

    [Test]
    public async Task HasStanceGatedStartedTrigger_TagOnAttackEvent_IsFalse()
    {
        // Only Started chooses the child at apply time; attack-gated tags are unrelated.
        var triggers = new[]
        {
            new BuffTriggerTemplate
            {
                Kind = BuffEventTriggerKind.Attack,
                OwnerBuffTagId = 3379
            }
        };

        await Assert.That(PassiveBuffStanceRules.HasStanceGatedStartedTrigger(triggers)).IsFalse();
    }

    [Test]
    public async Task HasStanceGatedStartedTrigger_StartedWithoutOwnerTags_IsFalse()
    {
        var triggers = new[]
        {
            new BuffTriggerTemplate { Kind = BuffEventTriggerKind.Started }
        };

        await Assert.That(PassiveBuffStanceRules.HasStanceGatedStartedTrigger(triggers)).IsFalse();
    }

    [Test]
    public async Task HasStanceGatedStartedTrigger_NullOrEmpty_IsFalse()
    {
        await Assert.That(PassiveBuffStanceRules.HasStanceGatedStartedTrigger(null)).IsFalse();
        await Assert.That(PassiveBuffStanceRules.HasStanceGatedStartedTrigger([])).IsFalse();
    }
}
