using System.Collections.Concurrent;
using System.Threading.Tasks;
using AAEmu.Login.Utils;

namespace AAEmu.Login.Core.Controllers
{
    public class RequestController : IdManager
    {
        private static RequestController _instance;
        private const uint firstId = 0x00000001;
        private const uint lastId = 0x00FFFFFF;
        private static uint[] exclude = { };
        private static string[,] objTables = {{ }};
        private readonly ConcurrentDictionary<uint, TaskCompletionSource<bool>> _requests;
        public static RequestController Instance => _instance ?? (_instance = new RequestController());

        public RequestController() : base("RequestController", firstId, lastId, objTables, exclude)
        {
            _requests = new ConcurrentDictionary<uint, TaskCompletionSource<bool>>();
        }

        public (uint[] requestIds, Task result) Create(int count, int timeout)
        {
            var requestIds = GetNextId(count);
            var tasks = new Task[count];
            for (var i = 0; i < count; i++)
            {
                var task = new TaskCompletionSource<bool>();
                _requests.TryAdd(requestIds[i], task);
                tasks[i] = Task.WhenAny(task.Task, Task.Delay(timeout));
            }

            return (requestIds, Task.WhenAll(tasks));
        }

        public override void ReleaseId(uint usedObjectId)
        {
            if (_requests.TryGetValue(usedObjectId, out var taskSource))
            {
                taskSource.TrySetResult(true);
                _requests.TryRemove(usedObjectId, out _);
            }

            base.ReleaseId(usedObjectId);
        }
    }
}
