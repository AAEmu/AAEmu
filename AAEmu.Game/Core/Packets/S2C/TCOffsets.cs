namespace AAEmu.Game.Core.Packets.S2C
{
    public static class TCOffsets
    {
        // All opcodes here are updated for version client_12_r208022
        public const ushort TCJoinResponsePacket = 0x001;
        public const ushort TCDoodadStreamPacket = 0x002;
        public const ushort TCDoodadIdsPacket = 0x003;
        public const ushort TCDownloadEmblemPacket = 0x004;
        public const ushort TCUccComplexPacket = 0x005;
        public const ushort TCUccStringPacket = 0x006;
        public const ushort TCUccPositionPacket = 0x007;
        public const ushort TCUccCharNamePacket = 0x008;
        public const ushort TCCharNameQueriedPacket = 0x009;
        public const ushort TCEmblemStreamRecvStatusPacket = 0x00a;
        public const ushort TCEmblemStreamSendStatusPacket = 0x00b;
        public const ushort TCEmblemStreamDownloadPacket = 0x00c;
        public const ushort TCItemUccDataPacket = 0x00d;
        public const ushort TCHouseFarmPacket = 0x00e;
        public const ushort TCUccComplexCheckValidPacket = 0x00f;
    }
}
