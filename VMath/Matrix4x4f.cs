using System;

namespace rrr.VMath
{
    public struct Matrix4x4f
    {
        public float M11;
        public float M12;
        public float M13;
        public float M14;

        public float M21;
        public float M22;
        public float M23;
        public float M24;

        public float M31;
        public float M32;
        public float M33;
        public float M34;

        public float M41;
        public float M42;
        public float M43;
        public float M44;

        public Matrix4x4f(
            float m11, float m12, float m13, float m14,
            float m21, float m22, float m23, float m24,
            float m31, float m32, float m33, float m34,
            float m41, float m42, float m43, float m44)
        {
            M11 = m11;
            M12 = m12;
            M13 = m13;
            M14 = m14;

            M21 = m21;
            M22 = m22;
            M23 = m23;
            M24 = m24;

            M31 = m31;
            M32 = m32;
            M33 = m33;
            M34 = m34;

            M41 = m41;
            M42 = m42;
            M43 = m43;
            M44 = m44;
        }

        public override string ToString()
        {
            return
                $"[{M11}, {M12}, {M13}, {M14}]\n" +
                $"[{M21}, {M22}, {M23}, {M24}]\n" +
                $"[{M31}, {M32}, {M33}, {M34}]\n" +
                $"[{M41}, {M42}, {M43}, {M44}]";
        }

        //
        // Static matrices
        //

        public static Matrix4x4f Identity =>
            new Matrix4x4f(
                1, 0, 0, 0,
                0, 1, 0, 0,
                0, 0, 1, 0,
                0, 0, 0, 1);

        public static Matrix4x4f Zero =>
            new Matrix4x4f(
                0, 0, 0, 0,
                0, 0, 0, 0,
                0, 0, 0, 0,
                0, 0, 0, 0);

        //
        // Matrix operations
        //

        public static Matrix4x4f Add(
            Matrix4x4f a,
            Matrix4x4f b)
        {
            return new Matrix4x4f(
                a.M11 + b.M11,
                a.M12 + b.M12,
                a.M13 + b.M13,
                a.M14 + b.M14,

                a.M21 + b.M21,
                a.M22 + b.M22,
                a.M23 + b.M23,
                a.M24 + b.M24,

                a.M31 + b.M31,
                a.M32 + b.M32,
                a.M33 + b.M33,
                a.M34 + b.M34,

                a.M41 + b.M41,
                a.M42 + b.M42,
                a.M43 + b.M43,
                a.M44 + b.M44);
        }

        public static Matrix4x4f Sub(
            Matrix4x4f a,
            Matrix4x4f b)
        {
            return new Matrix4x4f(
                a.M11 - b.M11,
                a.M12 - b.M12,
                a.M13 - b.M13,
                a.M14 - b.M14,

                a.M21 - b.M21,
                a.M22 - b.M22,
                a.M23 - b.M23,
                a.M24 - b.M24,

                a.M31 - b.M31,
                a.M32 - b.M32,
                a.M33 - b.M33,
                a.M34 - b.M34,

                a.M41 - b.M41,
                a.M42 - b.M42,
                a.M43 - b.M43,
                a.M44 - b.M44);
        }

        public static Matrix4x4f Multiply(
            Matrix4x4f a,
            Matrix4x4f b)
        {
            Matrix4x4f r;

            r.M11 = a.M11 * b.M11 + a.M12 * b.M21 + a.M13 * b.M31 + a.M14 * b.M41;
            r.M12 = a.M11 * b.M12 + a.M12 * b.M22 + a.M13 * b.M32 + a.M14 * b.M42;
            r.M13 = a.M11 * b.M13 + a.M12 * b.M23 + a.M13 * b.M33 + a.M14 * b.M43;
            r.M14 = a.M11 * b.M14 + a.M12 * b.M24 + a.M13 * b.M34 + a.M14 * b.M44;

            r.M21 = a.M21 * b.M11 + a.M22 * b.M21 + a.M23 * b.M31 + a.M24 * b.M41;
            r.M22 = a.M21 * b.M12 + a.M22 * b.M22 + a.M23 * b.M32 + a.M24 * b.M42;
            r.M23 = a.M21 * b.M13 + a.M22 * b.M23 + a.M23 * b.M33 + a.M24 * b.M43;
            r.M24 = a.M21 * b.M14 + a.M22 * b.M24 + a.M23 * b.M34 + a.M24 * b.M44;

            r.M31 = a.M31 * b.M11 + a.M32 * b.M21 + a.M33 * b.M31 + a.M34 * b.M41;
            r.M32 = a.M31 * b.M12 + a.M32 * b.M22 + a.M33 * b.M32 + a.M34 * b.M42;
            r.M33 = a.M31 * b.M13 + a.M32 * b.M23 + a.M33 * b.M33 + a.M34 * b.M43;
            r.M34 = a.M31 * b.M14 + a.M32 * b.M24 + a.M33 * b.M34 + a.M34 * b.M44;

            r.M41 = a.M41 * b.M11 + a.M42 * b.M21 + a.M43 * b.M31 + a.M44 * b.M41;
            r.M42 = a.M41 * b.M12 + a.M42 * b.M22 + a.M43 * b.M32 + a.M44 * b.M42;
            r.M43 = a.M41 * b.M13 + a.M42 * b.M23 + a.M43 * b.M33 + a.M44 * b.M43;
            r.M44 = a.M41 * b.M14 + a.M42 * b.M24 + a.M43 * b.M34 + a.M44 * b.M44;

            return r;
        }

        public static Vector4f Multiply(
            Matrix4x4f m,
            Vector4f v)
        {
            return new Vector4f(
                m.M11 * v.X + m.M12 * v.Y + m.M13 * v.Z + m.M14 * v.W,
                m.M21 * v.X + m.M22 * v.Y + m.M23 * v.Z + m.M24 * v.W,
                m.M31 * v.X + m.M32 * v.Y + m.M33 * v.Z + m.M34 * v.W,
                m.M41 * v.X + m.M42 * v.Y + m.M43 * v.Z + m.M44 * v.W);
        }

        public static Matrix4x4f Transpose(
            Matrix4x4f m)
        {
            return new Matrix4x4f(
                m.M11, m.M21, m.M31, m.M41,
                m.M12, m.M22, m.M32, m.M42,
                m.M13, m.M23, m.M33, m.M43,
                m.M14, m.M24, m.M34, m.M44);
        }

        //
        // Operators
        //

        public static Matrix4x4f operator +(
            Matrix4x4f a,
            Matrix4x4f b)
        {
            return Add(a, b);
        }

        public static Matrix4x4f operator -(
            Matrix4x4f a,
            Matrix4x4f b)
        {
            return Sub(a, b);
        }

        public static Matrix4x4f operator *(
            Matrix4x4f a,
            Matrix4x4f b)
        {
            return Multiply(a, b);
        }

        public static Vector4f operator *(
            Matrix4x4f m,
            Vector4f v)
        {
            return Multiply(m, v);
        }

        //
        // Utils
        //

        public static Matrix4x4f CreateTranslation(
            float x,
            float y,
            float z)
        {
            return new Matrix4x4f(
                1, 0, 0, x,
                0, 1, 0, y,
                0, 0, 1, z,
                0, 0, 0, 1);
        }

        public static Matrix4x4f CreateTranslation(
            Vector3f translation)
        {
            return CreateTranslation(
                translation.X,
                translation.Y,
                translation.Z);
        }

        public static Matrix4x4f CreateScale(
            float x,
            float y,
            float z)
        {
            return new Matrix4x4f(
                x, 0, 0, 0,
                0, y, 0, 0,
                0, 0, z, 0,
                0, 0, 0, 1);
        }

        public static Matrix4x4f CreateScale(
            float scale)
        {
            return CreateScale(
                scale,
                scale,
                scale);
        }

        public static Matrix4x4f CreateScale(
            Vector3f scale)
        {
            return CreateScale(
                scale.X,
                scale.Y,
                scale.Z);
        }

        public static Matrix4x4f CreateRotationX(
            float radians)
        {
            float c = MathF.Cos(radians);
            float s = MathF.Sin(radians);

            return new Matrix4x4f(
                1, 0, 0, 0,
                0, c, -s, 0,
                0, s, c, 0,
                0, 0, 0, 1);
        }

        public static Matrix4x4f CreateRotationY(
            float radians)
        {
            float c = MathF.Cos(radians);
            float s = MathF.Sin(radians);

            return new Matrix4x4f(
                 c, 0, s, 0,
                 0, 1, 0, 0,
                -s, 0, c, 0,
                 0, 0, 0, 1);
        }

        public static Matrix4x4f CreateRotationZ(
            float radians)
        {
            float c = MathF.Cos(radians);
            float s = MathF.Sin(radians);

            return new Matrix4x4f(
                c, -s, 0, 0,
                s, c, 0, 0,
                0, 0, 1, 0,
                0, 0, 0, 1);
        }

        public static Matrix4x4f CreateLookAt(
            Vector3f eye,
            Vector3f target,
            Vector3f up)
        {
            Vector3f zAxis =
                (target - eye).Normalized();

            Vector3f xAxis =
                Vector3f.Cross(up, zAxis)
                    .Normalized();

            Vector3f yAxis =
                Vector3f.Cross(zAxis, xAxis);

            return new Matrix4x4f(
                xAxis.X,
                xAxis.Y,
                xAxis.Z,
                -Vector3f.Dot(xAxis, eye),

                yAxis.X,
                yAxis.Y,
                yAxis.Z,
                -Vector3f.Dot(yAxis, eye),

                zAxis.X,
                zAxis.Y,
                zAxis.Z,
                -Vector3f.Dot(zAxis, eye),

                0,
                0,
                0,
                1);
        }

        public static Matrix4x4f CreatePerspective(
            float fieldOfView,
            float aspectRatio,
            float nearPlane,
            float farPlane)
        {
            float yScale =
                1.0f / MathF.Tan(fieldOfView * 0.5f);

            float xScale =
                yScale / aspectRatio;

            float zRange =
                farPlane - nearPlane;

            return new Matrix4x4f(
                xScale, 0, 0, 0,
                0, yScale, 0, 0,
                0, 0, farPlane / zRange, -nearPlane * farPlane / zRange,
                0, 0, 1, 0);
        }

        public static Matrix4x4f CreatePerspectiveDegrees(
            float fieldOfViewDegrees,
            float aspectRatio,
            float nearPlane,
            float farPlane)
        {
            return CreatePerspective(
                fieldOfViewDegrees * MathF.PI / 180.0f,
                aspectRatio,
                nearPlane,
                farPlane);
        }
    }
}