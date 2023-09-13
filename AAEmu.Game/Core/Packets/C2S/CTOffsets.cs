namespace AAEmu.Game.Core.Packets.C2S
{
    public static class CTOffsets
    {
        // All opcodes here are updated for version client_12_r208022
        public const ushort CTJoinPacket = 0x001;
        public const ushort CTRequestCellPacket = 0x002;
        public const ushort CTRequestEmblemPacket = 0x003;
        public const ushort CTCancelCellPacket = 0x004;
        public const ushort CTContinuePacket = 0x005;
        public const ushort CTUccComplexPacket = 0x006;
        public const ushort CTUccStringPacket = 0x007;
        public const ushort CTUccPositionPacket = 0x008;
        public const ushort CTUccCharacterNamePacket = 0x009;
        public const ushort CTQueryCharNamePacket = 0x00a;
        public const ushort CTUploadEmblemStreamPacket = 0x00c;
        public const ushort CTEmblemStreamUploadStatusPacket = 0x00d;
        public const ushort CTStartUploadEmblemStreamPacket = 0x00e;
        public const ushort CTEmblemStreamDownloadStatusPacket = 0x00f;
        public const ushort CTItemUccPacket = 0x010;
        public const ushort CTEmblemPartDownloadedPacket = 0x011;
        public const ushort CTUccComplexCheckValidPacket = 0x012;
    }
}
