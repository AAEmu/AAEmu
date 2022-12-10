using System;
using System.Collections;
using System.Collections.Generic;

namespace AAEmu.Game.Utils.Converters
{
    public class ChatConverter
    {
        private static byte[] Decrypt(string str)
        {
            var abc = new List<char>("0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz*+".ToCharArray());
            var size = (int)Math.Ceiling((double)str.Length * 6 / 8);
            var result = new byte[size];

            var res = new BitArray(size * 8);
            res.SetAll(false);

            var counter = 0;
            var pos = 7;

            for (var i = 0; i < str.Length; i++)
            {
                var h = new BitArray(new byte[] { (byte)abc.IndexOf(str[i]) });
                for (var i2 = 0; i2 < 6; i2++)
                {
                    res[pos--] = h[5 - i2];
                    counter++;
                    if (counter == 8)
                    {
                        pos += 16;
                        counter = 0;
                    }
                }
            }

            res.CopyTo(result, 0);

            return result;
        }

        private static string Encrypt(int a1, int a3, byte[] a4)
        {
            var v9 = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz*+\0".ToCharArray();
            int v10; // [sp+50h] [bp-8h]@1

            var a2 = new char[a3];

            var v4 = 0;
            var v5 = 1;
            var v6 = 2 * (a4[0] >> 7);
            v10 = 8 * a1;
            for (var i = 0; v5 < v10; ++v5)
            {
                v4 = i;
                var v7 = (1 << 7 - (v5 & 7) & a4[v5 >> 3]) >> 7 - (v5 & 7) | v6;
                if (v5 % 6 == 5)
                {
                    a2[i] = v9[v7];
                    v4++;
                    v6 = 0;
                    i = v4;
                    if (v4 >= a3) return new string(a2);
                }
                else v6 = 2 * v7;
            }
            if (v5 % 6 != 0)
            {
                v4++;
                a2[v4 - 1] = v9[v6 >> 1 << 6 * v5 / 6 - (byte)v5 + 6];
                if (v4 >= a3) return new string(a2);
            }
            else
            {
                a2[v4] = '\0';
                return new string(a2);
            }
            return string.Empty;
        }

        /// <summary>
        /// Creates a message encoded identifier for the item
        /// </summary>
        /// <param name="templateId">Template id of the item</param>
        /// <param name="grade">The grade of the item</param>
        /// <param name="durability">If not specified defaults to 100 to not show as red</param>
        /// <returns></returns>
        public static string ConvertAsChatMessageReference(uint templateId, byte grade, byte durability = 100)
        {
            
            var templateIdBytes = BitConverter.GetBytes(templateId);

            string encryptedItem = Encrypt(192, 384, new byte[]
            {
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            templateIdBytes[0], templateIdBytes[1], //template id 
            0x00, 0x00,
            grade, // grade
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00,
            durability, //durability
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
            });

            return $"|i{templateId},{grade},{encryptedItem.Trim('\0')};";
        }
    }
}
