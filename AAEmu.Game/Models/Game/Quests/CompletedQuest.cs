using System;
using System.Collections;
using AAEmu.DB.Game;

namespace AAEmu.Game.Models.Game.Quests
{
    public class CompletedQuest
    {
        public ushort Id { get; set; }
        public BitArray Body { get; set; }

        public CompletedQuest()
        {
        }

        public CompletedQuest(ushort id)
        {
            Id = id;
            Body = new BitArray(64);
        }

        public static explicit operator CompletedQuest(CompletedQuests v)
            =>
            new CompletedQuest()
            {
                Id   =              v.Id   ,
                Body = new BitArray(v.Data),
            };

        public CompletedQuests ToEntity(uint ownerId)
        {
            var body = new byte[8];
            this.Body.CopyTo(body, 0);
            return new CompletedQuests()
            {
                Id      = this.Id ,
                Data    = body    ,
                Owner   = ownerId ,
            };
        }
    }
}
