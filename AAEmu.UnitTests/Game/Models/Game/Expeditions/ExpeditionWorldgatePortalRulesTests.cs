using System.Numerics;
using AAEmu.Game.Models.Game.Expeditions;
using AAEmu.Game.Models.Game.Expeditions.Activities;

namespace AAEmu.UnitTests.Game.Models.Game.Expeditions;

public class ExpeditionWorldgatePortalRulesTests
{
    [Test]
    public async Task TryResolveNpcPair_RequiresBothSides()
    {
        await Assert.That(ExpeditionWorldgatePortalRules.TryResolveNpcPair(1, 2)).IsTrue();
        await Assert.That(ExpeditionWorldgatePortalRules.TryResolveNpcPair(0, 2)).IsFalse();
        await Assert.That(ExpeditionWorldgatePortalRules.TryResolveNpcPair(1, 0)).IsFalse();
    }

    [Test]
    public async Task ToBookPortal_MapsExpeditionPointFields()
    {
        var point = new ExpeditionPortalPoint
        {
            Id = 9,
            ExpeditionId = 700,
            Name = "base",
            ZoneId = 183,
            X = 1f,
            Y = 2f,
            Z = 3f,
            ZRot = 0.5f
        };

        var portal = ExpeditionWorldgatePortalRules.ToBookPortal(point);

        await Assert.That(portal).IsNotNull();
        await Assert.That(portal!.Id).IsEqualTo(9u);
        await Assert.That(portal.Owner).IsEqualTo(700u);
        await Assert.That(portal.Name).IsEqualTo("base");
        await Assert.That(portal.ZoneId).IsEqualTo(183u);
        await Assert.That(portal.X).IsEqualTo(1f);
        await Assert.That(portal.Y).IsEqualTo(2f);
        await Assert.That(portal.Z).IsEqualTo(3f);
        await Assert.That(portal.ZRot).IsEqualTo(0.5f);
        await Assert.That(ExpeditionWorldgatePortalRules.ToBookPortal(null)).IsNull();
    }

    [Test]
    public async Task ResolveEnterPosition_UsesWorldgateXyAndRaisesZ()
    {
        var gate = new Vector3(10, 20, 30);
        var caster = new Vector3(11, 21, 35);
        var aimpoint = new Vector3(0, 0, 1.7f);

        await Assert.That(ExpeditionWorldgatePortalRules.ResolveEnterPosition(null, caster))
            .IsEqualTo(caster);

        // No aimpoint: keep device XY, lift to caster standing height (pad origin is too low).
        await Assert.That(ExpeditionWorldgatePortalRules.ResolveEnterPosition(gate, caster))
            .IsEqualTo(new Vector3(10, 20, 35));

        // Aimpoint local Z lifts above the pad; still never below the caster.
        await Assert.That(ExpeditionWorldgatePortalRules.ResolveEnterPosition(gate, caster, aimpoint))
            .IsEqualTo(new Vector3(10, 20, 35));
        await Assert.That(ExpeditionWorldgatePortalRules.ResolveEnterPosition(
                gate, new Vector3(11, 21, 30.5f), aimpoint))
            .IsEqualTo(new Vector3(10, 20, 31.7f));
    }

    [Test]
    public async Task ConstNames_MatchShippedCatalogKeys()
    {
        await Assert.That(ExpeditionWorldgatePortalRules.EnterNpcConstName).IsEqualTo("portal");
        await Assert.That(ExpeditionWorldgatePortalRules.ExitNpcConstName).IsEqualTo("exit_portal");
        await Assert.That(ExpeditionWorldgatePortalRules.WorldgateDoodadConstName)
            .IsEqualTo("expedition_portal");
    }
}
