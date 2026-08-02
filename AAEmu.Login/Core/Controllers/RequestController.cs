using System.Collections.Concurrent;
using AAEmu.Login.Utils;

namespace AAEmu.Login.Core.Controllers;

public class RequestController(ILogger<RequestController> logger)
    : IdManager("RequestController", firstId, lastId, objTables, exclude, logger), IRequestController
{
    private const uint firstId = 0x00000001;
    private const uint lastId = 0x00FFFFFF;
    private static readonly uint[] exclude = [];
    private static readonly string[,] objTables = { { } };
    private readonly ConcurrentDictionary<uint, TaskCompletionSource<bool>> _requests = new();

    public (uint[] requestIds, Task result) Create(int count, TimeSpan timeout)
    {
        var requestIds = GetNextId(count);
        var tasks = new Task[count];
        for (var i = 0; i < count; i++)
        {
            var taskSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _requests.TryAdd(requestIds[i], taskSource);
            tasks[i] = WaitForCompletion(requestIds[i], taskSource.Task, timeout);
        }

        return (requestIds, Task.WhenAll(tasks));
    }

    private async Task WaitForCompletion(uint requestId, Task completion, TimeSpan timeout)
    {
        await Task.WhenAny(completion, Task.Delay(timeout));

        // A normal response removes the request through ReleaseId. If the delay won, this path owns cleanup.
        if (_requests.TryRemove(requestId, out _))
            base.ReleaseId(requestId);
    }

    public override void ReleaseId(uint usedObjectId)
    {
        if (_requests.TryRemove(usedObjectId, out var taskSource))
        {
            taskSource.TrySetResult(true);
            base.ReleaseId(usedObjectId);
        }
    }
}
