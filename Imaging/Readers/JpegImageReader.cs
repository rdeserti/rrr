using rrr.Core;
using System;
using System.IO;

namespace rrr.Imaging.Readers;

/// <summary>
/// JPEG/JFIF decoder written from scratch, supporting both <b>baseline</b>
/// (SOF0/SOF1, sequential DCT, Huffman, 8-bit) and <b>progressive</b> (SOF2)
/// images. Decoding is coefficient-based: each scan accumulates DCT coefficients
/// into a per-component buffer (DC/AC, first pass and successive-approximation
/// refinement, spectral selection, EOB runs); after the last scan the buffers
/// are dequantized, inverse-DCT'd, chroma-upsampled and converted to RGB.
/// 1-component grayscale and 3-component YCbCr with any subsampling, plus
/// restart markers, are handled. The progressive algorithm follows the standard
/// (pdf.js-style) state machine.
///
/// Not supported (rejected so callers fall back gracefully): arithmetic coding,
/// 12-bit precision, lossless and hierarchical modes; Adobe CMYK/YCCK.
/// </summary>
internal sealed class JpegImageReader : IImageReader
{
    private static readonly int[] ZigZag =
    {
         0,  1,  8, 16,  9,  2,  3, 10,
        17, 24, 32, 25, 18, 11,  4,  5,
        12, 19, 26, 33, 40, 48, 41, 34,
        27, 20, 13,  6,  7, 14, 21, 28,
        35, 42, 49, 56, 57, 50, 43, 36,
        29, 22, 15, 23, 30, 37, 44, 51,
        58, 59, 52, 45, 38, 31, 39, 46,
        53, 60, 61, 54, 47, 55, 62, 63
    };

    // Separable IDCT cosine basis: T[k,n] = c(n) * cos((2k+1) n π / 16).
    private static readonly float[,] IdctBasis = BuildIdctBasis();

    private sealed class Component
    {
        public int Id;
        public int H;          // horizontal sampling factor
        public int V;          // vertical sampling factor
        public int QuantId;

        // Block grid (real extent, and MCU-aligned/padded extent).
        public int BlocksPerLine;
        public int BlocksPerColumn;
        public int BlocksPerLineForMcu;
        public int BlocksPerColumnForMcu;

        public int[] BlockData = Array.Empty<int>(); // DCT coeffs, zig-zag order

        // Per-scan state.
        public HuffmanTable? DcTable;
        public HuffmanTable? AcTable;
        public int Pred;       // DC predictor

        // Reconstructed spatial samples (filled after all scans).
        public byte[] Plane = Array.Empty<byte>();
        public int PlaneWidth;
        public int PlaneHeight;
    }

    private sealed class HuffmanTable
    {
        public readonly int[] MaxCode = new int[18];
        public readonly int[] MinCode = new int[18];
        public readonly int[] ValPtr = new int[18];
        public byte[] Symbols = Array.Empty<byte>();
    }

    private readonly int[]?[] _quant = new int[4][];
    private readonly HuffmanTable?[] _dcTables = new HuffmanTable?[4];
    private readonly HuffmanTable?[] _acTables = new HuffmanTable?[4];

    private int _width;
    private int _height;
    private bool _progressive;
    private Component[] _components = Array.Empty<Component>();
    private int _maxH, _maxV;
    private int _mcusPerLine, _mcusPerColumn;
    private int _restartInterval;

    private byte[] _data = Array.Empty<byte>();
    private int _pos;

    // Bit reader state.
    private int _bitBuffer;
    private int _bitCount;
    private bool _markerHit;

    // Current scan parameters / state.
    private Component[] _scanComponents = Array.Empty<Component>();
    private int _spectralStart, _spectralEnd, _ah, _al;
    private int _eobrun;
    private int _successiveAcState;
    private int _successiveAcNextValue;

    public FrameBuffer Load(Stream stream)
    {
        using MemoryStream memory = new MemoryStream();
        stream.CopyTo(memory);
        _data = memory.ToArray();
        _pos = 0;

        if (_data.Length < 2 || _data[0] != 0xFF || _data[1] != 0xD8)
            throw new InvalidDataException("Not a JPEG file (missing SOI).");

        _pos = 2;

        ParseMarkers();

        return Reconstruct();
    }

    //
    // Marker stream
    //

    private void ParseMarkers()
    {
        while (_pos + 1 < _data.Length)
        {
            if (_data[_pos] != 0xFF) { _pos++; continue; }

            byte marker = _data[_pos + 1];
            _pos += 2;

            if (marker == 0xFF) { _pos--; continue; }
            if (marker == 0xD9) return; // EOI
            if (marker == 0xD8 || (marker >= 0xD0 && marker <= 0xD7))
                continue;

            int length = ReadUInt16();
            int segmentEnd = _pos + length - 2;

            switch (marker)
            {
                case 0xDB: ReadQuantTables(segmentEnd); break;
                case 0xC4: ReadHuffmanTables(segmentEnd); break;
                case 0xDD: _restartInterval = ReadUInt16(); break;

                case 0xC0: // baseline
                case 0xC1: // extended sequential (Huffman)
                    ReadFrameHeader(progressive: false);
                    break;
                case 0xC2: // progressive
                    ReadFrameHeader(progressive: true);
                    break;

                case 0xC3:
                case 0xC5: case 0xC6: case 0xC7:
                case 0xC9: case 0xCA: case 0xCB:
                case 0xCD: case 0xCE: case 0xCF:
                    throw new NotSupportedException(
                        $"Unsupported JPEG mode (SOF marker 0x{marker:X2}).");

                case 0xDA: // SOS
                    ReadScanHeader();
                    DecodeScan();
                    AlignToNextMarker();
                    continue; // _pos already at the next marker
            }

            _pos = segmentEnd;
        }
    }

    private void ReadQuantTables(int end)
    {
        while (_pos < end)
        {
            int pq_tq = _data[_pos++];
            int precision = pq_tq >> 4;
            int id = pq_tq & 0x0F;

            if (id >= 4)
                throw new InvalidDataException($"Invalid quant table id {id}.");

            int[] table = new int[64];
            for (int k = 0; k < 64; k++)
                table[k] = precision == 0 ? _data[_pos++] : ReadUInt16();

            _quant[id] = table;
        }
    }

    private void ReadHuffmanTables(int end)
    {
        while (_pos < end)
        {
            int tc_th = _data[_pos++];
            int cls = tc_th >> 4;
            int id = tc_th & 0x0F;

            if (id >= 4 || cls > 1)
                throw new InvalidDataException("Invalid Huffman table descriptor.");

            int[] counts = new int[17];
            int total = 0;
            for (int l = 1; l <= 16; l++) { counts[l] = _data[_pos++]; total += counts[l]; }

            byte[] symbols = new byte[total];
            for (int i = 0; i < total; i++) symbols[i] = _data[_pos++];

            HuffmanTable table = BuildHuffmanTable(counts, symbols);
            if (cls == 0) _dcTables[id] = table; else _acTables[id] = table;
        }
    }

    private void ReadFrameHeader(bool progressive)
    {
        _progressive = progressive;

        int precision = _data[_pos++];
        if (precision != 8)
            throw new NotSupportedException($"Only 8-bit JPEG is supported (precision {precision}).");

        _height = ReadUInt16();
        _width = ReadUInt16();

        int count = _data[_pos++];
        if (count != 1 && count != 3)
            throw new NotSupportedException($"Only 1- or 3-component JPEG is supported (got {count}).");

        _components = new Component[count];
        _maxH = 0; _maxV = 0;

        for (int i = 0; i < count; i++)
        {
            Component c = new Component { Id = _data[_pos++] };
            int sampling = _data[_pos++];
            c.H = sampling >> 4;
            c.V = sampling & 0x0F;
            c.QuantId = _data[_pos++];
            _components[i] = c;

            _maxH = Math.Max(_maxH, c.H);
            _maxV = Math.Max(_maxV, c.V);
        }

        _mcusPerLine = CeilDiv(_width, 8 * _maxH);
        _mcusPerColumn = CeilDiv(_height, 8 * _maxV);

        foreach (Component c in _components)
        {
            c.BlocksPerLine = CeilDiv(CeilDiv(_width, 8) * c.H, _maxH);
            c.BlocksPerColumn = CeilDiv(CeilDiv(_height, 8) * c.V, _maxV);
            c.BlocksPerLineForMcu = _mcusPerLine * c.H;
            c.BlocksPerColumnForMcu = _mcusPerColumn * c.V;
            c.BlockData = new int[c.BlocksPerLineForMcu * c.BlocksPerColumnForMcu * 64];
        }
    }

    private void ReadScanHeader()
    {
        int count = _data[_pos++];
        _scanComponents = new Component[count];

        for (int i = 0; i < count; i++)
        {
            int selector = _data[_pos++];
            int tables = _data[_pos++];

            Component c = FindComponent(selector);
            c.DcTable = _dcTables[tables >> 4];
            c.AcTable = _acTables[tables & 0x0F];
            _scanComponents[i] = c;
        }

        _spectralStart = _data[_pos++];
        _spectralEnd = _data[_pos++];
        int approx = _data[_pos++];
        _ah = approx >> 4;
        _al = approx & 0x0F;
    }

    private Component FindComponent(int id)
    {
        foreach (Component c in _components)
            if (c.Id == id) return c;
        throw new InvalidDataException($"Scan references unknown component {id}.");
    }

    //
    // Scan decoding (coefficient accumulation)
    //

    private void DecodeScan()
    {
        // Reset bit reader + scan state.
        _bitCount = 0;
        _markerHit = false;
        _eobrun = 0;
        _successiveAcState = 0;
        foreach (Component c in _scanComponents) c.Pred = 0;

        bool interleaved = _scanComponents.Length > 1;
        int restartCounter = 0;
        int interval = _restartInterval;

        if (!interleaved)
        {
            Component c = _scanComponents[0];
            int total = c.BlocksPerLine * c.BlocksPerColumn;

            for (int n = 0; n < total; n++)
            {
                if (interval > 0 && n > 0 && n % interval == 0)
                    HandleRestart();

                int row = n / c.BlocksPerLine;
                int col = n % c.BlocksPerLine;
                int offset = BlockOffset(c, row, col);
                DecodeBlock(c, offset);
            }
        }
        else
        {
            int totalMcu = _mcusPerLine * _mcusPerColumn;

            for (int mcu = 0; mcu < totalMcu; mcu++)
            {
                if (interval > 0 && mcu > 0 && mcu % interval == 0)
                    HandleRestart();

                int mcuRow = mcu / _mcusPerLine;
                int mcuCol = mcu % _mcusPerLine;

                foreach (Component c in _scanComponents)
                {
                    for (int by = 0; by < c.V; by++)
                        for (int bx = 0; bx < c.H; bx++)
                        {
                            int row = mcuRow * c.V + by;
                            int col = mcuCol * c.H + bx;
                            DecodeBlock(c, BlockOffset(c, row, col));
                        }
                }
                restartCounter++;
            }
        }
    }

    private static int BlockOffset(Component c, int row, int col)
        => 64 * (row * c.BlocksPerLineForMcu + col);

    private void DecodeBlock(Component c, int offset)
    {
        if (!_progressive)
        {
            DecodeBaselineBlock(c, offset);
            return;
        }

        if (_spectralStart == 0)
        {
            if (_ah == 0) DecodeDcFirst(c, offset);
            else DecodeDcRefine(c, offset);
        }
        else
        {
            if (_ah == 0) DecodeAcFirst(c, offset);
            else DecodeAcRefine(c, offset);
        }
    }

    private void DecodeBaselineBlock(Component c, int offset)
    {
        int t = DecodeHuffman(c.DcTable!);
        int diff = t == 0 ? 0 : Extend(ReadBits(t), t);
        c.Pred += diff;
        c.BlockData[offset] = c.Pred;

        int k = 1;
        while (k < 64)
        {
            int rs = DecodeHuffman(c.AcTable!);
            int s = rs & 0x0F;
            int r = rs >> 4;

            if (s == 0)
            {
                if (r < 15) break;
                k += 16;
                continue;
            }

            k += r;
            if (k >= 64) break;
            c.BlockData[offset + k] = Extend(ReadBits(s), s);
            k++;
        }
    }

    private void DecodeDcFirst(Component c, int offset)
    {
        int t = DecodeHuffman(c.DcTable!);
        int diff = t == 0 ? 0 : (Extend(ReadBits(t), t) << _al);
        c.Pred += diff;
        c.BlockData[offset] = c.Pred;
    }

    private void DecodeDcRefine(Component c, int offset)
    {
        if (ReadBit() != 0)
            c.BlockData[offset] |= 1 << _al;
    }

    private void DecodeAcFirst(Component c, int offset)
    {
        if (_eobrun > 0) { _eobrun--; return; }

        int k = _spectralStart;
        int e = _spectralEnd;

        while (k <= e)
        {
            int rs = DecodeHuffman(c.AcTable!);
            int s = rs & 0x0F;
            int r = rs >> 4;

            if (s == 0)
            {
                if (r < 15)
                {
                    _eobrun = ReadBits(r) + (1 << r) - 1;
                    break;
                }
                k += 16;
                continue;
            }

            k += r;
            if (k > e) break;
            c.BlockData[offset + k] = Extend(ReadBits(s), s) << _al;
            k++;
        }
    }

    private void DecodeAcRefine(Component c, int offset)
    {
        int k = _spectralStart;
        int e = _spectralEnd;
        int r = 0;

        while (k <= e)
        {
            int z = offset + k;
            int sign = c.BlockData[z] < 0 ? -1 : 1;

            switch (_successiveAcState)
            {
                case 0: // initial
                    {
                        int rs = DecodeHuffman(c.AcTable!);
                        int s = rs & 0x0F;
                        r = rs >> 4;

                        if (s == 0)
                        {
                            if (r < 15)
                            {
                                _eobrun = ReadBits(r) + (1 << r);
                                _successiveAcState = 4;
                            }
                            else
                            {
                                r = 16;
                                _successiveAcState = 1;
                            }
                        }
                        else
                        {
                            _successiveAcNextValue = Extend(ReadBits(s), s); // ±1
                            _successiveAcState = r != 0 ? 2 : 3;
                        }
                        continue;
                    }

                case 1: // skipping r zero-history coefficients
                case 2:
                    if (c.BlockData[z] != 0)
                        c.BlockData[z] += sign * (ReadBit() << _al);
                    else
                    {
                        r--;
                        if (r == 0)
                            _successiveAcState = _successiveAcState == 2 ? 3 : 0;
                    }
                    break;

                case 3: // place the new value at a zero-history coefficient
                    if (c.BlockData[z] != 0)
                        c.BlockData[z] += sign * (ReadBit() << _al);
                    else
                    {
                        c.BlockData[z] = _successiveAcNextValue << _al;
                        _successiveAcState = 0;
                    }
                    break;

                case 4: // EOB run: only refine existing non-zero coefficients
                    if (c.BlockData[z] != 0)
                        c.BlockData[z] += sign * (ReadBit() << _al);
                    break;
            }

            k++;
        }

        if (_successiveAcState == 4)
        {
            _eobrun--;
            if (_eobrun == 0) _successiveAcState = 0;
        }
    }

    //
    // Reconstruction (dequant + IDCT + upsample + color)
    //

    private FrameBuffer Reconstruct()
    {
        foreach (Component c in _components)
            ReconstructComponent(c);

        FrameBuffer fb = new FrameBuffer(_width, _height, PixelFormat.RGBA32);

        bool grayscale = _components.Length == 1;
        Component cy = _components[0];
        Component? cb = grayscale ? null : _components[1];
        Component? cr = grayscale ? null : _components[2];

        for (int y = 0; y < _height; y++)
        {
            for (int x = 0; x < _width; x++)
            {
                float Y = SampleComponent(cy, x, y);

                byte r, g, b;
                if (grayscale)
                {
                    r = g = b = ClampByte(Y);
                }
                else
                {
                    float cbv = SampleComponent(cb!, x, y) - 128f;
                    float crv = SampleComponent(cr!, x, y) - 128f;
                    r = ClampByte(Y + 1.402f * crv);
                    g = ClampByte(Y - 0.344136f * cbv - 0.714136f * crv);
                    b = ClampByte(Y + 1.772f * cbv);
                }

                fb.SetPixelUnsafe(x, y, PixelPacker.Pack(new ColorRGBA32(r, g, b, 255)));
            }
        }

        return fb;
    }

    private void ReconstructComponent(Component c)
    {
        int[] quant = _quant[c.QuantId]
            ?? throw new InvalidDataException($"Missing quant table {c.QuantId}.");

        c.PlaneWidth = c.BlocksPerLineForMcu * 8;
        c.PlaneHeight = c.BlocksPerColumnForMcu * 8;
        c.Plane = new byte[c.PlaneWidth * c.PlaneHeight];

        float[] spatial = new float[64];

        for (int br = 0; br < c.BlocksPerColumn; br++)
        {
            for (int bc = 0; bc < c.BlocksPerLine; bc++)
            {
                int offset = BlockOffset(c, br, bc);
                Idct(c.BlockData, offset, quant, spatial);

                int px = bc * 8;
                int py = br * 8;

                for (int yy = 0; yy < 8; yy++)
                {
                    int outY = py + yy;
                    if (outY >= c.PlaneHeight) break;
                    int rowBase = outY * c.PlaneWidth + px;

                    for (int xx = 0; xx < 8; xx++)
                    {
                        int outX = px + xx;
                        if (outX >= c.PlaneWidth) break;
                        c.Plane[rowBase + xx] = ClampByte(spatial[yy * 8 + xx]);
                    }
                }
            }
        }
    }

    private float SampleComponent(Component c, int x, int y)
    {
        int sx = x * c.H / _maxH;
        int sy = y * c.V / _maxV;
        if (sx >= c.PlaneWidth) sx = c.PlaneWidth - 1;
        if (sy >= c.PlaneHeight) sy = c.PlaneHeight - 1;
        return c.Plane[sy * c.PlaneWidth + sx];
    }

    //
    // IDCT
    //

    private void Idct(int[] data, int offset, int[] quant, float[] outSpatial)
    {
        Span<float> coef = stackalloc float[64];
        for (int k = 0; k < 64; k++)
            coef[ZigZag[k]] = data[offset + k] * quant[k];

        Span<float> tmp = stackalloc float[64];

        for (int v = 0; v < 8; v++)
        {
            int row = v * 8;
            for (int xpix = 0; xpix < 8; xpix++)
            {
                float sum = 0f;
                for (int u = 0; u < 8; u++)
                    sum += IdctBasis[xpix, u] * coef[row + u];
                tmp[row + xpix] = sum * 0.5f;
            }
        }

        for (int xpix = 0; xpix < 8; xpix++)
        {
            for (int ypix = 0; ypix < 8; ypix++)
            {
                float sum = 0f;
                for (int v = 0; v < 8; v++)
                    sum += IdctBasis[ypix, v] * tmp[v * 8 + xpix];
                outSpatial[ypix * 8 + xpix] = sum * 0.5f + 128f;
            }
        }
    }

    private static float[,] BuildIdctBasis()
    {
        float[,] basis = new float[8, 8];
        for (int k = 0; k < 8; k++)
            for (int n = 0; n < 8; n++)
            {
                float cn = n == 0 ? 1.0f / MathF.Sqrt(2.0f) : 1.0f;
                basis[k, n] = cn * MathF.Cos((2 * k + 1) * n * MathF.PI / 16.0f);
            }
        return basis;
    }

    //
    // Huffman
    //

    private static HuffmanTable BuildHuffmanTable(int[] counts, byte[] symbols)
    {
        HuffmanTable table = new HuffmanTable { Symbols = symbols };
        int code = 0;
        int p = 0;

        for (int l = 1; l <= 16; l++)
        {
            if (counts[l] > 0)
            {
                table.ValPtr[l] = p;
                table.MinCode[l] = code;
                code += counts[l];
                p += counts[l];
                table.MaxCode[l] = code - 1;
            }
            else table.MaxCode[l] = -1;

            code <<= 1;
        }

        return table;
    }

    private int DecodeHuffman(HuffmanTable table)
    {
        int code = 0;
        for (int l = 1; l <= 16; l++)
        {
            code = (code << 1) | ReadBit();
            if (table.MaxCode[l] >= 0 && code <= table.MaxCode[l])
                return table.Symbols[table.ValPtr[l] + (code - table.MinCode[l])];
        }
        return 0;
    }

    private static int Extend(int value, int size)
    {
        if (size == 0) return 0;
        int threshold = 1 << (size - 1);
        return value < threshold ? value - (1 << size) + 1 : value;
    }

    //
    // Bit reader (0xFF00 de-stuffing; stops at any marker, leaving _pos on 0xFF)
    //

    private int ReadBit()
    {
        if (_bitCount == 0)
        {
            if (_markerHit || _pos >= _data.Length)
                return 0;

            int b = _data[_pos];

            if (b == 0xFF)
            {
                int next = _pos + 1 < _data.Length ? _data[_pos + 1] : 0xD9;
                if (next == 0x00)
                {
                    _pos += 2; // stuffed: literal 0xFF
                }
                else
                {
                    _markerHit = true; // leave _pos on the 0xFF of the marker
                    return 0;
                }
            }
            else
            {
                _pos++;
            }

            _bitBuffer = b;
            _bitCount = 8;
        }

        _bitCount--;
        return (_bitBuffer >> _bitCount) & 1;
    }

    private int ReadBits(int count)
    {
        int value = 0;
        for (int i = 0; i < count; i++)
            value = (value << 1) | ReadBit();
        return value;
    }

    private void HandleRestart()
    {
        _bitCount = 0;

        if (_pos + 1 < _data.Length &&
            _data[_pos] == 0xFF && _data[_pos + 1] >= 0xD0 && _data[_pos + 1] <= 0xD7)
        {
            _pos += 2;
        }
        else
        {
            // Not yet aligned on the marker: scan forward to the next FF Dn.
            while (_pos + 1 < _data.Length &&
                   !(_data[_pos] == 0xFF && _data[_pos + 1] >= 0xD0 && _data[_pos + 1] <= 0xD7))
                _pos++;
            if (_pos + 1 < _data.Length) _pos += 2;
        }

        _markerHit = false;
        _eobrun = 0;
        _successiveAcState = 0;
        foreach (Component c in _scanComponents) c.Pred = 0;
    }

    /// <summary>Advances _pos to the 0xFF of the next real (non-RST) marker.</summary>
    private void AlignToNextMarker()
    {
        _bitCount = 0;

        while (_pos + 1 < _data.Length)
        {
            if (_data[_pos] == 0xFF)
            {
                byte m = _data[_pos + 1];
                if (m != 0x00 && !(m >= 0xD0 && m <= 0xD7))
                    break;
            }
            _pos++;
        }
    }

    //
    // Helpers
    //

    private int ReadUInt16()
    {
        int value = (_data[_pos] << 8) | _data[_pos + 1];
        _pos += 2;
        return value;
    }

    private static int CeilDiv(int a, int b) => (a + b - 1) / b;

    private static byte ClampByte(float v)
    {
        if (v <= 0f) return 0;
        if (v >= 255f) return 255;
        return (byte)(v + 0.5f);
    }
}
