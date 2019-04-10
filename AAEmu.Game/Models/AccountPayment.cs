using System;
using AAEmu.Game.Core.Network.Connections;

namespace AAEmu.Game.Models
{
    public class AccountPayment
    {
        private GameConnection _connection;

        public PaymentMethodType Method { get; set; } = PaymentMethodType.Premium;
        public int Location { get; set; } = 1;

        public DateTime StartTime { get; set; } = DateTime.MinValue;
        public DateTime EndTime { get; set; } = new DateTime(2020, 1, 1);

        public AccountPayment(GameConnection connection)
        {
            _connection = connection;
        }
    }

    public enum PaymentMethodType
    {
        Premium = 1,
        Demo = 3,
        None = 5
    }
}
