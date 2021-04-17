namespace AAEmu.Login.Core.Packets.C2L
{
    public static class CLOffsets
    {
        // All opcodes here are updated for version client_12_r208022
        public const ushort CARequestAuthPacket = 0x001;
        public const ushort CARequestAuthTencentPacket = 0x002;
        public const ushort CARequestAuthGameOnPacket = 0x003;
        public const ushort CARequestAuthTrionPacket = 0x004;
        public const ushort CARequestAuthMailRuPacket = 0x005;
        public const ushort CAChallengeResponsePacket = 0x006;
        public const ushort CAChallengeResponse2Packet = 0x007;
        public const ushort CAOtpNumberPacket = 0x008;
        public const ushort CAPcCertNumberPacket = 0x00a;
        public const ushort CAListWorldPacket = 0x00b;
        public const ushort CAEnterWorldPacket = 0x00c;
        public const ushort CACancelEnterWorldPacket = 0x00d;
        public const ushort CARequestReconnectPacket = 0x00e;
    }
}
