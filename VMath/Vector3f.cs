using rrr.Scene;
using System;

namespace rrr.VMath
{
    public struct Vector3f
    {
        public float X;
        public float Y;
        public float Z;

        public Vector3f(
            float x,
            float y,
            float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public float Length()
        {
            return MathF.Sqrt(
                X * X +
                Y * Y +
                Z * Z);
        }

        public float LengthSquared()
        {
            return
                X * X +
                Y * Y +
                Z * Z;
        }

        public Vector3f Normalized()
        {
            float length = Length();

            if (length <= float.Epsilon)
                return Zero;

            return new Vector3f(
                X / length,
                Y / length,
                Z / length);
        }

        public static Vector3f Normalize(
            Vector3f v)
        {
            return v.Normalized();
        }

        public override string ToString()
        {
            return $"({X}, {Y}, {Z})";
        }

        //
        // Static operations
        //

        public static Vector3f Add(
            Vector3f a,
            Vector3f b)
        {
            return new Vector3f(
                a.X + b.X,
                a.Y + b.Y,
                a.Z + b.Z);
        }

        public static Vector3f Sub(
            Vector3f a,
            Vector3f b)
        {
            return new Vector3f(
                a.X - b.X,
                a.Y - b.Y,
                a.Z - b.Z);
        }

        public static float Dot(
            Vector3f a,
            Vector3f b)
        {
            return
                a.X * b.X +
                a.Y * b.Y +
                a.Z * b.Z;
        }

        public static Vector3f Cross(
            Vector3f a,
            Vector3f b)
        {
            return new Vector3f(
                a.Y * b.Z - a.Z * b.Y,
                a.Z * b.X - a.X * b.Z,
                a.X * b.Y - a.Y * b.X);
        }

        public static float Distance(
            Vector3f a,
            Vector3f b)
        {
            return (a - b).Length();
        }

        public static float DistanceSquared(
            Vector3f a,
            Vector3f b)
        {
            return (a - b).LengthSquared();
        }

        public static float Angle(
            Vector3f a,
            Vector3f b)
        {
            float dot =
                Dot(a, b);

            float len =
                a.Length() *
                b.Length();

            return MathF.Acos(dot / len);
        }

        public static Vector3f Project(
            Vector3f a,
            Vector3f b)
        {
            float factor =
                Dot(a, b) /
                Dot(b, b);

            return b * factor;
        }

        public static Vector3f Reflect(
            Vector3f incident,
            Vector3f normal)
        {
            return incident -
                   2.0f *
                   Dot(incident, normal) *
                   normal;
        }

        /// <summary>
        /// Linear interpolation
        /// </summary>
        /// <param name="a">Starting vector</param>
        /// <param name="b">Ending vector</param>
        /// <param name="t">amount</param>
        /// <returns></returns>
        public static Vector3f Lerp(
            Vector3f a,
            Vector3f b,
            float t)
        {
            return a + (b - a) * t;
        }

        public static Vector3f Min(
            Vector3f a,
            Vector3f b)
        {
            return new Vector3f(
                MathF.Min(a.X, b.X),
                MathF.Min(a.Y, b.Y),
                MathF.Min(a.Z, b.Z));
        }

        public static Vector3f Max(
            Vector3f a,
            Vector3f b)
        {
            return new Vector3f(
                MathF.Max(a.X, b.X),
                MathF.Max(a.Y, b.Y),
                MathF.Max(a.Z, b.Z));
        }

        //
        // Operators
        //

        public static Vector3f operator +(
            Vector3f a,
            Vector3f b)
        {
            return Add(a, b);
        }

        public static Vector3f operator -(
            Vector3f a,
            Vector3f b)
        {
            return Sub(a, b);
        }

        public static Vector3f operator -(
            Vector3f v)
        {
            return new Vector3f(
                -v.X,
                -v.Y,
                -v.Z);
        }

        public static Vector3f operator *(
            Vector3f v,
            float scalar)
        {
            return new Vector3f(
                v.X * scalar,
                v.Y * scalar,
                v.Z * scalar);
        }

        public static Vector3f operator *(
            float scalar,
            Vector3f v)
        {
            return new Vector3f(
                v.X * scalar,
                v.Y * scalar,
                v.Z * scalar);
        }

        public static Vector3f operator /(
            Vector3f v,
            float scalar)
        {
            return new Vector3f(
                v.X / scalar,
                v.Y / scalar,
                v.Z / scalar);
        }

        public static implicit operator Vector3f(Vertex v)
        {
            throw new NotImplementedException();
        }

        //
        // Useful constants
        //

        public static Vector3f Zero =>
            new Vector3f(0.0f, 0.0f, 0.0f);

        public static Vector3f One =>
            new Vector3f(1.0f, 1.0f, 1.0f);

        public static Vector3f UnitX =>
            new Vector3f(1.0f, 0.0f, 0.0f);

        public static Vector3f UnitY =>
            new Vector3f(0.0f, 1.0f, 0.0f);

        public static Vector3f UnitZ =>
            new Vector3f(0.0f, 0.0f, 1.0f);

        public static Vector3f Right =>
            UnitX;

        public static Vector3f Left =>
            -UnitX;

        public static Vector3f Up =>
            UnitY;

        public static Vector3f Down =>
            -UnitY;

        public static Vector3f Forward =>
            UnitZ;

        public static Vector3f Backward =>
            -UnitZ;
    }
}