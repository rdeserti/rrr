using System;

namespace rrr.VMath
{
    public struct Vector2f
    {
        public float X;
        public float Y;

        public Vector2f(float x, float y)
        {
            X = x;
            Y = y;
        }

        public float Length()
        {
            return MathF.Sqrt(
                X * X +
                Y * Y);
        }

        public float LengthSquared()
        {
            return
                X * X +
                Y * Y;
        }

        public Vector2f Normalized()
        {
            float length = Length();

            if (length <= float.Epsilon)
                return Zero;

            return new Vector2f(
                X / length,
                Y / length);
        }

        public override string ToString()
        {
            return $"({X}, {Y})";
        }

        //
        // Static operations
        //

        public static Vector2f Add(
            Vector2f a,
            Vector2f b)
        {
            return new Vector2f(
                a.X + b.X,
                a.Y + b.Y);
        }

        public static Vector2f Sub(
            Vector2f a,
            Vector2f b)
        {
            return new Vector2f(
                a.X - b.X,
                a.Y - b.Y);
        }

        public static float Dot(
            Vector2f a,
            Vector2f b)
        {
            return
                a.X * b.X +
                a.Y * b.Y;
        }

        //
        // Operators
        //

        public static Vector2f operator +(
            Vector2f a,
            Vector2f b)
        {
            return Add(a, b);
        }

        public static Vector2f operator -(
            Vector2f a,
            Vector2f b)
        {
            return Sub(a, b);
        }

        public static Vector2f operator -(
            Vector2f v)
        {
            return new Vector2f(
                -v.X,
                -v.Y);
        }

        public static Vector2f operator *(
            Vector2f v,
            float scalar)
        {
            return new Vector2f(
                v.X * scalar,
                v.Y * scalar);
        }

        public static Vector2f operator *(
            float scalar,
            Vector2f v)
        {
            return new Vector2f(
                v.X * scalar,
                v.Y * scalar);
        }

        public static Vector2f operator /(
            Vector2f v,
            float scalar)
        {
            return new Vector2f(
                v.X / scalar,
                v.Y / scalar);
        }

        //
        // Useful constants
        //

        public static Vector2f Zero =>
            new Vector2f(0.0f, 0.0f);

        public static Vector2f One =>
            new Vector2f(1.0f, 1.0f);

        public static Vector2f UnitX =>
            new Vector2f(1.0f, 0.0f);

        public static Vector2f UnitY =>
            new Vector2f(0.0f, 1.0f);
    }
}