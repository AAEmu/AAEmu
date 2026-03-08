using System.IO.MemoryMappedFiles;
using System.Security.Cryptography;
using System.Text;

namespace AAEmu.Launcher.Core;

/// <summary>
/// Manages the shared memory IPC session for delivering an RC4-encrypted authentication ticket
/// to the game client and waiting for its read acknowledgment.
/// Handles are valid for the lifetime of this object; dispose to release them.
/// </summary>
internal sealed class SharedMemoryTicketSession : IDisposable
{
    private readonly MemoryMappedFile _mappedFile;
    private readonly EventWaitHandle _event;
    private bool _disposed;

    /// <summary>
    /// The native handle of the memory-mapped file, for passing to the game's -handle argument.
    /// </summary>
    public nint FileMapHandle { get { ThrowIfDisposed(); return field; } }

    /// <summary>
    /// The native handle of the event, for passing to the game's -handle argument.
    /// </summary>
    public nint EventHandle { get { ThrowIfDisposed(); return field; }  }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    /// <summary>
    /// Encrypts the ticket and writes it to shared memory. The event is created non-signaled;
    /// the game client will signal it after reading the ticket.
    /// </summary>
    /// <param name="ticketString">The plaintext ticket string (TFIR + signature + XML).</param>
    public SharedMemoryTicketSession(string ticketString)
    {
        var ticketBytes = Encoding.UTF8.GetBytes(ticketString);
        var rc4Key = RandomNumberGenerator.GetBytes(8);
        var encryptedData = Rc4.Encrypt(rc4Key, ticketBytes);

        // Layout: [8 bytes key][4 bytes length][N bytes encrypted data]
        var totalSize = 8 + 4 + encryptedData.Length;

        _mappedFile = MemoryMappedFile.CreateNew(
            mapName: null,
            totalSize,
            MemoryMappedFileAccess.ReadWrite,
            MemoryMappedFileOptions.None,
            HandleInheritability.Inheritable);

        // If anything below throws, dispose what we've already acquired.
        EventWaitHandle? localEvent = null;
        try
        {
            using var accessor = _mappedFile.CreateViewAccessor(0, totalSize);
            accessor.WriteArray(0, rc4Key, 0, rc4Key.Length);
            accessor.Write(8, encryptedData.Length);
            accessor.WriteArray(12, encryptedData, 0, encryptedData.Length);

            FileMapHandle = _mappedFile.SafeMemoryMappedFileHandle.DangerousGetHandle();

            localEvent = new EventWaitHandle(false, EventResetMode.ManualReset);
            EventHandle = localEvent.SafeWaitHandle.DangerousGetHandle();
            _event = localEvent;
        }
        catch
        {
            localEvent?.Dispose();
            _mappedFile.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Asynchronously waits for the game client to signal that it has read the ticket.
    /// Returns true if signaled, false if the timeout expired. Throws <see cref="OperationCanceledException"/>
    /// if <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    public Task<bool> WaitForGameReadAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<bool>(cancellationToken);

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var waitHandle = ThreadPool.RegisterWaitForSingleObject(
            _event,
            static (state, timedOut) => ((TaskCompletionSource<bool>)state!).TrySetResult(!timedOut),
            state: tcs,
            timeout,
            executeOnlyOnce: true);

        var ctReg = cancellationToken.Register(static state => ((TaskCompletionSource<bool>)state!).TrySetCanceled(), tcs);

        tcs.Task.ContinueWith(
            static (_, state) =>
            {
                var (reg, ctRegistration) = ((RegisteredWaitHandle, CancellationTokenRegistration))state!;
                reg.Unregister(null);
                ctRegistration.Dispose();
            },
            (waitHandle, ctReg),
            TaskContinuationOptions.ExecuteSynchronously);

        return tcs.Task;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _event.Dispose();
        _mappedFile.Dispose();
    }
}
