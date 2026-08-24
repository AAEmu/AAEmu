namespace AAEmu.BillServer.Protocol;

/// <summary>
/// WorldToBill / BillToWorld opcodes mirror each other.
/// </summary>
public static class BillOpcodes
{
    public const ushort GetCash = 0;
    public const ushort Buy = 1;
    public const ushort Join = 2;
    public const ushort Heartbeat = 3;
    public const ushort BuyConfirm = 4;
    public const ushort BillMsg = 6;
    public const ushort ActiveItem = 7;
    public const ushort LoginLoadEx = 10;
    public const ushort BuyCount = 11;
    public const ushort LeaveWorld = 16;
    public const ushort PlayersInWorld = 17;
    public const ushort PlayerInWorld = 18;
    public const ushort DailyPurchaseLimitReset = 20;
    public const ushort GmAddCash = 21;
}
