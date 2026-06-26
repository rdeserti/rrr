using rrr.VMath;

namespace rrr.Importers.Gltf;

/// <summary>
/// A minimal 4x4 transform in glTF's native <b>column-major</b> layout
/// (element at column <c>c</c>, row <c>r</c> is <c>M[c * 4 + r]</c>). Kept
/// self-contained so glTF node math stays independent of the renderer's
/// row-major <see cref="Matrix4x4f"/> conventions; vertices are transformed
/// here and handed to the mesh as plain points.
/// </summary>
internal readonly struct Mat4
{
    private readonly float[] _m;

    private Mat4(float[] m) => _m = m;

    public static Mat4 Identity =>
        new Mat4(new float[]
        {
            1, 0, 0, 0,
            0, 1, 0, 0,
            0, 0, 1, 0,
            0, 0, 0, 1
        });

    public static Mat4 FromColumnMajor(float[] m)
    {
        float[] copy = new float[16];
        Array.Copy(m, copy, 16);
        return new Mat4(copy);
    }

    public static Mat4 FromTRS(
        Vector3f t,
        (float x, float y, float z, float w) q,
        Vector3f s)
    {
        // Quaternion (x,y,z,w) -> rotation, then scale each basis column, with
        // translation in the last column.
        float xx = q.x * q.x, yy = q.y * q.y, zz = q.z * q.z;
        float xy = q.x * q.y, xz = q.x * q.z, yz = q.y * q.z;
        float wx = q.w * q.x, wy = q.w * q.y, wz = q.w * q.z;

        float r00 = 1 - 2 * (yy + zz);
        float r01 = 2 * (xy - wz);
        float r02 = 2 * (xz + wy);

        float r10 = 2 * (xy + wz);
        float r11 = 1 - 2 * (xx + zz);
        float r12 = 2 * (yz - wx);

        float r20 = 2 * (xz - wy);
        float r21 = 2 * (yz + wx);
        float r22 = 1 - 2 * (xx + yy);

        float[] m = new float[16];

        // Column 0 (basis X) scaled by s.X
        m[0] = r00 * s.X; m[1] = r10 * s.X; m[2] = r20 * s.X; m[3] = 0;
        // Column 1 (basis Y) scaled by s.Y
        m[4] = r01 * s.Y; m[5] = r11 * s.Y; m[6] = r21 * s.Y; m[7] = 0;
        // Column 2 (basis Z) scaled by s.Z
        m[8] = r02 * s.Z; m[9] = r12 * s.Z; m[10] = r22 * s.Z; m[11] = 0;
        // Column 3 (translation)
        m[12] = t.X; m[13] = t.Y; m[14] = t.Z; m[15] = 1;

        return new Mat4(m);
    }

    /// <summary>Returns <c>this * rhs</c> (apply <paramref name="rhs"/> first).</summary>
    public static Mat4 operator *(Mat4 a, Mat4 b)
    {
        float[] r = new float[16];

        for (int col = 0; col < 4; col++)
        {
            for (int row = 0; row < 4; row++)
            {
                float sum = 0;
                for (int k = 0; k < 4; k++)
                    sum += a._m[k * 4 + row] * b._m[col * 4 + k];

                r[col * 4 + row] = sum;
            }
        }

        return new Mat4(r);
    }

    public Vector3f TransformPoint(Vector3f v)
    {
        float x = _m[0] * v.X + _m[4] * v.Y + _m[8] * v.Z + _m[12];
        float y = _m[1] * v.X + _m[5] * v.Y + _m[9] * v.Z + _m[13];
        float z = _m[2] * v.X + _m[6] * v.Y + _m[10] * v.Z + _m[14];
        return new Vector3f(x, y, z);
    }

    public Vector3f TransformDirection(Vector3f v)
    {
        // Upper 3x3 only (ignores translation). Adequate for normals under
        // rigid / uniform-scale transforms; renormalized by the caller.
        float x = _m[0] * v.X + _m[4] * v.Y + _m[8] * v.Z;
        float y = _m[1] * v.X + _m[5] * v.Y + _m[9] * v.Z;
        float z = _m[2] * v.X + _m[6] * v.Y + _m[10] * v.Z;
        return new Vector3f(x, y, z);
    }
}
