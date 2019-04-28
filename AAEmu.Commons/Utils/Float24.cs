using System;
using System.Linq;

namespace AAEmu.Commons.Utils
{
    public static class Float24
    {
        public static float ToFloat(int value)
        {
            byte[] byteArray = BitConverter.GetBytes(value);
            float val = BitConverter.ToSingle(new byte[] { 0 }.Concat(byteArray).ToArray(), 0);
            return val;
        }

        public static float ToFloat(Uint24 value)
        {
            byte[] threeByteArray = BitConverter.GetBytes(value);
            float val = BitConverter.ToSingle(new byte[] { 0 }.Concat(threeByteArray).ToArray(), 0);
            return val;
        }

        public static Uint24 ToFloat24(int value)
        {
            byte[] byteArray = BitConverter.GetBytes(value);
            // (00 0D 91 3F)    1.133209        FE 0C 91 3F
            // (00 00 BF 41)    23.875          00 00 BF 41
            // (00 0F 1B 42)    38.76465        00 0F 1B 42
            // (00 0F 9B 41)    19.38232        00 0F 9B 41
            // (00 E5 6C 41)    14.80591        00 E5 6C 41
            // (00 96 43 3F)    0.7640076       00 96 43 3F
            // (00 0F 91 3F)    1.13327         FE 0E 91 3F
            if (byteArray[0] >= 0x80)
            {
                ++byteArray[1];
            }
            Uint24 val = (uint)(byteArray[1] | byteArray[2] << 8 | byteArray[3] << 16);
            return val;
        }

        public static Uint24 ToFloat24(float value)
        {
            //int a = Convert.ToInt32(value);
            byte[] byteArray = BitConverter.GetBytes(value);
            // (00 0D 91 3F)    1.133209        FE 0C 91 3F
            // (00 00 BF 41)    23.875          00 00 BF 41
            // (00 0F 1B 42)    38.76465        00 0F 1B 42
            // (00 0F 9B 41)    19.38232        00 0F 9B 41
            // (00 E5 6C 41)    14.80591        00 E5 6C 41
            // (00 96 43 3F)    0.7640076       00 96 43 3F
            // (00 0F 91 3F)    1.13327         FE 0E 91 3F
            if (byteArray[0] >= 0x80)
            {
                ++byteArray[1];
            }
            Uint24 val = (uint)(byteArray[1] | byteArray[2] << 8 | byteArray[3] << 16);
            return val;
        }

        public static int ToInt32(Uint24 value)
        {
            byte[] threeByteArray = BitConverter.GetBytes(value);
            Array.Reverse(threeByteArray);
            byte[] byteArray = new byte[] {0}.Concat(threeByteArray).ToArray();
            Array.Reverse(byteArray);
            int val = BitConverter.ToInt32(byteArray, 0);
            return val;
        }

        public static int ToInt32(byte[] value)
        {
            //Array.Reverse(value);
            byte[] arr = new byte[4];
            arr[0] = value[1];
            arr[1] = value[2];
            arr[2] = value[3];
            //arr[0] = 0;
            int val = BitConverter.ToInt32(arr, 0);
            return val;
        }
        public static int ToInt32(float value)
        {
            byte[] arr1 = BitConverter.GetBytes(value);
            //Array.Reverse(value);
            byte[] arr2 = new byte[4];
            arr2[0] = arr1[1];
            arr2[1] = arr1[2];
            arr2[2] = arr1[3];
            //arr[0] = 0;
            int val = BitConverter.ToInt32(arr2, 0);
            return val;
        }

    }
}
