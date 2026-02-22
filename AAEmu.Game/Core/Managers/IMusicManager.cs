using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Music;

namespace AAEmu.Game.Core.Managers;

public interface IMusicManager : ILoadable
{
    bool Save(SongData songData);
    void UploadSong(uint charId, string title, string song, ulong itemId);
    bool CreateSheetMusic(Character player, Item sourceItem);
}
