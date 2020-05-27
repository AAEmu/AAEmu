using System.Threading.Tasks;

namespace AAEmu.Commons.Utils.IdContainers
{
    public interface IIdContainer
    {
        void Initialize(ulong[] usedObjectIds);
        Task<ulong> GetNextId();
        Task<ulong[]> GetNextId(int count);
        bool ReleaseId(ulong usedObjectId);
        bool[] ReleaseId(ulong[] usedObjectIds);
    }
}
