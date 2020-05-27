using System.Collections.Generic;
using AAEmu.Commons.Network.Share.Character;

namespace AAEmu.Commons.Messages
{
    public class RequestInfoResponse
    {
        public List<CharacterInfo> Characters { get; set; }
        
        #region Overrides of Object
        public override string ToString() => $"RequestInfoResponse(CharactersCount={Characters.Count})";
        #endregion
    }
}
