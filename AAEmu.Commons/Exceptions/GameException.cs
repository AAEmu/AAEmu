using System;

namespace AAEmu.Commons.Exceptions;
public class GameException : Exception
{
    public GameException(string message) : base(message)
    {
    }
}
