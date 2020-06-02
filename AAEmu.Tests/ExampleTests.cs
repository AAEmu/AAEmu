using AAEmu.Game.Core.Managers.World;
using Xunit;

namespace AAEmu.Tests
{
    public class ExampleTests
    {

        [Fact]
        public void SampleTest()
        {
            Assert.Equal(1, SpecialtyManager.Instance.GetValueOfOne());
        }
    }
}
