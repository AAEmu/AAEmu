using AAEmu.Game.Models.Game.TowerDefs;

namespace AAEmu.UnitTests.Game.Models.Game.TowerDefs;

public class TowerDefFlavorFieldsTests
{
    [Test]
    public async Task TowerDef_FlavorFields_Roundtrip()
    {
        var towerDef = new TowerDef
        {
            Id = 5,
            Name = "Ynystere Crimson Rift",
            StartMsg = "rift appears",
            EndMsg = "rift vanishes",
            TitleMsg = "Crimson Omens",
            MilestoneId = 42,
            TimeOfDay = 12f,
            ForceEndTime = 3600f,
            TimeOfDayDayInterval = 1,
            Progs = [],
        };

        await Assert.That(towerDef.Id).IsEqualTo(5u);
        await Assert.That(towerDef.Name).IsEqualTo("Ynystere Crimson Rift");
        await Assert.That(towerDef.StartMsg).IsEqualTo("rift appears");
        await Assert.That(towerDef.EndMsg).IsEqualTo("rift vanishes");
        await Assert.That(towerDef.TitleMsg).IsEqualTo("Crimson Omens");
        await Assert.That(towerDef.MilestoneId).IsEqualTo(42u);
        await Assert.That(towerDef.Progs).IsNotNull();
    }

    [Test]
    public async Task TowerDefProg_Msg_Roundtrips()
    {
        var prog = new TowerDefProg
        {
            Id = 15,
            Msg = "marching boots",
            CondToNextTime = 0f,
            CondCompByAnd = true,
            KillTargets = [],
            SpawnTargets = [],
        };

        await Assert.That(prog.Id).IsEqualTo(15u);
        await Assert.That(prog.Msg).IsEqualTo("marching boots");
    }

    [Test]
    public async Task TowerDefProg_Msg_AcceptsNull()
    {
        var prog = new TowerDefProg { Msg = null };

        await Assert.That(prog.Msg).IsNull();
    }
}
