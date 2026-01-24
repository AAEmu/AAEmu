using System.Globalization;

namespace AAEmu.Login.Models;

public readonly record struct ConnectionId(uint Value)
{
    public override string ToString()
    {
        return Value.ToString(CultureInfo.InvariantCulture);
    }
}
