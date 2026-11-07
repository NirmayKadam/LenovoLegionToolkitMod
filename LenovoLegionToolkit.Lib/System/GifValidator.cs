using System;
using System.Text;

namespace LenovoLegionToolkit.Lib.System;

/// <summary>
/// Strict structural check for GIF boot logos. The firmware decodes the logo before Windows starts, and
/// firmware image parsers have had memory-safety bugs ("LogoFAIL", 2023). Animated GIFs can't be re-encoded
/// without losing timing, so instead anything outside a plain, well-formed GIF is rejected:
/// known blocks only, frames inside the screen, valid LZW data that never refers to colors outside the palette
/// or produces more pixels than the frame holds, and nothing after the trailer.
/// </summary>
internal static class GifValidator
{
    public const int MAX_FRAMES = 1000;
    public const int MAX_FILE_BYTES = 32 * 1024 * 1024;

    private const int MAX_LZW_CODES = 4096;

    /// <summary>
    /// Throws <see cref="InvalidBootLogoImageFormatException"/> for malformed data and
    /// <see cref="InvalidBootLogoImageSizeException"/> when the image is larger than allowed.
    /// </summary>
    public static void Validate(byte[] data, int maxWidth, int maxHeight)
    {
        if (data.Length > MAX_FILE_BYTES)
            throw new InvalidBootLogoImageSizeException();

        var reader = new Reader(data);

        var signature = Encoding.ASCII.GetString(reader.Bytes(6));
        if (signature is not ("GIF87a" or "GIF89a"))
            throw Format("bad signature");

        var screenWidth = reader.UInt16();
        var screenHeight = reader.UInt16();
        var screenFlags = reader.Byte();
        reader.Bytes(2); // background color index, pixel aspect ratio

        if (screenWidth == 0 || screenHeight == 0)
            throw Format("empty logical screen");
        if (screenWidth > maxWidth || screenHeight > maxHeight)
            throw new InvalidBootLogoImageSizeException();

        var globalColors = 0;
        if ((screenFlags & 0x80) != 0)
        {
            globalColors = 1 << ((screenFlags & 0x07) + 1);
            reader.Bytes(3 * globalColors);
        }

        var frames = 0;
        while (true)
        {
            var introducer = reader.Byte();
            switch (introducer)
            {
                case 0x3B: // trailer
                    if (frames == 0)
                        throw Format("no frames");
                    if (!reader.AtEnd)
                        throw Format("data after trailer");
                    return;

                case 0x21: // extension
                    ReadExtension(reader);
                    break;

                case 0x2C: // image
                    ReadImage(reader, screenWidth, screenHeight, globalColors);
                    if (++frames > MAX_FRAMES)
                        throw Format("too many frames");
                    break;

                default:
                    throw Format($"unknown block 0x{introducer:X2}");
            }
        }
    }

    private static void ReadExtension(Reader reader)
    {
        var label = reader.Byte();
        switch (label)
        {
            case 0xF9: // graphic control
                if (reader.Byte() != 4)
                    throw Format("bad graphic control size");
                reader.Bytes(4);
                if (reader.Byte() != 0)
                    throw Format("graphic control not terminated");
                break;

            case 0xFF: // application
                if (reader.Byte() != 11)
                    throw Format("bad application extension size");
                var id = Encoding.ASCII.GetString(reader.Bytes(11));
                if (id is not ("NETSCAPE2.0" or "ANIMEXTS1.0"))
                    throw Format($"unknown application extension {id}");
                ReadSubBlocks(reader);
                break;

            default:
                throw Format($"extension 0x{label:X2} not allowed");
        }
    }

    private static void ReadImage(Reader reader, int screenWidth, int screenHeight, int globalColors)
    {
        var left = reader.UInt16();
        var top = reader.UInt16();
        var width = reader.UInt16();
        var height = reader.UInt16();
        var flags = reader.Byte();

        if (width == 0 || height == 0)
            throw Format("empty frame");
        if (left + width > screenWidth || top + height > screenHeight)
            throw Format("frame outside the logical screen");

        var colors = globalColors;
        if ((flags & 0x80) != 0)
        {
            colors = 1 << ((flags & 0x07) + 1);
            reader.Bytes(3 * colors);
        }

        if (colors == 0)
            throw Format("frame has no color table");

        var minCodeSize = reader.Byte();
        if (minCodeSize is < 2 or > 8)
            throw Format("bad LZW minimum code size");

        var lzw = ReadSubBlocks(reader);
        ValidateLzw(lzw, minCodeSize, (long)width * height, colors);
    }

    private static byte[] ReadSubBlocks(Reader reader)
    {
        var buffer = new global::System.IO.MemoryStream();
        while (true)
        {
            var size = reader.Byte();
            if (size == 0)
                return buffer.ToArray();
            buffer.Write(reader.Bytes(size));
        }
    }

    /// <summary>
    /// Decodes the LZW stream without producing pixels: checks every code is defined, literal codes are inside
    /// the palette, and the frame never receives more pixels than it holds.
    /// </summary>
    internal static void ValidateLzw(byte[] data, int minCodeSize, long maxPixels, int paletteSize)
    {
        var clear = 1 << minCodeSize;
        var end = clear + 1;
        var lengths = new int[MAX_LZW_CODES];
        for (var i = 0; i < clear; i++)
            lengths[i] = 1;

        var codeSize = minCodeSize + 1;
        var next = end + 1;
        var previous = -1;
        long pixels = 0;

        var bitPosition = 0L;
        var totalBits = (long)data.Length * 8;

        while (bitPosition + codeSize <= totalBits)
        {
            var code = 0;
            for (var i = 0; i < codeSize; i++, bitPosition++)
            {
                if ((data[bitPosition >> 3] & (1 << (int)(bitPosition & 7))) != 0)
                    code |= 1 << i;
            }

            if (code == clear)
            {
                codeSize = minCodeSize + 1;
                next = end + 1;
                previous = -1;
                continue;
            }

            if (code == end)
                return;

            int length;
            if (code < clear)
            {
                if (code >= paletteSize)
                    throw Format("pixel refers to a color outside the palette");
                length = 1;
            }
            else if (previous == -1)
            {
                throw Format("stream starts with a non-literal code");
            }
            else if (code < next)
            {
                length = lengths[code];
            }
            else if (code == next)
            {
                length = lengths[previous] + 1;
            }
            else
            {
                throw Format("undefined LZW code");
            }

            if (previous != -1 && next < MAX_LZW_CODES)
            {
                lengths[next] = lengths[previous] + 1;
                next++;
                if (next == 1 << codeSize && codeSize < 12)
                    codeSize++;
            }

            pixels += length;
            if (pixels > maxPixels)
                throw Format("more pixels than the frame holds");

            previous = code;
        }
    }

    private static InvalidBootLogoImageFormatException Format(string reason)
    {
        Utils.Log.Instance.Trace($"Rejected GIF boot logo: {reason}.");
        return new InvalidBootLogoImageFormatException();
    }

    private sealed class Reader(byte[] data)
    {
        private int _position;

        public bool AtEnd => _position == data.Length;

        public byte Byte()
        {
            if (_position >= data.Length)
                throw Format("unexpected end of file");
            return data[_position++];
        }

        public int UInt16() => Byte() | (Byte() << 8);

        public byte[] Bytes(int count)
        {
            if (count < 0 || _position + count > data.Length)
                throw Format("unexpected end of file");
            var result = data.AsSpan(_position, count).ToArray();
            _position += count;
            return result;
        }
    }
}
