using AAEmu.Commons.Network;
namespace AAEmu.Game.Models.Game.Units;

public static class UnitModelPostureSerializer
{
    private const byte AllHouseOpeningsOpen = byte.MaxValue;

    public static void Write(PacketStream stream, Unit unit, uint animActionId, bool activateAnimation)
    {
        stream.Write((byte)unit.ModelPostureType);
        stream.Write(unit.IsLooted);

        switch (unit.ModelPostureType)
        {
            case ModelPostureType.HouseState:
                stream.Write(AllHouseOpeningsOpen);
                break;
            case ModelPostureType.ActorModelState:
                stream.Write(animActionId);
                stream.Write(activateAnimation);
                break;
            case ModelPostureType.FarmfieldState:
                WriteEmptyFarmfieldState(stream);
                break;
            case ModelPostureType.TurretState:
                stream.Write(0f); // pitch
                stream.Write(0f); // yaw
                break;
        }
    }

    private static void WriteEmptyFarmfieldState(PacketStream stream)
    {
        stream.Write(0u);
        stream.Write(0f);
        stream.Write(0u);
        stream.Write((byte)0);
    }
}
