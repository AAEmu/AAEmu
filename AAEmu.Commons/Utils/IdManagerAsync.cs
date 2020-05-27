using System;
using System.Collections.Concurrent;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using AAEmu.Commons.DI;
using AAEmu.Commons.Utils.IdContainers;

namespace AAEmu.Commons.Utils
{
    public interface IIdManagerAsync
    {
        void Create(IdType tag, ulong[] usedObjectId = null);
        Task<ulong> GetNextId(IdType tag);
        Task<ulong[]> GetNextId(IdType tag, int count);
        Task<ulong> TryGetNextId(IdType tag);
        Task<ulong[]> TryGetNextId(IdType tag, int count);
        bool ReleaseId(IdType tag, ulong usedId);
        bool[] ReleaseId(IdType tag, ulong[] usedId);
    }

    public class IdManagerAsync : ISingletonService, IIdManagerAsync
    {
        private readonly ConcurrentDictionary<IdType, IIdContainer> _dictionary;
        private readonly ConcurrentDictionary<IdType, ConcurrentBag<ulong>> _pool;

        public IdManagerAsync()
        {
            _dictionary = new ConcurrentDictionary<IdType, IIdContainer>();
            _pool = new ConcurrentDictionary<IdType, ConcurrentBag<ulong>>();
            Math.PrimeFinder.Init();
        }

        public async void Create(IdType tag, ulong[] usedObjectId = null)
        {
            if (_dictionary.ContainsKey(tag))
                throw new DuplicateNameException();

            IIdContainer container;
            switch (tag)
            {
                case IdType.AccountId:
                case IdType.ItemId:
                    container = new BitIdContainer(0x00000001, 0xFFFFFFFF); // TODO : Generate ulong Bit64IdContainer
                    break;
                case IdType.CharacterId:
                    container = new BitIdContainer(0x00000001, 0xFFFFFFFF);
                    break;
                case IdType.UnitObjId:
                case IdType.DoodadObjId:
                case IdType.SkillObjId:
                case IdType.PlotObjId:
                case IdType.GimmickObjId:
                    container = new BitIdContainer(0x00000001, 0x00FEFFFF);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(tag), tag, null);
            }

            if (usedObjectId != null)
                container.Initialize(usedObjectId);

            _dictionary.TryAdd(tag, container);

            var reserved = await container.GetNextId(10000); // TODO buffer, id pool
            Array.Reverse(reserved);
            _pool.TryAdd(tag, new ConcurrentBag<ulong>(reserved));
        }

        [Obsolete("This method is deprecated, is better to use TryGetNextId", false)]
        public async Task<ulong> GetNextId(IdType tag)
        {
            if (_dictionary.TryGetValue(tag, out var container))
                return await container.GetNextId();

            throw new ArgumentOutOfRangeException(nameof(tag));
        }

        [Obsolete("This method is deprecated, is better to use TryGetNextId", false)]
        public async Task<ulong[]> GetNextId(IdType tag, int count)
        {
            if (_dictionary.TryGetValue(tag, out var container))
                return await container.GetNextId(count);

            throw new ArgumentOutOfRangeException(nameof(tag));
        }

        public Task<ulong> TryGetNextId(IdType tag)
        {
            if (_pool.TryGetValue(tag, out var pool))
                if (pool.TryTake(out var id))
                    return Task.FromResult(id);

#pragma warning disable 0618
            return GetNextId(tag);
#pragma warning restore 0618
        }

        public async Task<ulong[]> TryGetNextId(IdType tag, int count)
        {

            if (_pool.TryGetValue(tag, out var pool))
            {
                var ids = new ulong[count];
                int i;
                for (i = 0; i < ids.Length; i++)
                {
                    if (pool.TryTake(out var id))
                        ids[i] = id;
                    else
                        break;
                }

                if (i < ids.Length)
                {
                    var next = ids.Length - (i + 1);
#pragma warning disable 0618
                    var nextIds = await GetNextId(tag, next);
#pragma warning restore 0618
                    for (var j = 0; j < nextIds.Length; j++)
                        ids[i + j] = nextIds[j];
                }

                return ids;

            }

#pragma warning disable 0618
            return await GetNextId(tag, count);
#pragma warning restore 0618
        }

        public bool ReleaseId(IdType tag, ulong usedId)
        {
            if (_pool.TryGetValue(tag, out var pool))
            {
                pool.Add(usedId);
                return true;
            }

            if (_dictionary.TryGetValue(tag, out var container))
                return container.ReleaseId(usedId);

            throw new ArgumentOutOfRangeException(nameof(tag));
        }

        public bool[] ReleaseId(IdType tag, ulong[] usedId)
        {
            if (_pool.TryGetValue(tag, out var pool))
            {
                Parallel.ForEach(usedId, id => pool.Add(id));
                return Enumerable.Repeat(true, usedId.Length).ToArray();
            }

            if (_dictionary.TryGetValue(tag, out var container))
                return container.ReleaseId(usedId);

            throw new ArgumentOutOfRangeException(nameof(tag));
        }
    }
}
