using System;
using System.Diagnostics.CodeAnalysis;

namespace AAEmu.Commons.Exceptions
{
    [ExcludeFromCodeCoverage]
    public class GameException : Exception
    {
        public GameException(string message) : base(message)
        {
        }

        public GameException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
