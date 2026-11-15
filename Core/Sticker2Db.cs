using System.Buffers.Binary;

using System.IO;

namespace TDU2SaveEditor.Core;

/// <summary>
/// A TDU2 <c>.2DB</c> "BMAP" texture (the format behind the savegame
/// <c>STICKERS\*.STI</c> files). Shipped stickers are 1024x1024 DXT5 with a
/// 9-level mip chain (1398096 bytes of pixel payload after the 80-byte header).
/// </summary>
public sealed class StickerImage
{
    public int Width { get; init; }
    public int Height { get; init; }
    public int MipCount { get; init; }

    /// <summary>"DXT1", "DXT5" or "ARGB8".</summary>
    public string Format { get; init; } = "";

    /// <summary>The raw 80-byte .2DB/BMAP header, kept verbatim for re-writing.</summary>
    public byte[] Header { get; init; } = Array.Empty<byte>();

    /// <summary>Decoded mip level 0 as straight (non-premultiplied) Bgra32.</summary>
    public byte[] Bgra { get; init; } = Array.Empty<byte>();

    public byte[] Payload { get; init; } = Array.Empty<byte>();
}

/// <summary>
/// Reader/writer for the TDU2 <c>.2DB</c> "BMAP" texture container used by the
/// savegame <c>STICKERS</c> folder. Layout (little-endian):
/// <code>
///   0  u16 file_version     24 u32 some_size       48 u32 (format/unk)
///   2  u16 unk1             28 u32 some_other_size 52 u32 unk6
///   4  u32 unk2             32 u8[8] name key      56 u32 unk7
///   8  u32 size             40 u16 width           60 u32 param6
///   12 u8[4] ".2DB"         42 u16 height          64 u32 flags
///   16 u8[4] "BMAP"         44 u16 param4          68 u32 unk9
///   20 u16 unk3/unk4        46 u8  mip_count       72 u32 unk10
///                           47 u8  unk5            76 u32 unk11
///   80 image payload (DXT5 for shipped stickers)
/// </code>
/// </summary>
public static class Sticker2Db
{
    public const int HeaderSize = 80;

    private const uint MagicDot2db = 0x4244322E; // ".2DB" little-endian
    private const uint MagicBmap = 0x50414D42;    // "BMAP" little-endian

    public static bool IsSticker(ReadOnlySpan<byte> d)
    {
        if (d.Length < HeaderSize) return false;
        uint a = BitConverter.ToUInt32(d.Slice(12, 4));
        uint b = BitConverter.ToUInt32(d.Slice(16, 4));
        return a == MagicDot2db && b == MagicBmap;
    }

    public static bool TryParse(byte[] data, out StickerImage image)
    {
        image = null!;
        if (!IsSticker(data)) return false;

        int width = BitConverter.ToUInt16(data, 40);
        int height = BitConverter.ToUInt16(data, 42);
        int mips = data[46];
        if (width <= 0 || height <= 0 || mips <= 0) return false;

        byte[] header = data[..HeaderSize];
        byte[] payload = data[HeaderSize..];

        string format;
        byte[] bgra;
        if (payload.Length == ChainSize(width, height, mips, 16, false))
        {
            format = "DXT5";
            bgra = DecodeDxt5(payload, width, height);
        }
        else if (payload.Length == ChainSize(width, height, mips, 8, false))
        {
            format = "DXT1";
            bgra = DecodeDxt1(payload, width, height);
        }
        else if (payload.Length == ChainSize(width, height, mips, 4, true))
        {
            format = "ARGB8";
            bgra = DecodeArgb8(payload, width, height);
        }
        else
        {
            return false;
        }

        image = new StickerImage
        {
            Width = width,
            Height = height,
            MipCount = mips,
            Format = format,
            Header = header,
            Bgra = bgra,
            Payload = payload
        };
        return true;
    }

    // ---------------------------------------------------------------------
    // Sizing
    // ---------------------------------------------------------------------

    /// <summary>
    /// Total byte size of a mip chain. Block formats use 4x4 blocks (min one
    /// block per level); <paramref name="perPixel"/> selects 4-byte/pixel instead.
    /// </summary>
    public static long ChainSize(int width, int height, int mips, int bytesPerUnit, bool perPixel)
    {
        long total = 0;
        int w = width, h = height;
        for (int i = 0; i < mips; i++)
        {
            total += perPixel
                ? (long)w * h * bytesPerUnit
                : (long)Math.Max(1, (w + 3) / 4) * Math.Max(1, (h + 3) / 4) * bytesPerUnit;
            w = Math.Max(1, w / 2);
            h = Math.Max(1, h / 2);
        }
        return total;
    }

    // ---------------------------------------------------------------------
    // Decode to Bgra32
    // ---------------------------------------------------------------------

    public static byte[] DecodeDxt5(byte[] payload, int width, int height)
    {
        var bgra = new byte[width * height * 4];
        int bw = Math.Max(1, (width + 3) / 4);
        int bh = Math.Max(1, (height + 3) / 4);
        int p = 0;
        for (int by = 0; by < bh; by++)
        {
            for (int bx = 0; bx < bw; bx++)
            {
                DecodeDxt5Block(payload, p, bgra, width, height, bx * 4, by * 4);
                p += 16;
            }
        }
        return bgra;
    }

    private static void DecodeDxt5Block(byte[] src, int o, byte[] dst, int w, int h, int ox, int oy)
    {
        Span<byte> alpha = stackalloc byte[8];
        byte a0 = src[o], a1 = src[o + 1];
        alpha[0] = a0; alpha[1] = a1;
        if (a0 > a1)
        {
            for (int i = 1; i <= 6; i++)
                alpha[i + 1] = (byte)(((7 - i) * a0 + i * a1) / 7);
        }
        else
        {
            for (int i = 1; i <= 4; i++)
                alpha[i + 1] = (byte)(((5 - i) * a0 + i * a1) / 5);
            alpha[6] = 0;
            alpha[7] = 255;
        }
        ulong abits = 0;
        for (int i = 0; i < 6; i++) abits |= (ulong)src[o + 2 + i] << (8 * i);

        ushort c0 = (ushort)(src[o + 8] | (src[o + 9] << 8));
        ushort c1 = (ushort)(src[o + 10] | (src[o + 11] << 8));
        Span<byte> col = stackalloc byte[12];
        Rgb565(c0, col, 0);
        Rgb565(c1, col, 3);
        if (c0 > c1)
        {
            for (int k = 0; k < 3; k++)
            {
                col[6 + k] = (byte)((2 * col[k] + col[3 + k]) / 3);
                col[9 + k] = (byte)((col[k] + 2 * col[3 + k]) / 3);
            }
        }
        else
        {
            for (int k = 0; k < 3; k++)
                col[6 + k] = (byte)((col[k] + col[3 + k]) / 2);
            col[9] = col[10] = col[11] = 0;
        }
        uint cbits = (uint)(src[o + 12] | (src[o + 13] << 8) | (src[o + 14] << 16) | (src[o + 15] << 24));

        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                int px = ox + x, py = oy + y;
                if (px >= w || py >= h) continue;
                int i = y * 4 + x;
                byte a = alpha[(int)((abits >> (3 * i)) & 7)];
                int ci = (int)((cbits >> (2 * i)) & 3) * 3;
                int d = (py * w + px) * 4;
                dst[d] = col[ci];
                dst[d + 1] = col[ci + 1];
                dst[d + 2] = col[ci + 2];
                dst[d + 3] = a;
            }
        }
    }

    public static byte[] DecodeDxt1(byte[] payload, int width, int height)
    {
        var bgra = new byte[width * height * 4];
        int bw = Math.Max(1, (width + 3) / 4);
        int bh = Math.Max(1, (height + 3) / 4);
        int p = 0;
        Span<byte> col = stackalloc byte[12];
        for (int by = 0; by < bh; by++)
        {
            for (int bx = 0; bx < bw; bx++)
            {
                ushort c0 = (ushort)(payload[p] | (payload[p + 1] << 8));
                ushort c1 = (ushort)(payload[p + 2] | (payload[p + 3] << 8));
                Rgb565(c0, col, 0);
                Rgb565(c1, col, 3);
                if (c0 > c1)
                {
                    for (int k = 0; k < 3; k++)
                    {
                        col[6 + k] = (byte)((2 * col[k] + col[3 + k]) / 3);
                        col[9 + k] = (byte)((col[k] + 2 * col[3 + k]) / 3);
                    }
                }
                else
                {
                    for (int k = 0; k < 3; k++) col[6 + k] = (byte)((col[k] + col[3 + k]) / 2);
                    col[9] = col[10] = col[11] = 0;
                }
                uint cbits = (uint)(payload[p + 4] | (payload[p + 5] << 8) | (payload[p + 6] << 16) | (payload[p + 7] << 24));
                for (int y = 0; y < 4; y++)
                {
                    for (int x = 0; x < 4; x++)
                    {
                        int px = bx * 4 + x, py = by * 4 + y;
                        if (px >= width || py >= height) continue;
                        int i = y * 4 + x;
                        int ci = (int)((cbits >> (2 * i)) & 3);
                        int d = (py * width + px) * 4;
                        if (ci == 3 && c0 <= c1)
                        {
                            bgra[d] = bgra[d + 1] = bgra[d + 2] = bgra[d + 3] = 0;
                        }
                        else
                        {
                            bgra[d] = col[ci * 3];
                            bgra[d + 1] = col[ci * 3 + 1];
                            bgra[d + 2] = col[ci * 3 + 2];
                            bgra[d + 3] = 255;
                        }
                    }
                }
                p += 8;
            }
        }
        return bgra;
    }

    public static byte[] DecodeArgb8(byte[] payload, int width, int height)
    {
        var bgra = new byte[width * height * 4];
        int n = Math.Min(bgra.Length, payload.Length);
        Buffer.BlockCopy(payload, 0, bgra, 0, n);
        return bgra;
    }

    private static void Rgb565(ushort c, Span<byte> dst, int o)
    {
        dst[o] = (byte)((c & 0x1F) * 255 / 31);
        dst[o + 1] = (byte)(((c >> 5) & 0x3F) * 255 / 63);
        dst[o + 2] = (byte)(((c >> 11) & 0x1F) * 255 / 31);
    }

    // ---------------------------------------------------------------------
    // Encode (DXT5) + container write
    // ---------------------------------------------------------------------

    /// <summary>
    /// DXT5-encodes a Bgra32 level-0 image and all its mip levels (box-filtered),
    /// producing the exact payload bytes for the given dimensions and mip count.
    /// </summary>
    public static byte[] EncodeDxt5(byte[] bgra, int width, int height, int mips)
    {
        using var ms = new MemoryStream();
        byte[] level = bgra;
        int w = width, h = height;
        for (int m = 0; m < mips; m++)
        {
            EncodeDxt5Level(level, w, h, ms);
            if (m + 1 < mips)
            {
                int nw = Math.Max(1, w / 2), nh = Math.Max(1, h / 2);
                level = Downsample(level, w, h, nw, nh);
                w = nw; h = nh;
            }
        }
        return ms.ToArray();
    }

    private static void EncodeDxt5Level(byte[] bgra, int w, int h, Stream outStream)
    {
        int bw = Math.Max(1, (w + 3) / 4);
        int bh = Math.Max(1, (h + 3) / 4);
        var block = new byte[16];
        var ar = new int[16];
        var ag = new int[16];
        var ab = new int[16];
        var aa = new int[16];
        for (int by = 0; by < bh; by++)
        {
            for (int bx = 0; bx < bw; bx++)
            {
                for (int y = 0; y < 4; y++)
                {
                    for (int x = 0; x < 4; x++)
                    {
                        int px = bx * 4 + x, py = by * 4 + y;
                        int i = y * 4 + x;
                        if (px < w && py < h)
                        {
                            int d = (py * w + px) * 4;
                            ab[i] = bgra[d];
                            ag[i] = bgra[d + 1];
                            ar[i] = bgra[d + 2];
                            aa[i] = bgra[d + 3];
                        }
                        else
                        {
                            ab[i] = ag[i] = ar[i] = 0;
                            aa[i] = 0;
                        }
                    }
                }
                EncodeDxt5Block(ar, ag, ab, aa, block);
                outStream.Write(block, 0, 16);
            }
        }
    }

    private static void EncodeDxt5Block(int[] r, int[] g, int[] b, int[] a, byte[] outBlock)
    {
        // ---- Alpha (8-value interpolated, a0 > a1 form) -------------------
        int amin = 255, amax = 0;
        for (int i = 0; i < 16; i++)
        {
            if (a[i] < amin) amin = a[i];
            if (a[i] > amax) amax = a[i];
        }
        byte a0, a1;
        if (amin == amax)
        {
            outBlock[0] = (byte)amin;
            outBlock[1] = (byte)amin;
            for (int i = 2; i < 8; i++) outBlock[i] = 0;
        }
        else
        {
            a0 = (byte)amax;
            a1 = (byte)amin;
            if (a0 <= a1) a0 = (byte)Math.Min(255, a1 + 1);
            outBlock[0] = a0;
            outBlock[1] = a1;
            int[] pal = new int[8];
            pal[0] = outBlock[0];
            pal[1] = outBlock[1];
            for (int i = 1; i <= 6; i++)
                pal[i + 1] = ((7 - i) * pal[0] + i * pal[1]) / 7;
            ulong bits = 0;
            for (int i = 0; i < 16; i++)
            {
                int best = 0, bestD = int.MaxValue;
                for (int k = 0; k < 8; k++)
                {
                    int d = Math.Abs(a[i] - pal[k]);
                    if (d < bestD) { bestD = d; best = k; }
                }
                bits |= (ulong)best << (3 * i);
            }
            for (int i = 0; i < 6; i++) outBlock[2 + i] = (byte)(bits >> (8 * i));
        }

        int rMin = int.MaxValue, rMax = 0, gMin = int.MaxValue, gMax = 0, bMin = int.MaxValue, bMax = 0;
        for (int i = 0; i < 16; i++)
        {
            if (r[i] < rMin) rMin = r[i]; if (r[i] > rMax) rMax = r[i];
            if (g[i] < gMin) gMin = g[i]; if (g[i] > gMax) gMax = g[i];
            if (b[i] < bMin) bMin = b[i]; if (b[i] > bMax) bMax = b[i];
        }
        ushort c0 = To565(rMax, gMax, bMax);
        ushort c1 = To565(rMin, gMin, bMin);
        if (c0 <= c1)
        {
            if (c0 > 0) c1 = (ushort)(c0 - 1);
            else c0 = 1;
        }
        Span<byte> col = stackalloc byte[12];
        Rgb565(c0, col, 0);
        Rgb565(c1, col, 3);
        col[6] = (byte)((2 * col[0] + col[3]) / 3);
        col[7] = (byte)((2 * col[1] + col[4]) / 3);
        col[8] = (byte)((2 * col[2] + col[5]) / 3);
        col[9] = (byte)((col[0] + 2 * col[3]) / 3);
        col[10] = (byte)((col[1] + 2 * col[4]) / 3);
        col[11] = (byte)((col[2] + 2 * col[5]) / 3);

        outBlock[8] = (byte)(c0 & 0xFF);
        outBlock[9] = (byte)(c0 >> 8);
        outBlock[10] = (byte)(c1 & 0xFF);
        outBlock[11] = (byte)(c1 >> 8);

        uint cbits = 0;
        for (int i = 0; i < 16; i++)
        {
            int best = 0, bestD = int.MaxValue;
            for (int k = 0; k < 4; k++)
            {
                int dr = r[i] - col[k * 3];
                int dg = g[i] - col[k * 3 + 1];
                int db = b[i] - col[k * 3 + 2];
                int d = dr * dr + dg * dg + db * db;
                if (d < bestD) { bestD = d; best = k; }
            }
            cbits |= (uint)best << (2 * i);
        }
        outBlock[12] = (byte)(cbits & 0xFF);
        outBlock[13] = (byte)((cbits >> 8) & 0xFF);
        outBlock[14] = (byte)((cbits >> 16) & 0xFF);
        outBlock[15] = (byte)((cbits >> 24) & 0xFF);
    }

    private static ushort To565(int r, int g, int b)
        => (ushort)(((r >> 3) << 11) | ((g >> 2) << 5) | (b >> 3));

    private static byte[] Downsample(byte[] src, int sw, int sh, int dw, int dh)
    {
        var dst = new byte[dw * dh * 4];
        for (int y = 0; y < dh; y++)
        {
            int sy0 = y * 2, sy1 = Math.Min(sh - 1, sy0 + 1);
            for (int x = 0; x < dw; x++)
            {
                int sx0 = x * 2, sx1 = Math.Min(sw - 1, sx0 + 1);
                int a = 0, r = 0, g = 0, b = 0, n = 0;
                for (int yy = sy0; yy <= sy1; yy++)
                {
                    for (int xx = sx0; xx <= sx1; xx++)
                    {
                        int s = (yy * sw + xx) * 4;
                        b += src[s]; g += src[s + 1]; r += src[s + 2]; a += src[s + 3]; n++;
                    }
                }
                int d = (y * dw + x) * 4;
                dst[d] = (byte)(b / n);
                dst[d + 1] = (byte)(g / n);
                dst[d + 2] = (byte)(r / n);
                dst[d + 3] = (byte)(a / n);
            }
        }
        return dst;
    }

    /// <summary>
    /// Builds a complete .2DB/BMAP file from a template header (copied verbatim,
    /// keeping the name key and unknown fields) and a fresh DXT5 payload.
    /// </summary>
    public static byte[] BuildSticker(byte[] templateHeader, int width, int height, int mips, byte[] dxt5Payload)
    {
        var header = new byte[HeaderSize];
        Array.Copy(templateHeader, header, HeaderSize);

        int size = HeaderSize + dxt5Payload.Length;
        WriteU32(header, 8, (uint)size);
        WriteU16(header, 40, (ushort)width);
        WriteU16(header, 42, (ushort)height);
        header[46] = (byte)mips;
        header[47] = (byte)mips;
        WriteU32(header, 24, (uint)(size - 16));
        WriteU32(header, 28, (uint)(size - 16));

        var result = new byte[size];
        Buffer.BlockCopy(header, 0, result, 0, HeaderSize);
        Buffer.BlockCopy(dxt5Payload, 0, result, HeaderSize, dxt5Payload.Length);
        return result;
    }

    private static void WriteU16(byte[] b, int o, ushort v)
    {
        b[o] = (byte)(v & 0xFF);
        b[o + 1] = (byte)(v >> 8);
    }

    private static void WriteU32(byte[] b, int o, uint v)
    {
        b[o] = (byte)(v & 0xFF);
        b[o + 1] = (byte)((v >> 8) & 0xFF);
        b[o + 2] = (byte)((v >> 16) & 0xFF);
        b[o + 3] = (byte)((v >> 24) & 0xFF);
    }
}
