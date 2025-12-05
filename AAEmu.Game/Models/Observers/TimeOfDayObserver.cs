using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Observers;

public class TimeOfDayObserver(Character owner) : IObserver<float>
{
    public void OnCompleted()
    {
        throw new NotImplementedException();
    }

    public void OnError(Exception error)
    {
        throw new NotImplementedException();
    }

    public void OnNext(float value)
    {
        owner.SendPacket(new SCTimeOfDayPacket(value));
    }
}