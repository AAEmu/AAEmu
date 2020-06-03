using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Utils;
using Xunit;

namespace AAEmu.Tests
{
    public class IdManagerTests
    {
        [Fact]
        public void ItemIdManagerGetsNextId()
        {
            ItemIdManager.Instance.Initialize();
            var firstId = 0x01000000u;
            var id = ItemIdManager.Instance.GetNextId();
            Assert.Equal(firstId, id);
            id = ItemIdManager.Instance.GetNextId();
            Assert.Equal(firstId+1, id);
            id = ItemIdManager.Instance.GetNextId();
            Assert.Equal(firstId+2, id);
        }
        
        [Fact]
        public void ItemIdManagerReleasesId()
        {
            ItemIdManager.Instance.Initialize();
            var firstId = 0x01000000u;
            var id = ItemIdManager.Instance.GetNextId();
            Assert.Equal(firstId, id);
            id = ItemIdManager.Instance.GetNextId();
            Assert.Equal(firstId+1, id);
            
            ItemIdManager.Instance.ReleaseId(id);;
            
            id = ItemIdManager.Instance.GetNextId();
            Assert.Equal(firstId+1, id);
        }

        [Fact]
        public void ItemIdManagerGetMultipleIds()
        {
            ItemIdManager.Instance.Initialize();
            
            var firstId = 0x01000000u;
            var ids = ItemIdManager.Instance.GetNextId(10);
            Assert.Equal(new uint[]{firstId, firstId+1, firstId+2, firstId+3, firstId+4, firstId+5, firstId+6, firstId+7, firstId+8, firstId+9}, ids);
        }
    }
}
