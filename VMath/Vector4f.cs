using System;

namespace rrr.VMath
{
    public struct Vector4f
    {
        public float X;
        public float Y;
        public float Z;
        public float W;

        public Vector4f(
            float x,
            float y,
            float z,
            float w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        public float Length()
        {
            return MathF.Sqrt(
                X * X +
                Y * Y +
                Z * Z +
                W * W);
        }

        public float LengthSquared()
        {
            return
                X * X +
                Y * Y +
                Z * Z +
                W * W;
        }

        public Vector4f Normalized()
        {
            float length = Length();

            if (length <= float.Epsilon)
                return Zero;

            return this / length;
        }

        public override string ToString()
        {
            return $"({X}, {Y}, {Z}, {W})";
        }

        //
        // Static operations
        //

        public static Vector4f Add(
            Vector4f a,
            Vector4f b)
        {
            return new Vector4f(
                a.X + b.X,
                a.Y + b.Y,
                a.Z + b.Z,
                a.W + b.W);
        }

        public static Vector4f Sub(
            Vector4f a,
            Vector4f b)
        {
            return new Vector4f(
                a.X - b.X,
                a.Y - b.Y,
                a.Z - b.Z,
                a.W - b.W);
        }

        public static float Dot(
            Vector4f a,
            Vector4f b)
        {
            return
                a.X * b.X +
                a.Y * b.Y +
                a.Z * b.Z +
                a.W * b.W;
        }

        public static float Distance(
            Vector4f a,
            Vector4f b)
        {
            return (a - b).Length();
        }

        public static float DistanceSquared(
            Vector4f a,
            Vector4f b)
        {
            return (a - b).LengthSquared();
        }

        public static Vector4f Lerp(
            Vector4f a,
            Vector4f b,
            float t)
        {
            return a + (b - a) * t;
        }

        public static Vector4f Min(
            Vector4f a,
            Vector4f b)
        {
            return new Vector4f(
                MathF.Min(a.X, b.X),
                MathF.Min(a.Y, b.Y),
                MathF.Min(a.Z, b.Z),
                MathF.Min(a.W, b.W));
        }

        public static Vector4f Max(
            Vector4f a,
            Vector4f b)
        {
            return new Vector4f(
                MathF.Max(a.X, b.X),
                MathF.Max(a.Y, b.Y),
                MathF.Max(a.Z, b.Z),
                MathF.Max(a.W, b.W));
        }
        public Vector3f XYZ()
        {
            return new Vector3f(
                X,
                Y,
                Z);
        }

        public static Vector4f FromVector3(
            Vector3f v,
            float w)
        {
            return new Vector4f(
                v.X,
                v.Y,
                v.Z,
                w);
        }

        public Vector3f PerspectiveDivide()
        {
            return new Vector3f(
                X / W,
                Y / W,
                Z / W);
        }

        //
        // Operators
        //

        public static Vector4f operator +(
            Vector4f a,
            Vector4f b)
        {
            return Add(a, b);
        }

        public static Vector4f operator -(
            Vector4f a,
            Vector4f b)
        {
            return Sub(a, b);
        }

        public static Vector4f operator -(
            Vector4f v)
        {
            return new Vector4f(
                -v.X,
                -v.Y,
                -v.Z,
                -v.W);
        }

        public static Vector4f operator *(
            Vector4f v,
            float scalar)
        {
            return new Vector4f(
                v.X * scalar,
                v.Y * scalar,
                v.Z * scalar,
                v.W * scalar);
        }

        public static Vector4f operator *(
            float scalar,
            Vector4f v)
        {
            return v * scalar;
        }

        public static Vector4f operator /(
            Vector4f v,
            float scalar)
        {
            return new Vector4f(
                v.X / scalar,
                v.Y / scalar,
                v.Z / scalar,
                v.W / scalar);
        }

        //
        // Useful constants
        //

        public static Vector4f Zero =>
            new Vector4f(0f, 0f, 0f, 0f);

        public static Vector4f One =>
            new Vector4f(1f, 1f, 1f, 1f);

        public static Vector4f UnitX =>
            new Vector4f(1f, 0f, 0f, 0f);

        public static Vector4f UnitY =>
            new Vector4f(0f, 1f, 0f, 0f);

        public static Vector4f UnitZ =>
            new Vector4f(0f, 0f, 1f, 0f);

        public static Vector4f UnitW =>
            new Vector4f(0f, 0f, 0f, 1f);
    }
}