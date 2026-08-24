using System.Net;
using System.Net.Sockets;
using AAEmu.BillServer.Cash;
using AAEmu.BillServer.Protocol;
using NLog;

namespace AAEmu.BillServer.Network;

/// <summary>World→Bill binary listener (retail :12345).</summary>
public sealed class BillWorldListener
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private readonly TcpListener _listener;
    private readonly ICashStore _cash;
    private readonly ICatalogStore _catalog;
    private CancellationTokenSource? _cts;

    public BillWorldListener(IPAddress host, int port, ICashStore cash, ICatalogStore catalog)
    {
        _listener = new TcpListener(host, port);
        _cash = cash;
        _catalog = catalog;
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listener.Start();
        Log.Info("bill service start to listen on {0}", _listener.LocalEndpoint);
        _ = AcceptLoop(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _listener.Stop();
    }

    private async Task AcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(ct);
                _ = Task.Run(() => SessionLoop(client, ct), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "accept failed");
            }
        }
    }

    private async Task SessionLoop(TcpClient client, CancellationToken ct)
    {
        var ep = client.Client.RemoteEndPoint?.ToString() ?? "?";
        Log.Info("world session from {0}", ep);
        var buf = new List<byte>();
        var stream = client.GetStream();
        var scratch = new byte[8192];
        byte worldId = 0;
        var joined = false;

        try
        {
            while (!ct.IsCancellationRequested && client.Connected)
            {
                var n = await stream.ReadAsync(scratch.AsMemory(0, scratch.Length), ct);
                if (n <= 0)
                    break;
                buf.AddRange(scratch.AsSpan(0, n).ToArray());
                while (BillFrame.TryReadFrame(buf, out var opcode, out var body))
                {
                    var reply = Handle(opcode, body, ref joined, ref worldId);
                    if (reply is { Length: > 0 })
                        await stream.WriteAsync(reply, ct);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Debug(ex, "session {0} closed", ep);
        }
        finally
        {
            client.Close();
            Log.Info("world session closed {0}", ep);
        }
    }

    private byte[]? Handle(ushort opcode, byte[] body, ref bool joined, ref byte worldId)
    {
        try
        {
            var wid = worldId;
            return opcode switch
            {
                BillOpcodes.Join => OnJoin(body, ref joined, ref worldId),
                BillOpcodes.Heartbeat => OnHeartbeat(),
                BillOpcodes.GetCash => RequireJoined(joined, () => OnGetCash(body)),
                BillOpcodes.Buy => RequireJoined(joined, () => OnBuy(body, wid)),
                BillOpcodes.BuyConfirm => RequireJoined(joined, () => OnBuyConfirm(body)),
                BillOpcodes.BuyCount => RequireJoined(joined, () => OnBuyCount(body)),
                BillOpcodes.GmAddCash => RequireJoined(joined, () => OnGmAdd(body, wid)),
                BillOpcodes.LeaveWorld => OnLeaveWorld(body),
                BillOpcodes.LoginLoadEx => RequireJoined(joined, () => OnLoginLoadEx(body)),
                BillOpcodes.ActiveItem => RequireJoined(joined, () => OnActiveItem(body, wid)),
                BillOpcodes.BillMsg or BillOpcodes.DailyPurchaseLimitReset
                    or BillOpcodes.PlayerInWorld or BillOpcodes.PlayersInWorld => null,
                _ => LogUnknown(opcode)
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "handle opcode {0}", opcode);
            return null;
        }
    }

    private static byte[]? RequireJoined(bool joined, Func<byte[]?> action) =>
        joined ? action() : null;

    private static byte[]? LogUnknown(ushort opcode)
    {
        Log.Warn("unhandled WB opcode {0}", opcode);
        return null;
    }

    private static byte[] OnJoin(byte[] body, ref bool joined, ref byte worldId)
    {
        var r = new BillReader(body);
        var pFrom = r.ReadI32();
        var pTo = r.ReadI32();
        worldId = r.ReadU8();
        _ = r.ReadI32(); // heartbeat
        var w = new BillWriter();
        if (pFrom != 4 || pTo != 1)
        {
            Log.Warn("wrong protocol version p_from={0} p_to={1}", pFrom, pTo);
            w.WriteU16(1); // fail
            return BillFrame.Encode(BillOpcodes.Join, w.ToArray());
        }

        joined = true;
        w.WriteU16(0); // ok
        Log.Info("WBJoin worldId={0}", worldId);
        return BillFrame.Encode(BillOpcodes.Join, w.ToArray());
    }

    private static byte[] OnHeartbeat()
    {
        var w = new BillWriter();
        w.WriteU64((ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        return BillFrame.Encode(BillOpcodes.Heartbeat, w.ToArray());
    }

    private byte[] OnGetCash(byte[] body)
    {
        var r = new BillReader(body);
        var accountId = r.ReadU64();
        _ = r.ReadString();
        var applier = r.ReadI64();
        var charId = r.ReadI32();
        _ = r.ReadU8();
        var wallet = _cash.GetBalance(accountId);
        var w = new BillWriter();
        w.WriteI32(wallet.Cash);
        w.WriteI32(wallet.BonusCash);
        w.WriteI64(applier);
        w.WriteI32(charId);
        w.WriteI64(0); // guid
        Log.Debug("GetCash account={0} cash={1} bonus={2}", accountId, wallet.Cash, wallet.BonusCash);
        return BillFrame.Encode(BillOpcodes.GetCash, w.ToArray());
    }

    private byte[] OnBuy(byte[] body, byte worldId)
    {
        var r = new BillReader(body);
        var applier = r.ReadI64();
        var accountId = r.ReadU64();
        _ = r.ReadString();
        var cid = r.ReadI32();
        _ = r.ReadString();
        _ = r.ReadU64();
        _ = r.ReadString();
        _ = r.ReadI32();
        _ = r.ReadString();
        _ = r.ReadU32();
        var buySource = r.ReadU8();
        _ = r.ReadU32();
        _ = r.ReadU32();

        var slots = new List<(uint price, ushort priceType, uint cashShopId, byte limitType, uint buyLimit)>();
        for (var i = 0; i < 10; i++)
        {
            var price = r.ReadI32();
            var priceType = r.ReadU16();
            var cashShopId = r.ReadU32();
            var limitType = r.ReadU8();
            var buyLimit = r.ReadU32();
            slots.Add(((uint)Math.Max(0, price), priceType, cashShopId, limitType, buyLimit));
        }

        var guid = r.ReadI64();

        ushort resp = 0;
        var productResp = new ushort[10];
        var buyCodes = new long[10];
        uint totalCash = 0;
        var wallet = _cash.GetBalance(accountId);

        for (var i = 0; i < 10; i++)
        {
            var (price, priceType, cashShopId, limitType, buyLimit) = slots[i];
            if (cashShopId == 0 || price == 0)
            {
                productResp[i] = 0;
                continue;
            }

            var product = _catalog.Get(cashShopId);
            if (product is null || product.Value.Available == 0)
            {
                productResp[i] = 2; // unavailable
                resp = 1;
                continue;
            }

            var effective = product.Value.DiscountPrice > 0 ? product.Value.DiscountPrice : product.Value.Price;
            // Prefer catalog price; fall back to client-sent price
            if (effective == 0)
                effective = price;

            if (product.Value.BuyLimit > 0)
            {
                var count = _cash.GetBuyCount(accountId, cid, (int)cashShopId, limitType);
                if (count >= product.Value.BuyLimit)
                {
                    productResp[i] = 3; // limit
                    resp = 1;
                    continue;
                }
            }

            var useType = product.Value.PriceType != 0 ? product.Value.PriceType : priceType;
            var opId = $"BUY-{accountId}-{worldId}-{guid}-{cashShopId}-{effective}";
            var after = _cash.Debit(opId, accountId, cid, worldId, (int)effective, useType, buySource == 1 ? "ingame_shop" : "unknown");
            if (after is null)
            {
                productResp[i] = 1; // insufficient
                resp = 1;
                continue;
            }

            wallet = after.Value;
            _cash.RecordBuySlot(guid, accountId, cid, buySource, i, (int)cashShopId, useType, (int)effective, limitType, (int)buyLimit, "ingame_shop");
            productResp[i] = 0;
            buyCodes[i] = guid + i;
            totalCash = (uint)wallet.Cash;
        }

        var w = new BillWriter();
        w.WriteI64(applier);
        w.WriteI32(cid);
        w.WriteI64(guid);
        w.WriteU16(resp);
        foreach (var pr in productResp)
            w.WriteU16(pr);
        w.WriteI32(0); // lra
        w.WriteU32(totalCash != 0 ? totalCash : (uint)wallet.Cash);
        w.WriteU32((uint)wallet.BonusCash);
        foreach (var bc in buyCodes)
            w.WriteI64(bc);
        return BillFrame.Encode(BillOpcodes.Buy, w.ToArray());
    }

    private byte[] OnBuyConfirm(byte[] body)
    {
        var r = new BillReader(body);
        var guid = r.ReadI64();
        _ = r.ReadU16();
        _ = r.ReadU16();
        var applier = r.ReadI64();
        var cid = r.ReadI32();
        var composite = r.ReadI64();
        var amount = r.ReadU32();
        var shopIds = new uint[10];
        for (var i = 0; i < 10; i++)
            shopIds[i] = r.ReadU32();

        var confirmed = new List<uint>();
        foreach (var id in shopIds)
        {
            if (id == 0)
                continue;
            _cash.ConfirmBuy(guid, cid, (int)id);
            confirmed.Add(id);
        }

        var w = new BillWriter();
        w.WriteI64(guid);
        w.WriteU16(0);
        w.WriteI16((short)confirmed.Count);
        foreach (var id in confirmed)
            w.WriteU32(id);
        foreach (var _ in confirmed)
            w.WriteU8(0); // remainBuyCount
        w.WriteI64(applier);
        w.WriteI32(cid);
        w.WriteI64(composite);
        w.WriteU32(amount);
        return BillFrame.Encode(BillOpcodes.BuyConfirm, w.ToArray());
    }

    private byte[] OnBuyCount(byte[] body)
    {
        var r = new BillReader(body);
        var composite = r.ReadI64();
        var accountId = r.ReadU64();
        var charId = r.ReadI32();
        var count = r.ReadI16();
        var pairs = new List<(uint pId, byte limitType)>();
        for (var i = 0; i < count; i++)
            pairs.Add((r.ReadU32(), r.ReadU8()));
        var kind = r.Remaining >= 4 ? r.ReadU32() : 0u;

        var w = new BillWriter();
        w.WriteI64(composite);
        w.WriteI16((short)pairs.Count);
        foreach (var (pId, _) in pairs)
            w.WriteU32(pId);
        foreach (var (pId, lt) in pairs)
            w.WriteU32((uint)_cash.GetBuyCount(accountId, charId, (int)pId, lt));
        w.WriteU32(kind);
        return BillFrame.Encode(BillOpcodes.BuyCount, w.ToArray());
    }

    private byte[] OnGmAdd(byte[] body, byte worldId)
    {
        var r = new BillReader(body);
        var accountId = r.ReadU64();
        var charId = r.ReadI32();
        var amount = r.ReadU32();
        var priceType = r.ReadU16();
        var noticeType = r.ReadU8();
        var requestId = r.ReadI64();
        var opId = $"GMADD-{accountId}-{worldId}-{requestId}";
        var after = _cash.Credit(opId, accountId, charId, worldId, (int)amount, priceType, "gm_command")
                    ?? _cash.GetBalance(accountId);
        var w = new BillWriter();
        w.WriteI32(charId);
        w.WriteU32((uint)after.Cash);
        w.WriteU32((uint)after.BonusCash);
        w.WriteU8(noticeType);
        Log.Info("GmAddCash account={0} amount={1} type={2}", accountId, amount, priceType);
        return BillFrame.Encode(BillOpcodes.GmAddCash, w.ToArray());
    }

    private static byte[] OnLeaveWorld(byte[] body)
    {
        _ = body;
        var w = new BillWriter();
        w.WriteU16(0);
        return BillFrame.Encode(BillOpcodes.LeaveWorld, w.ToArray());
    }

    private static byte[] OnLoginLoadEx(byte[] body)
    {
        var r = new BillReader(body);
        var composite = r.ReadI64();
        var accountId = r.ReadU64();
        _ = r.ReadString();
        _ = r.ReadI64();
        _ = r.ReadI32();
        _ = r.ReadU8();
        if (r.Remaining > 0)
            _ = r.ReadString();
        var w = new BillWriter();
        w.WriteI64(composite);
        w.WriteU64(accountId);
        w.WriteU32(0); // payMethod
        w.WriteU32(0); // payLocation
        w.WriteI64(0); // billId
        w.WriteU64(0); // startAt
        w.WriteU64(0); // endAt
        w.WriteI64(0); // realPayTimeSec
        w.WriteU32(0); // buyPremiumCount
        return BillFrame.Encode(BillOpcodes.LoginLoadEx, w.ToArray());
    }

    private byte[] OnActiveItem(byte[] body, byte worldId)
    {
        var r = new BillReader(body);
        var applier = r.ReadI64();
        var tkey = r.ReadU32();
        var requestId = r.ReadI64();
        _ = r.ReadI32();
        var accountId = r.ReadU64();
        _ = r.ReadString();
        var cid = r.ReadI32();
        // remainder ignored
        var wallet = _cash.GetBalance(accountId);
        var w = new BillWriter();
        w.WriteI64(applier);
        w.WriteU32(tkey);
        w.WriteU32((uint)cid);
        w.WriteU16(0);
        w.WriteU32((uint)wallet.Cash);
        w.WriteU32((uint)wallet.BonusCash);
        w.WriteI64(requestId);
        return BillFrame.Encode(BillOpcodes.ActiveItem, w.ToArray());
    }
}
