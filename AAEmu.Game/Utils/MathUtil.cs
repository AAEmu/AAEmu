using System;
using System.Numerics;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Utils
{
    public static class MathUtil
    {
        // Return degree value of object 2 to the horizontal line with object 1 being the origin
        public static double CalculateAngleFrom(GameObject obj1, GameObject obj2)
        {
            return CalculateAngleFrom(obj1.Position.X, obj1.Position.Y, obj2.Position.X, obj2.Position.Y);
        }

        // Return degree value of object 2 to the horizontal line with object 1 being the origin
        public static double CalculateAngleFrom(float obj1X, float obj1Y, float obj2X, float obj2Y)
        {
            var angleTarget = RadianToDegree(Math.Atan2(obj2Y - obj1Y, obj2X - obj1X));
            if (angleTarget < 0)
            {
                angleTarget += 360;
            }

            return angleTarget;
        }

        public static double RadianToDegree(double angle)
        {
            return angle * (180.0 / Math.PI);
        }
        public static double DegreeToRadian(double angle)
        {
            return angle * (Math.PI / 180.0);
        }

        public static double ConvertDirectionToDegree(sbyte direction)
        {
            var angle = direction * (360f / 128) + 90;
            if (angle < 0)
            {
                angle += 360;
            }

            return angle;
        }

        public static sbyte ConvertDegreeToDirection(double degree)
        {
            if (degree < 0)
            {
                degree = 360 + degree;
            }

            degree -= 90;
            var res = (sbyte)(degree / (360f / 128));
            if (res > 85)
            {
                res = (sbyte)((degree - 360) / (360f / 128));
            }

            return res;
        }

        public static short UpdateHeading(Vector3 point, Vector3 target)
        {
            var rad = Math.Atan2(target.Y - point.Y, target.X - point.X);
            rad -= Math.PI / 2; // 12 o'clock == 0°
            var heading = (short)(rad * 32767 / Math.PI);

            return heading;
        }
        public static ushort UpdateHeading(double degree, bool radian = false)
        {
            ushort heading;
            if (radian)
            {
                degree -= Math.PI / 2; // 12 o'clock == 0°
                heading = (ushort)(degree * 32767 / Math.PI);
            }
            else
            {
                degree -= 90; // 12 o'clock == 0°
                heading = (ushort)(degree * 32767 / 180);
            }
            //    52 - 0.00371041
            //     1 - 0.000071353925
            // 65536 - 4.6762582646153846153846153846154

            return heading;
        }

        public static sbyte ConvertDegreeToDirection2(double degree)
        {
            var rotateModifier = 1;
            if (degree < 0)
            {
                //rotateModifier = -1;   // remember the sign of the angle
                degree = 360 + degree; // working with positive angles
            }
            degree -= 90; // 12 o'clock == 0°
            //if (degree > 180)
            //{
            //    degree = 360 - degree;  // we work only with angles up to 180 degrees
            //}
            var res = (sbyte)(degree / (180f / 127) * rotateModifier);
            //if (res > 85)
            //    res = (sbyte)((degree - 360) / (180f / 127) * rotateModifier);
            return res;
        }

        public static sbyte ConvertDegreeToDoodadDirection(double degree)
        {
            while (degree < 0f)
            {
                degree += 360f;
            }

            if ((degree > 90f) && (degree <= 180f))
            {
                return (sbyte)((((degree - 90f) / 90f * 37f) + 90f) * -1);
            }

            if (degree > 180f)
            {
                return (sbyte)((((degree - 270f) / 90f * 37f) - 90f) * -1);
            }
            // When range is between -90 and 90, no rotation scaling is applied for doodads
            return (sbyte)(degree * -1);
        }
        public static bool IsFront(GameObject obj1, GameObject obj2)
        {
            var degree = CalculateAngleFrom(obj1, obj2);
            var degree2 = ConvertDirectionToDegree(obj2.Position.RotationZ);
            var diff = Math.Abs(degree - degree2);

            if (diff >= 90 && diff <= 270)
            {
                return true;
            }

            return false;
        }

        public static double ConvertDirectionToRadian(sbyte direction)
        {
            return ConvertDirectionToDegree(direction) * Math.PI / 180.0;
        }

        public static (float, float) AddDistanceToFront(float distance, float x, float y, sbyte rotZ)
        {
            var rad = ConvertDirectionToRadian(rotZ);
            var newX = (distance * (float)Math.Cos(rad)) + x;
            var newY = (distance * (float)Math.Sin(rad)) + y;
            return (newX, newY);
        }
        public static (float, float) AddDistanceToFront(float distance, float x, float y, double rad)
        {
            var newX = distance * (float)Math.Cos(rad) + x;
            var newY = distance * (float)Math.Sin(rad) + y;
            return (newX, newY);
        }

        public static (float, float) AddDistanceToRight(float distance, float x, float y, sbyte rotZ)
        {
            var rad = ConvertDirectionToRadian(rotZ) - (Math.PI / 2);
            var newX = (distance * (float)Math.Cos(rad)) + x;
            var newY = (distance * (float)Math.Sin(rad)) + y;
            return (newX, newY);
        }

        public static sbyte ConvertRadianToDirection(double radian) // TODO float zRot
        {
            var degree = RadianToDegree(radian);
            if (degree < 0)
            {
                degree = 360 + degree;
            }

            var res = (sbyte)(degree / (360f / 128));
            if (res > 85)
            {
                res = (sbyte)((degree - 360) / (360f / 128));
            }

            return res;
        }

        public static float CalculateDistance(Point loc, Point loc2, bool includeZAxis = false)
        {
            double dx = loc.X - loc2.X;
            double dy = loc.Y - loc2.Y;

            if (includeZAxis)
            {
                double dz = loc.Z - loc2.Z;
                return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
            }

            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        // linear interpolation
        public static float Lerp(float s, float e, float t)
        {
            return s + (e - s) * t;
        }

        // box linear interpolation
        public static float Blerp(float cX0Y0, float cX1Y0, float cX0Y1, float cX1Y1, float tx, float ty)
        {
            return MathUtil.Lerp(MathUtil.Lerp(cX0Y0, cX1Y0, tx), MathUtil.Lerp(cX0Y1, cX1Y1, tx), ty);
        }

        public static double Distance(GameObject obj, Point target)
        {
            return new Vector3(obj.Position.X, obj.Position.Y, obj.Position.Z).Distance(new Vector3(target.X, target.Y, target.Z));
        }

        public static short UpdateHeading(Unit obj, Vector3 target)
        {
            return (short)(Math.Atan2(target.Y - obj.Position.Y, target.X - obj.Position.X) * 32768 / Math.PI);
        }

        // ===========================================================================================================
        /*
         create a unit quaternion rotating by axis angle representation
        */
        public static Quaternion unitFromAxisAngle(Vector3 axis, float angle)
        {
            var v = Vector3.Normalize(axis);
            var halfAngle = angle * 0.5f;
            var sinA = (float)Math.Sin(halfAngle);
            var quaternion = new Quaternion(v.X * sinA, v.Y * sinA, v.Z * sinA, (float)Math.Cos(halfAngle));
            return quaternion;
        }
        //-----------------------------------
        /*
          convert a quaternion to axis angle representation, 
          preserve the axis direction and angle from -PI to +PI
        */
        public static (Vector3, float) toAxisAngle(float x, float y, float z, float w = 1.0f)
        {
            Vector3 axis;
            float angle;

            var vl = (float)Math.Sqrt(x * x + y * y + z * z);

            if (vl > 0.99993801 || vl < 0.000062000123)
            {
                axis = new Vector3(0, 0, 0);
                angle = 1.0f;
            }
            else
            {
                var ivl = 1.0f / vl;
                axis = new Vector3(x * ivl, y * ivl, z * ivl);
                if (w < 0)
                {
                    angle = 2.0f * (float)Math.Atan2(-vl, -w); // -PI, 0
                }
                else
                {
                    angle = 2.0f * (float)Math.Atan2(vl, w);   //   0, PI
                }
            }

            return (axis, angle);
        }
        //-----------------------------------
        /*
        С помощью "Shortest arc" можно ориентировать ракету в направлении полета, причем ее повороты будут выглядеть естественно
        (разворот по кратчайшей дуге). Алгоритм очень прост, на каждом шаге берем предыдущий вектор направления. Строим "Shortest
        arc" от него к текущему направлению и поворачиваем объект на получившийся кватернион. Если мы применим повороты с помощью
        "Shortest arc" при движении по непрерывной кривой (например, по сплайну), мы реализуем очень полезный метод "parallel
        transport frame". Этот метод дает нам как бы ориентацию каната протянутого по этой кривой и минимизирует скручивание
        "twist" каната. Это особенно полезно для создания объектов по заданному трафарету и профилю кривой.
         Для решения задачи инверсной кинематики, когда по заданному направлению надо найти необходимый поворот
        "Shortest arc" придется как нельзя кстати.
        */

        /*
        the shortest arc quaternion that will rotate one vector to another.
        create rotation from -> to, for any length vectors
        */
        public static Quaternion shortestArc(Vector3 from, Vector3 to)
        {
            var q2 = new Quaternion(0, 0, 0, 1);
            var crossV = Vector3.Cross(from, to);
            var q = new Quaternion(crossV.X, crossV.Y, crossV.Z, Vector3.Dot(from, to));
            q = Quaternion.Normalize(q);    // if "from" or "to" is not unit, normalize it

            // contains quaternion of "double angle" rotation from to. can be non unit.
            q.W += 1.0f;               // reducing angle to half angle
            if (q.W <= 0.000062000123) // angle close to PI
            {
                if ((from.Z * from.Z) > (from.X * from.X))
                {
                    q2 = new Quaternion(0, from.Z, -from.Y, q.W); // from * vector3(1,0,0) 
                }
                else
                {
                    q2 = new Quaternion(from.Y, -from.X, 0, q.W); // from * vector3(0,0,1) 
                }
            }
            q2 = Quaternion.Normalize(q2);
            return q2;
        }
        public static float ConvertToDirection(double radian)
        {
            var degree = RadianToDegree(radian);
            degree += -90; // 12 o'clock == 0°

            double direction = 0f;
            if (Math.Abs(degree) > 135 && Math.Abs(degree) <= 180)
            {
                direction = degree * 182.0389;
            }
            else if (Math.Abs(degree) > 90 && Math.Abs(degree) <= 135 || Math.Abs(degree) < 270 && Math.Abs(degree) >= 225)
            {
                direction = degree * 205.6814814814815;
            }
            else if (Math.Abs(degree) >= 0 && Math.Abs(degree) <= 90 || Math.Abs(degree) <= 360 && Math.Abs(degree) >= 270)
            {
                direction = degree * 252.9777777777778;
            }
            //_log.Warn("Degree0={0}, Degree1={1}, Direction={2}", tmp, degree, direction);

            return Helpers.ConvertDirectionToRadian((short)direction);
        }
        public static double CalculateDirection(Vector3 obj1, Vector3 obj2)
        {
            var rad = Math.Atan2(obj2.Y - obj1.Y, obj2.X - obj1.X);

            return rad;
        }
        public static double CalculateDirectionZ(Vector3 obj1, Vector3 obj2)
        {
            var rad = Math.Atan2(obj2.Y - obj1.Y, obj2.Z - obj1.Z);

            return rad;
        }
        public static float GetDistance(Vector3 v1, Vector3 v2, bool point3d = false)
        {
            return point3d
                ?
                MathF.Sqrt(MathF.Pow(v1.X - v2.X, 2) + MathF.Pow(v1.Y - v2.Y, 2) + MathF.Pow(v1.Y - v2.Y, 2))
                :
                MathF.Sqrt(MathF.Pow(v1.X - v2.X, 2) + MathF.Pow(v1.Y - v2.Y, 2));
        }
    }
}
