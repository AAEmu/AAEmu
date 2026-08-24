# AAEmu Bill Server

Retail-style **X2 Bill Server** reconstruction for World↔Bill wallet / buy, plus a catalog API
controlled by **AAEmu.BillManager**. Protocol source: Nikes RE (`re/research/bill-server-10.0.2.13/`,
from `bill-server.zip`).

## Topology

```
World (future) ──TCP :12345──► AAEmu.BillServer ──(optional MySQL aaemu_bill)
Client ICS SC   ──(still World)──► ICS tables ◄── Publish from Bill catalog
BillManager     ──HTTP :18080──► admin (catalog / cash / publish)
```

Bill can be stopped for maintenance; World should fail shop closed when Bill is down (World
client not wired yet — protocol side is ready).

## Build & run

```bat
cd AAEmu.BillServer
dotnet build -c Debug
dotnet run -c Debug
```

Admin: `http://127.0.0.1:18080/status`  
World protocol: `0.0.0.0:12345`

Verify:

```bat
python Scripts\test_bill_client.py --host 127.0.0.1 --port 12345 --account 10001 --char 20001
```

Seed memory wallet: account **10001** gets 1000 cash + 100 bonus at boot.

## Catalog fields (BillManager)

| Field | Meaning |
|-------|---------|
| `available` | **1** = on cash shop, **0** = hidden / not sellable |
| `price` / `discount_price` | charge amount (discount used if &gt; 0) |
| `buy_limit` | max purchases (0 = unlimited) |
| `limit_type` | 0 none, 1 account, 2 character (ICS sync) |
| `ics_currency` | Credits=0, AaPoints=1, Loyalty=2, Coins=3 when published |

**Publish→ICS** writes available rows into `aaemu_game.ics_*` (shop_id 2000000–2999999). Then in-game `/ics reload` (shop must be off during reload). Empty spinner needs World goods-path fix too.

## MySQL (optional)

```bat
docker exec -i aaemu-mysql mysql -uroot -ppassword < SQL/aaemu_bill.sql
```

Set `Config.json` `UseMysql: true` and connection strings.

## BillManager

```bat
dotnet run --project AAEmu.BillManager -c Debug
```

Start/Stop BillServer, edit catalog grid, grant cash, publish.
