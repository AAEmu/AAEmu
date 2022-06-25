using System;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Observers
{
    public class TimeOfDayObserver : IObserver<float>
    {
        private readonly ICharacter _owner;

        public TimeOfDayObserver(ICharacter owner)
        {
            _owner = owner;
        }

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
            _owner.SendPacket(new SCTimeOfDayPacket(value));
        }
    }
}
