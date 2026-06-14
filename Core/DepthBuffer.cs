namespace rrr.Core;

public class DepthBuffer
{
    private readonly float[] _depth;

    public int Width { get; }
    public int Height { get; }
    public int Stride { get; }

    public float[] Depth => _depth;

    public DepthBuffer(
        int width,
        int height)
    {
        Width = width;
        Height = height;
        Stride = width;

        _depth = new float[width * height];

        Clear();
    }

    public void Clear()
    {
        Array.Fill(
            _depth,
            float.PositiveInfinity);
    }

    public float GetDepth(
        int x,
        int y)
    {
        return _depth[y * Stride + x];
    }

    public void SetDepth(
        int x,
        int y,
        float depth)
    {
        _depth[y * Stride + x] = depth;
    }

    public bool TestAndWrite(
    int x,
    int y,
    float depth)
    {
        int index =
            y * Stride + x;

        if (depth >= _depth[index])
            return false;

        _depth[index] = depth;

        return true;
    }

    internal bool TestAndWriteUnsafe(
    int x,
    int y,
    float z)
    {
        int index =
            y * Stride + x;

        if (z >= _depth[index])
            return false;

        _depth[index] = z;

        return true;
    }
}