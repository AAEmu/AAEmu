using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Utils;
using Xunit;

namespace AAEmu.Tests.Unit
{
    public class IdManagerTests
    {
        [Fact]
        public void ItemIdManagerGetsNextId()
        {
            var objIdManager = new ObjectIdManager();
            objIdManager.Initialize();
            
            var firstId = 0x00000001u;
            var id = objIdManager.GetNextId();
            Assert.Equal(firstId, id);
            id = objIdManager.GetNextId();
            Assert.Equal(firstId+1, id);
            id = objIdManager.GetNextId();
            Assert.Equal(firstId+2, id);
        }
        
        [Fact]
        public void ItemIdManagerReleasesId()
        {
            var objIdManager = new ObjectIdManager();
            objIdManager.Initialize();
            var firstId = 0x00000001u;
            var id = objIdManager.GetNextId();
            Assert.Equal(firstId, id);
            id = objIdManager.GetNextId();
            Assert.Equal(firstId+1, id);

            objIdManager.ReleaseId(id);;
            
            id = objIdManager.GetNextId();
            // We get the next ID and THEN release
            Assert.Equal(firstId+1, id);
        }

        [Fact]
        public void ItemIdManagerGetMultipleIds()
        {
            var objIdManager = new ObjectIdManager();
            objIdManager.Initialize();
            
            var firstId = 0x00000001u;
            var ids = objIdManager.GetNextId(10);
            Assert.Equal(new uint[]{firstId, firstId+1, firstId+2, firstId+3, firstId+4, firstId+5, firstId+6, firstId+7, firstId+8, firstId+9}, ids);
        }
    }
}
