using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Weather;

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
        var tod = StormShipLogic.ResolveClientTimeOfDayHours(owner) ?? value;
        owner.SendPacket(new SCTimeOfDayPacket(tod));
    }
}
