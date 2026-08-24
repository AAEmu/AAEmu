#!/usr/bin/env python3
"""
Minimal test client for the ArcheAge Bill Server world-listener (default :12345).

Speaks the reverse-engineered binary protocol (see world-protocol.md):
    frame = [u16 length][u16 opcode][binary body]   (all little-endian, plaintext)
    length = len(opcode + body)  (i.e. everything after the length field), max 65534
    string on wire = [u16 length][raw bytes]  (no NUL, no capacity)
    primitives = raw little-endian, widths u8/u16/u32/u64

Flow: Join (op2) -> GetBalance (op0) -> Buy (op1), printing each reply.

Even with cash_db disabled the handlers send reject responses, so this still
exercises framing/serialization end-to-end. Run bill_server_db.exe first.

    python test_client.py --host 127.0.0.1 --port 12345 \
        --account 10001 --char 20001 --world 1
"""
import argparse
import socket
import struct
import sys

# ---- archive primitive encoders (little-endian, raw) ----
def u8(v):  return struct.pack("<B", v & 0xFF)
def u16(v): return struct.pack("<H", v & 0xFFFF)
def i32(v): return struct.pack("<i", v)
def u32(v): return struct.pack("<I", v & 0xFFFFFFFF)
def i64(v): return struct.pack("<q", v)
def u64(v): return struct.pack("<Q", v & 0xFFFFFFFFFFFFFFFF)

def s(text):
    """String field: u16 length prefix + raw bytes (matches sub_140022D40)."""
    b = text.encode("utf-8")
    return u16(len(b)) + b

# ---- opcodes (WorldToBillPacketType) ----
OP_GETCASH, OP_BUY, OP_JOIN = 0, 1, 2

def frame(opcode, body):
    """Wrap [u16 opcode][body] with the u16 length prefix."""
    payload = u16(opcode) + body
    if len(payload) > 65534:
        raise ValueError("packet too large")
    return u16(len(payload)) + payload

# ---- outbound packet builders ----
def build_join(world_id, heartbeat=0):
    # WBJoinRequest: p_from(i32=4) p_to(i32=1) worldId(u8) heartbeat(i32)
    return frame(OP_JOIN, i32(4) + i32(1) + u8(world_id) + i32(heartbeat))

def build_getcash(account_id, account_name, applier, char_id, ctype=0):
    # WBGetCash: accountId(u64) account(str) applier(i64) charId(i32) type(u8)
    return frame(OP_GETCASH,
                 u64(account_id) + s(account_name) + i64(applier) + i32(char_id) + u8(ctype))

def item_slot(price, price_type, cash_shop_id, limit_type=0, buy_limit=0):
    # per-slot: price(i32) priceType(u16) cashId(u32) limitType(u8) buyLimit(u32)  (15 B)
    return i32(price) + u16(price_type) + u32(cash_shop_id) + u8(limit_type) + u32(buy_limit)

def build_buy(applier, account_id, aname, char_id, cname,
              recv_account_id, raname, recv_char_id, rcname,
              ip, buy_source, activity_id, auction_config_id, slots, guid):
    body  = i64(applier) + u64(account_id) + s(aname) + i32(char_id) + s(cname)
    body += u64(recv_account_id) + s(raname) + i32(recv_char_id) + s(rcname)
    body += u32(ip) + u8(buy_source) + u32(activity_id) + u32(auction_config_id)
    # exactly 10 slots on the wire; pad with empty slots
    padded = (list(slots) + [item_slot(0, 0, 0)] * 10)[:10]
    body += b"".join(padded)
    body += i64(guid)
    return frame(OP_BUY, body)

# ---- response reader ----
class Reader:
    def __init__(self, data): self.d, self.o = data, 0
    def take(self, n):
        v = self.d[self.o:self.o + n]; self.o += n; return v
    def u8(self):  return self.take(1)[0]
    def u16(self): return struct.unpack_from("<H", self.take(2))[0]
    def i32(self): return struct.unpack_from("<i", self.take(4))[0]
    def u32(self): return struct.unpack_from("<I", self.take(4))[0]
    def i64(self): return struct.unpack_from("<q", self.take(8))[0]
    def u64(self): return struct.unpack_from("<Q", self.take(8))[0]

def recv_frame(sock):
    hdr = recv_exact(sock, 2)
    if hdr is None: return None
    length = struct.unpack("<H", hdr)[0]
    body = recv_exact(sock, length)
    if body is None: return None
    return body  # [u16 opcode][fields]

def recv_exact(sock, n):
    buf = b""
    while len(buf) < n:
        try:
            chunk = sock.recv(n - len(buf))
        except socket.timeout:
            return None
        if not chunk:
            return None
        buf += chunk
    return buf

def decode_response(body):
    r = Reader(body)
    op = r.u16()
    hexdump = body.hex()
    if op == OP_JOIN:      # BWJoinResponse: resp u16
        return f"JoinResponse   resp={r.u16()}"
    if op == OP_GETCASH:   # amount i32, bonus i32, applier i64, charId i32, guid i64
        amount, bonus = r.i32(), r.i32()
        applier, char_id, guid = r.i64(), r.i32(), r.i64()
        return f"GetCashResponse cash={amount} bonus={bonus} charId={char_id} applier={applier} guid={guid}"
    if op == OP_BUY:       # applier i64, charId i32, guid i64, resp u16, productResp[10] u16, lra i32, cash u32, bonus u32, buyCode[10] i64
        applier, char_id, guid = r.i64(), r.i32(), r.i64()
        resp = r.u16()
        product = [r.u16() for _ in range(10)]
        lra, cash, bonus = r.i32(), r.u32(), r.u32()
        buy_code = [r.i64() for _ in range(10)]
        return (f"BuyResponse    resp={resp} cash={cash} bonus={bonus} lra={lra} "
                f"productResp={product[:4]}... buyCode={buy_code[:2]}...")
    return f"opcode={op} (undecoded) body={hexdump}"

def send_and_read(sock, label, data, expect_reply=True):
    print(f"\n>> {label}: {len(data)}B  {data.hex()}")
    sock.sendall(data)
    if not expect_reply:
        return
    body = recv_frame(sock)
    if body is None:
        print("   << (no reply / timeout)")
        return
    print(f"   << {len(body)}B  {body.hex()}")
    print(f"   == {decode_response(body)}")

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--host", default="127.0.0.1")
    ap.add_argument("--port", type=int, default=12345)
    ap.add_argument("--world", type=int, default=1)
    ap.add_argument("--account", type=int, default=10001)
    ap.add_argument("--char", type=int, default=20001)
    ap.add_argument("--timeout", type=float, default=5.0)
    a = ap.parse_args()

    with socket.create_connection((a.host, a.port), timeout=a.timeout) as sock:
        sock.settimeout(a.timeout)
        print(f"connected to {a.host}:{a.port}")

        # 1) handshake — must be first
        send_and_read(sock, "Join", build_join(a.world, heartbeat=0))

        # 2) balance query
        send_and_read(sock, "GetBalance",
                      build_getcash(a.account, "test", applier=a.account,
                                    char_id=a.char, ctype=0))

        # 3) buy one cash item (price_type 0 = AA_CASH); shop_id matches bill seed catalog
        slots = [item_slot(price=100, price_type=0, cash_shop_id=2000000)]
        send_and_read(sock, "Buy",
                      build_buy(applier=a.account, account_id=a.account, aname="test",
                                char_id=a.char, cname="tester",
                                recv_account_id=a.account, raname="test",
                                recv_char_id=a.char, rcname="tester",
                                ip=0x0100007F, buy_source=1,
                                activity_id=0, auction_config_id=0,
                                slots=slots, guid=1))
        print("\ndone.")

if __name__ == "__main__":
    try:
        main()
    except (ConnectionRefusedError, socket.timeout) as e:
        print(f"connection failed: {e} — is bill_server_db.exe listening on :12345?", file=sys.stderr)
        sys.exit(1)
