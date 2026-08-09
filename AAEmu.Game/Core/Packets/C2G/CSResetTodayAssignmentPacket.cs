using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Daily schedule Change Mission: re-roll an in-progress real_step.
/// moneyAmount is bag copper charged for paid re-rolls (0 while free budget remains).
/// </summary>
public class CSResetTodayAssignmentPacket() : GamePacket(CSOffsets.CSResetTodayAssignmentPacket, 1)
{
    public uint RealStep { get; private set; }
    public ulong MoneyAmount { get; private set; }

    public override void Read(PacketStream stream)
    {
        RealStep = stream.ReadUInt32();
        MoneyAmount = stream.ReadUInt64();
    }

    public override void Execute()
    {
        TodayAssignmentManager.Instance.HandleReset(
            Connection.ActiveChar,
            RealStep,
            MoneyAmount);
    }
}
