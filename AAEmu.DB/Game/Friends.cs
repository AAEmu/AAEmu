using System;
using System.Collections.Generic;

namespace AAEmu.DB.Game
{
    public partial class Friends
    {
        public uint Id { get; set; }
        public uint FriendId { get; set; }
        public uint Owner { get; set; }
    }
}
