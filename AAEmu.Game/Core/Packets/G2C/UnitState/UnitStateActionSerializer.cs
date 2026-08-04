using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.G2C.UnitState;

public static class UnitStateActionSerializer
{
    public static void Write(PacketStream stream, UnitStateAction action)
    {
        stream.Write(action.StateType);
        if (action.StateType == UnitStateAction.None)
            return;

        stream.Write(action.Time);
        stream.Write(action.Duration);
        stream.Write(action.MaxHorizontalVelocity);
        stream.Write(action.MaxVerticalVelocity);
    }
}
