using System.Net.Sockets;
using AAEmu.Game.Models;
using NLog;

namespace AAEmu.Game.Core.Network.Bill;

/// <summary>TCP client for the retail Bill Server world-listener (:12345).</summary>
public sealed class BillClient : IDisposable
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly BillServerConfig _config;
    private readonly byte _worldId;
    private readonly SemaphoreSlim _ioLock = new(1, 1);
    private readonly List<byte> _rxBuffer = [];
    private readonly object _pendingGate = new();
    private TaskCompletionSource<(ushort Opcode, byte[] Body)>? _pending;

    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;
    private Task? _readerTask;
    private long _nextGuid = 1;

    public BillClient(BillServerConfig config, byte worldId)
    {
        _config = config;
        _worldId = worldId;
    }

    public bool IsConnected => _tcp?.Connected == true;

    public async Task<bool> ConnectAndJoinAsync(CancellationToken cancellationToken)
    {
        await _ioLock.WaitAsync(cancellationToken);
        try
        {
            DisposeSocket();

            _tcp = new TcpClient();
            await _tcp.ConnectAsync(_config.Host, _config.Port, cancellationToken);
            _stream = _tcp.GetStream();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _readerTask = Task.Run(() => ReadLoop(_cts.Token), _cts.Token);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Bill connect failed {0}:{1}", _config.Host, _config.Port);
            DisposeSocket();
            return false;
        }
        finally
        {
            _ioLock.Release();
        }

        try
        {
            var joinBody = new BillWriter();
            joinBody.WriteI32(4);
            joinBody.WriteI32(1);
            joinBody.WriteU8(_worldId);
            joinBody.WriteI32(0);

            var joinResp = await SendAndWaitAsync(
                BillOpcodes.Join, joinBody.ToArray(), BillOpcodes.Join, _config.RequestTimeoutMs, cancellationToken);
            var reader = new BillReader(joinResp);
            var resp = reader.ReadU16();
            if (resp != 0)
            {
                Logger.Warn("Bill join rejected resp={0}", resp);
                Disconnect();
                return false;
            }

            Logger.Info("Bill connected {0}:{1} worldId={2}", _config.Host, _config.Port, _worldId);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Bill join failed {0}:{1}", _config.Host, _config.Port);
            Disconnect();
            return false;
        }
    }

    public async Task<(int Cash, int Bonus)?> GetCashAsync(
        ulong accountId,
        string accountName,
        int charId,
        CancellationToken cancellationToken)
    {
        var body = new BillWriter();
        body.WriteU64(accountId);
        body.WriteString(accountName);
        body.WriteI64(0);
        body.WriteI32(charId);
        body.WriteU8(0);

        var respBody = await SendAndWaitAsync(BillOpcodes.GetCash, body.ToArray(), BillOpcodes.GetCash, _config.RequestTimeoutMs, cancellationToken);
        var r = new BillReader(respBody);
        var cash = r.ReadI32();
        var bonus = r.ReadI32();
        _ = r.ReadI64();
        _ = r.ReadI32();
        _ = r.ReadI64();
        return (cash, bonus);
    }

    public async Task<BillBuyResult?> BuyAsync(BillBuyRequest request, CancellationToken cancellationToken)
    {
        var guid = Interlocked.Increment(ref _nextGuid);
        var body = new BillWriter();
        body.WriteI64(0);
        body.WriteU64(request.AccountId);
        body.WriteString(request.BuyerName);
        body.WriteI32(request.BuyerCharId);
        body.WriteString(request.BuyerName);
        body.WriteU64(request.ReceiverAccountId);
        body.WriteString(request.ReceiverName);
        body.WriteI32(request.ReceiverCharId);
        body.WriteString(request.ReceiverName);
        body.WriteU32(0);
        body.WriteU8(1);
        body.WriteU32(0);
        body.WriteU32(0);

        for (var i = 0; i < 10; i++)
        {
            if (i < request.Slots.Count)
            {
                var slot = request.Slots[i];
                body.WriteI32((int)slot.Price);
                body.WriteU16(slot.PriceType);
                body.WriteU32(slot.CashShopId);
                body.WriteU8(slot.LimitType);
                body.WriteU32(slot.BuyLimit);
            }
            else
            {
                body.WriteI32(0);
                body.WriteU16(0);
                body.WriteU32(0);
                body.WriteU8(0);
                body.WriteU32(0);
            }
        }

        body.WriteI64(guid);

        var respBody = await SendAndWaitAsync(BillOpcodes.Buy, body.ToArray(), BillOpcodes.Buy, _config.RequestTimeoutMs, cancellationToken);
        var r = new BillReader(respBody);
        _ = r.ReadI64();
        _ = r.ReadI32();
        _ = r.ReadI64();
        var resp = r.ReadU16();
        var productResp = new ushort[10];
        for (var i = 0; i < 10; i++)
            productResp[i] = r.ReadU16();
        _ = r.ReadI32();
        var cashAmount = r.ReadU32();
        var bonusAmount = r.ReadU32();
        var buyCodes = new long[10];
        for (var i = 0; i < 10; i++)
            buyCodes[i] = r.ReadI64();

        return new BillBuyResult(resp, productResp, cashAmount, bonusAmount, buyCodes);
    }

    public async Task SendHeartbeatAsync(CancellationToken cancellationToken)
    {
        if (!IsConnected)
            return;

        var body = new BillWriter();
        body.WriteU64((ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        _ = await SendAndWaitAsync(BillOpcodes.Heartbeat, body.ToArray(), BillOpcodes.Heartbeat, _config.RequestTimeoutMs, cancellationToken);
    }

    public void Disconnect()
    {
        if (!_ioLock.Wait(0))
        {
            DisposeSocket();
            return;
        }

        try
        {
            DisposeSocket();
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public void Dispose()
    {
        Disconnect();
        _ioLock.Dispose();
    }

    private async Task<byte[]> SendAndWaitAsync(
        ushort sendOpcode,
        byte[] body,
        ushort expectOpcode,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        if (_stream is null)
            throw new InvalidOperationException("Bill not connected");

        await _ioLock.WaitAsync(cancellationToken);
        try
        {
            var tcs = new TaskCompletionSource<(ushort, byte[])>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_pendingGate)
            {
                if (_pending is not null)
                    throw new InvalidOperationException("Bill request already in flight");
                _pending = tcs;
            }

            var frame = BillFrame.Encode(sendOpcode, body);
            await _stream.WriteAsync(frame, cancellationToken);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(timeoutMs);
            try
            {
                var (opcode, respBody) = await tcs.Task.WaitAsync(timeout.Token);
                if (opcode != expectOpcode)
                    throw new InvalidOperationException($"Bill unexpected opcode {opcode} expected {expectOpcode}");
                return respBody;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                lock (_pendingGate)
                    _pending = null;
                throw new TimeoutException($"Bill request 0x{sendOpcode:X} timed out");
            }
        }
        finally
        {
            _ioLock.Release();
        }
    }

    private void ReadLoop(CancellationToken cancellationToken)
    {
        var scratch = new byte[8192];
        try
        {
            while (!cancellationToken.IsCancellationRequested && _stream is not null)
            {
                var read = _stream.Read(scratch, 0, scratch.Length);
                if (read <= 0)
                    break;

                lock (_rxBuffer)
                {
                    for (var i = 0; i < read; i++)
                        _rxBuffer.Add(scratch[i]);

                    while (BillFrame.TryReadFrame(_rxBuffer, out var opcode, out var body))
                        DispatchFrame(opcode, body);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            Logger.Debug(ex, "Bill read loop ended");
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Bill read loop error");
        }
        finally
        {
            FailPending(new IOException("Bill disconnected"));
        }
    }

    private void DispatchFrame(ushort opcode, byte[] body)
    {
        lock (_pendingGate)
        {
            if (_pending is null)
            {
                if (opcode != BillOpcodes.Heartbeat)
                    Logger.Trace("Bill unsolicited opcode={0} len={1}", opcode, body.Length);
                return;
            }

            _pending.TrySetResult((opcode, body));
            _pending = null;
        }
    }

    private void FailPending(Exception ex)
    {
        lock (_pendingGate)
        {
            _pending?.TrySetException(ex);
            _pending = null;
        }
    }

    private void DisposeSocket()
    {
        try
        {
            _cts?.Cancel();
        }
        catch
        {
            // ignore
        }

        try
        {
            _stream?.Close();
        }
        catch
        {
            // ignore
        }

        try
        {
            _tcp?.Close();
        }
        catch
        {
            // ignore
        }

        _stream = null;
        _tcp = null;
        _cts?.Dispose();
        _cts = null;
        lock (_rxBuffer)
            _rxBuffer.Clear();
        FailPending(new IOException("Bill socket closed"));
    }
}

public sealed class BillBuyRequest
{
    public ulong AccountId { get; init; }
    public string BuyerName { get; init; } = "";
    public int BuyerCharId { get; init; }
    public ulong ReceiverAccountId { get; init; }
    public string ReceiverName { get; init; } = "";
    public int ReceiverCharId { get; init; }
    public List<BillBuySlot> Slots { get; init; } = [];
}

public readonly struct BillBuySlot
{
    public BillBuySlot(uint price, ushort priceType, uint cashShopId, byte limitType, uint buyLimit)
    {
        Price = price;
        PriceType = priceType;
        CashShopId = cashShopId;
        LimitType = limitType;
        BuyLimit = buyLimit;
    }

    public uint Price { get; }
    public ushort PriceType { get; }
    public uint CashShopId { get; }
    public byte LimitType { get; }
    public uint BuyLimit { get; }
}

public readonly struct BillBuyResult
{
    public BillBuyResult(ushort resp, ushort[] productResp, uint cashAmount, uint bonusAmount, long[] buyCodes)
    {
        Resp = resp;
        ProductResp = productResp;
        CashAmount = cashAmount;
        BonusAmount = bonusAmount;
        BuyCodes = buyCodes;
    }

    public ushort Resp { get; }
    public ushort[] ProductResp { get; }
    public uint CashAmount { get; }
    public uint BonusAmount { get; }
    public long[] BuyCodes { get; }

    public bool IsSuccess => Resp == 0 && ProductResp.All(p => p == 0);
}
