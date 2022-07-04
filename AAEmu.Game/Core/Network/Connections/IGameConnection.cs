using System.Collections.Generic;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Network.Connections
{
    public interface IGameConnection
    {
        Dictionary<uint, ICharacter> Characters { get; }
    }
}
