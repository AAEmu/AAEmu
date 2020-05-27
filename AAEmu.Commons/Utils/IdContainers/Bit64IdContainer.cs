using System.Threading.Tasks;

namespace AAEmu.Commons.Utils.IdContainers
{
    public class Bit64IdContainer : IIdContainer
    {
        public void Initialize(ulong[] usedObjectIds)
        {
            throw new System.NotImplementedException();
        }

        public Task<ulong> GetNextId()
        {
            throw new System.NotImplementedException();
        }

        public Task<ulong[]> GetNextId(int count)
        {
            throw new System.NotImplementedException();
        }

        public bool ReleaseId(ulong usedObjectId)
        {
            throw new System.NotImplementedException();
        }

        public bool[] ReleaseId(ulong[] usedObjectIds)
        {
            throw new System.NotImplementedException();
        }
    }
}
