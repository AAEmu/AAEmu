namespace AAEmu.Login.Core.Packets.C2L
{
    public static class CLOffsets
    {
        // All opcodes here are updated for version client_8.0.3.12_r558734
        public const ushort CARequestAuthKakaoPacket = 0x017; // 1
        public const ushort CAListWorldPacket = 0x00c;        // 2
        public const ushort CAEnterWorldPacket = 0x00d;       // 3

        // требует проверки
        public const ushort CARequestAuthPacket = 0x001;
        public const ushort CARequestAuthPacket_0x002 = 0x002;
        public const ushort CARequestAuthGameOnPacket = 0x003;
        public const ushort CARequestAuthTrionPacket = 0xfff;
        public const ushort CARequestAuthPacket_0x004 = 0x004;
        public const ushort CAChallengeResponsePacket = 0x005;
        public const ushort CARequestAuthMailRuPacket = 0x006;
        public const ushort CAOtpNumberPacket = 0x007;
        public const ushort CAPcCertNumberPacket = 0x009;
        public const ushort CACancelEnterWorldPacket = 0xfff;
        public const ushort CARequestReconnectPacket = 0x00f;
        public const ushort CARequestAuthTWPacket = 0x011;
        public const ushort CARequestAuthPacket_0x016 = 0xfff;
    }
}
