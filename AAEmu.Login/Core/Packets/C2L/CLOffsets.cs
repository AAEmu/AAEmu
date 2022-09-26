namespace AAEmu.Login.Core.Packets.C2L
{
    public static class CLOffsets
    {
        // All opcodes here are updated for version client_3030_r330995
        public const ushort CARequestAuthPacket_0x001 = 0x001;
        public const ushort CARequestAuthPacket_0x002 = 0x002;
        public const ushort CARequestAuthPacket_0x003 = 0x003;
        public const ushort CARequestAuthPacket_0x004 = 0x004;
        public const ushort CARequestAuthTrionPacket = 0x005;
        public const ushort CARequestAuthMailRuPacket = 0x006;
        public const ushort CAChallengeResponsePacket = 0x007;
        public const ushort CAChallengeResponse2Packet = 0x008;
        public const ushort CAOtpNumberPacket = 0x009;
        public const ushort CAPcCertNumberPacket = 0x00b;
        public const ushort CAListWorldPacket = 0x00c;
        public const ushort CAEnterWorldPacket = 0x00d;
        public const ushort CACancelEnterWorldPacket = 0x00e;
        public const ushort CARequestReconnectPacket = 0x00f;
    }
}
