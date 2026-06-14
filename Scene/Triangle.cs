namespace rrr.Scene
{
    public struct Triangle
    {
        public int P0;
        public int P1;
        public int P2;

        public int N0;
        public int N1;
        public int N2;

        public int UV0;
        public int UV1;
        public int UV2;

        public Triangle(
            int p0,
            int p1,
            int p2)
        {
            P0 = p0;
            P1 = p1;
            P2 = p2;

            N0 = N1 = N2 = -1;
            UV0 = UV1 = UV2 = -1;
        }

        public Triangle(
            int p0, int uv0, int n0,
            int p1, int uv1, int n1,
            int p2, int uv2, int n2)
        {
            P0 = p0;
            P1 = p1;
            P2 = p2;

            UV0 = uv0;
            UV1 = uv1;
            UV2 = uv2;

            N0 = n0;
            N1 = n1;
            N2 = n2;
        }
    }
}