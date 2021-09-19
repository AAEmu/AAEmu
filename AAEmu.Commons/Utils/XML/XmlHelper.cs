using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Xml;

namespace AAEmu.Commons.Utils.XML
{
    public static class XmlHelper
    {
        public static Vector3 StringToVector3(string positionString)
        {
            var xyz = positionString.Split(',');
            if (xyz.Length == 3)
            {
                if ((float.TryParse(xyz[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)) && 
                    (float.TryParse(xyz[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)) &&
                    (float.TryParse(xyz[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z)))
                    return new Vector3(x, y, z);
            }
            return Vector3.Zero;
        }

        public static Dictionary<string, string> ReadNodeAttributes(XmlNode node)
        {
            var res = new Dictionary<string, string>();
            if (node.Attributes != null)
            {
                for (var i = 0; i < node.Attributes.Count; i++)
                    res.Add(node.Attributes.Item(i).Name, node.Attributes.Item(i).Value);
            }
            return res;
        }

        public static string ReadAttributeString(Dictionary<string, string> attribs, string field, string defaultValue = "")
        {
            if (attribs.TryGetValue(field, out var val))
                return val;
            return defaultValue;
        }

        public static int ReadAttributeInt(Dictionary<string, string> attribs, string field, int defaultValue = 0)
        {
            if (attribs.TryGetValue(field, out var val))
                if (int.TryParse(val, out var i))
                    return i;
            return defaultValue;
        }

    }
}
