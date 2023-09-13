using System.Diagnostics.CodeAnalysis;

namespace AAEmu.Commons.Exceptions
{
    [ExcludeFromCodeCoverage]
    public sealed class MarshalException : GameException // next: is it necessary?
    {
        public MarshalException() : base("Marshal exception")
        {
        }
    }
}