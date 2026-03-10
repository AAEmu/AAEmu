using AAEmu.Game.Core.Managers.World;

namespace AAEmu.UnitTests.Game.Core.Managers.World;

public class ExampleTests
{

    [Test]
    public async Task SampleTest()
    {
        await Assert.That(SpecialtyManager.GetValueOfOne()).IsEqualTo(1);
    }
}