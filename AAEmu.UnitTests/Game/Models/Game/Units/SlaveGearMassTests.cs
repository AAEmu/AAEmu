using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Slaves;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Units;

public class SlaveGearMassTests
{
    [Test]
    public async Task UpdateSlaveGearBonuses_FoldsChildTemplateMassOnly()
    {
        var hull = new Slave { ObjId = 2339 };
        hull.AttachedSlaves.Add(new Slave
        {
            ObjId = 2348,
            Template = new SlaveTemplate
            {
                Bonuses =
                [
                    new BonusTemplate
                    {
                        Attribute = UnitAttribute.Mass,
                        ModifierType = UnitModifierType.Value,
                        Value = 11000
                    },
                    new BonusTemplate
                    {
                        Attribute = UnitAttribute.MaxHealth,
                        ModifierType = UnitModifierType.Value,
                        Value = 15000
                    }
                ]
            }
        });

        hull.UpdateSlaveGearBonuses();

        await Assert.That(hull.CalculateWithBonuses(0, UnitAttribute.Mass)).IsEqualTo(11000);
        await Assert.That(hull.CalculateWithBonuses(0, UnitAttribute.MaxHealth)).IsEqualTo(0);
    }
}
