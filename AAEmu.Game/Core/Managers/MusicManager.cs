using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLog;

namespace AAEmu.Game.Core.Managers
{
    class SongUploadData
    {
        public string Title;
        public string Song;
        public ulong SourceItemId;
    }
    
    public class MusicManager : Singleton<MusicManager>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        private Dictionary<uint, SongUploadData> _uploadQueue;

        public MusicManager()
        {
            
        }

        public void Initialize()
        {
            _uploadQueue = new Dictionary<uint, SongUploadData>();
        }

        public void UploadSong(uint charId, string title, string song, ulong itemId)
        {
            if (!_uploadQueue.TryGetValue(charId, out var q))
            {
                q = new SongUploadData();
                _uploadQueue.Add(charId, q);
            }
            q.Title = title;
            q.Song = song;
            q.SourceItemId = itemId;
        }

        public bool CreateSheetMusic(Character player, Item sourceItem)
        {
            // Check if a valid owned item
            if ((sourceItem == null) || (sourceItem._holdingContainer.Owner.Id != player.Id))
                return false;

            // Grab the related queued song (if any)
            if (!_uploadQueue.TryGetValue(player.Id, out var sud))
                return false;
            
            // TODO: Get ID, save song in DB and create item for player

            return true;
        }
    }
}
