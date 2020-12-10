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
            ObjectIdManager.Instance.Initialize();
            var firstId = 0x00000001u;
            var id = ObjectIdManager.Instance.GetNextId();
            Assert.Equal(firstId, id);
            id = ObjectIdManager.Instance.GetNextId();
            Assert.Equal(firstId+1, id);
            id = ObjectIdManager.Instance.GetNextId();
            Assert.Equal(firstId+2, id);
        }
        
        [Fact]
        public void ItemIdManagerReleasesId()
        {
            ObjectIdManager.Instance.Initialize();
            var firstId = 0x00000001u;
            var id = ObjectIdManager.Instance.GetNextId();
            Assert.Equal(firstId, id);
            id = ObjectIdManager.Instance.GetNextId();
            Assert.Equal(firstId+1, id);
            
            ObjectIdManager.Instance.ReleaseId(id);;
            
            id = ObjectIdManager.Instance.GetNextId();
            // We get the next ID and THEN release
            Assert.Equal(firstId+1, id);
        }

        [Fact]
        public void ItemIdManagerGetMultipleIds()
        {
            ObjectIdManager.Instance.Initialize();
            
            var firstId = 0x00000001u;
            var ids = ObjectIdManager.Instance.GetNextId(10);
            Assert.Equal(new uint[]{firstId, firstId+1, firstId+2, firstId+3, firstId+4, firstId+5, firstId+6, firstId+7, firstId+8, firstId+9}, ids);
        }
    }
}
