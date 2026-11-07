using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using LenovoLegionToolkit.Lib.System;
using Xunit;

namespace LenovoLegionToolkit.Tests.Security;

// GIFs come from Windows' own encoder (GDI+) or are the well-known minimal GIF, mutated byte by byte.
public class BootLogoTests : IDisposable
{
    private const int MaxW = 1920;
    private const int MaxH = 1080;

    // The classic 43-byte 1x1 GIF89a (2-color palette, graphic control extension, one frame).
    private static readonly byte[] MinimalGif =
    [
        0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x01, 0x00, 0x01, 0x00, 0x80, 0x00, 0x00, // header + screen (GCT, 2 colors)
        0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00,                                           // palette
        0x21, 0xF9, 0x04, 0x01, 0x00, 0x00, 0x00, 0x00,                               // graphic control
        0x2C, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00,                   // image 1x1 at 0,0
        0x02, 0x02, 0x44, 0x01, 0x00,                                                 // LZW min 2, data
        0x3B,                                                                         // trailer
    ];

    // Byte positions in MinimalGif: 0-12 header+screen, 13-18 palette, 19-26 graphic control,
    // 27 image separator, 28-31 left/top, 32-35 width/height, 36 flags, 37 LZW min size,
    // 38 sub-block size, 39-40 LZW data, 41 terminator, 42 trailer.
    private const int ImageWidthOffset = 32;     // low byte of image descriptor width
    private const int LzwMinOffset = 37;
    private const int LzwDataOffset = 39;        // two data bytes

    private readonly string _dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"llt_test_{Guid.NewGuid():N}")).FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    private static byte[] Mutate(Action<byte[]> change)
    {
        var copy = MinimalGif.ToArray();
        change(copy);
        return copy;
    }

    [Fact]
    public void MinimalGif_IsValid()
    {
        GifValidator.Validate(MinimalGif, MaxW, MaxH);
    }

    [Fact]
    public void GdiPlusStaticGif_IsValid()
    {
        using var bmp = new Bitmap(64, 32);
        using (var g = Graphics.FromImage(bmp))
            g.Clear(Color.OrangeRed);

        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Gif);

        GifValidator.Validate(ms.ToArray(), MaxW, MaxH);
    }

    [Fact]
    public void GdiPlusAnimatedGif_IsValid()
    {
        var path = Path.Combine(_dir, "anim.gif");
        var encoder = ImageCodecInfo.GetImageEncoders().First(e => e.FormatID == ImageFormat.Gif.Guid);

        // The bitmaps must be disposed before reading the file back: GDI+ finishes writing it on dispose.
        using (var first = new Bitmap(40, 20))
        using (var second = new Bitmap(40, 20))
        using (var p = new EncoderParameters(1))
        {
            using (var g = Graphics.FromImage(first)) g.Clear(Color.Red);
            using (var g = Graphics.FromImage(second)) g.Clear(Color.Blue);

            p.Param[0] = new EncoderParameter(global::System.Drawing.Imaging.Encoder.SaveFlag, (long)EncoderValue.MultiFrame);
            first.Save(path, encoder, p);
            p.Param[0] = new EncoderParameter(global::System.Drawing.Imaging.Encoder.SaveFlag, (long)EncoderValue.FrameDimensionTime);
            first.SaveAdd(second, p);
            p.Param[0] = new EncoderParameter(global::System.Drawing.Imaging.Encoder.SaveFlag, (long)EncoderValue.Flush);
            first.SaveAdd(p);
        }

        // Confirm with an independent decoder (WPF) that the file really is a 2-frame animation.
        using (var stream = File.OpenRead(path))
        {
            var decoder = new global::System.Windows.Media.Imaging.GifBitmapDecoder(stream,
                global::System.Windows.Media.Imaging.BitmapCreateOptions.None,
                global::System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
            Assert.Equal(2, decoder.Frames.Count);
        }

        GifValidator.Validate(File.ReadAllBytes(path), MaxW, MaxH);
    }

    [Fact]
    public void LargerThanSupported_IsSizeError()
    {
        Assert.Throws<InvalidBootLogoImageSizeException>(() => GifValidator.Validate(MinimalGif, 0, 0));
    }

    [Fact]
    public void FrameOutsideScreen_IsRejected()
    {
        var bad = Mutate(b => b[ImageWidthOffset] = 2); // 2-wide frame on a 1-wide screen
        Assert.Throws<InvalidBootLogoImageFormatException>(() => GifValidator.Validate(bad, MaxW, MaxH));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(9)]
    [InlineData(12)]
    public void BadLzwMinimumCodeSize_IsRejected(byte size)
    {
        var bad = Mutate(b => b[LzwMinOffset] = size);
        Assert.Throws<InvalidBootLogoImageFormatException>(() => GifValidator.Validate(bad, MaxW, MaxH));
    }

    [Fact]
    public void PixelOutsidePalette_IsRejected()
    {
        // clear, literal 3 (palette only has 2 colors), end
        var bad = Mutate(b => { b[LzwDataOffset] = 0x5C; b[LzwDataOffset + 1] = 0x01; });
        Assert.Throws<InvalidBootLogoImageFormatException>(() => GifValidator.Validate(bad, MaxW, MaxH));
    }

    [Fact]
    public void MorePixelsThanFrame_IsRejected()
    {
        // clear, literal 0, literal 0, end -> 2 pixels into a 1x1 frame
        var bad = Mutate(b => { b[LzwDataOffset] = 0x04; b[LzwDataOffset + 1] = 0x0A; });
        Assert.Throws<InvalidBootLogoImageFormatException>(() => GifValidator.Validate(bad, MaxW, MaxH));
    }

    // Pairs proving the two LZW tests above fail for their intended reason: the same bytes pass
    // once only that one limit is relaxed.
    [Fact]
    public void Lzw_LiteralThree_FailsOnlyBecauseOfPaletteSize()
    {
        byte[] data = [0x5C, 0x01];
        Assert.Throws<InvalidBootLogoImageFormatException>(() => GifValidator.ValidateLzw(data, 2, 1, 2));
        GifValidator.ValidateLzw(data, 2, 1, 4);
    }

    [Fact]
    public void Lzw_TwoPixels_FailsOnlyBecauseOfFrameSize()
    {
        byte[] data = [0x04, 0x0A];
        Assert.Throws<InvalidBootLogoImageFormatException>(() => GifValidator.ValidateLzw(data, 2, 1, 2));
        GifValidator.ValidateLzw(data, 2, 2, 2);
    }

    [Fact]
    public void Lzw_OriginalData_Passes()
    {
        GifValidator.ValidateLzw([0x44, 0x01], 2, 1, 2);
    }

    [Fact]
    public void FrameInsideLargerScreen_Passes()
    {
        // Same 2-wide frame as FrameOutsideScreen, but the logical screen is widened to 2: now valid,
        // so that test fails only because of the bounds check.
        var widened = Mutate(b => { b[ImageWidthOffset] = 2; b[6] = 2; });
        GifValidator.Validate(widened, MaxW, MaxH);
    }

    [Fact]
    public void CommentExtension_IsRejected()
    {
        var comment = new byte[] { 0x21, 0xFE, 0x03, 0x41, 0x42, 0x43, 0x00 };
        var bad = MinimalGif.Take(19).Concat(comment).Concat(MinimalGif.Skip(19)).ToArray();
        Assert.Throws<InvalidBootLogoImageFormatException>(() => GifValidator.Validate(bad, MaxW, MaxH));
    }

    [Fact]
    public void TrailingBytes_AreRejected()
    {
        var bad = MinimalGif.Concat(new byte[] { 0x00, 0x01 }).ToArray();
        Assert.Throws<InvalidBootLogoImageFormatException>(() => GifValidator.Validate(bad, MaxW, MaxH));
    }

    [Fact]
    public void MissingTrailer_IsRejected()
    {
        var bad = MinimalGif.Take(MinimalGif.Length - 1).ToArray();
        Assert.Throws<InvalidBootLogoImageFormatException>(() => GifValidator.Validate(bad, MaxW, MaxH));
    }

    [Fact]
    public void NotAGif_IsRejected()
    {
        Assert.Throws<InvalidBootLogoImageFormatException>(() => GifValidator.Validate([0x89, 0x50, 0x4E, 0x47, 0, 0, 0, 0], MaxW, MaxH));
    }

    [Fact]
    public void StaticPng_AlreadyCorrect_IsStillReEncoded()
    {
        var source = Path.Combine(_dir, "logo.png");
        var destination = Path.Combine(_dir, "out.png");

        using (var bmp = new Bitmap(120, 60, PixelFormat.Format24bppRgb))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.DarkSlateBlue);
                g.FillEllipse(Brushes.Gold, 10, 10, 40, 40);
            }
            bmp.Save(source, ImageFormat.Png);
        }

        // Add an extra chunk (like hidden metadata) before IEND; a byte-for-byte copy would keep it.
        var original = File.ReadAllBytes(source);
        var iend = original.Length - 12;
        byte[] extraChunk = [0, 0, 0, 4, (byte)'t', (byte)'E', (byte)'X', (byte)'t', (byte)'a', (byte)'b', (byte)'c', (byte)'d', 0, 0, 0, 0];
        File.WriteAllBytes(source, original.Take(iend).Concat(extraChunk).Concat(original.Skip(iend)).ToArray());

        BootLogo.ProcessAndSaveStaticImage(source, destination, ".png", MaxW, MaxH);

        var output = File.ReadAllBytes(destination);
        Assert.NotEqual(File.ReadAllBytes(source), output);
        Assert.False(ContainsSequence(output, "tEXtabcd"u8.ToArray()), "Extra chunk survived; the image wasn't re-encoded.");

        using var result = Image.FromFile(destination);
        Assert.Equal(120, result.Width);
        Assert.Equal(60, result.Height);
    }

    private static bool ContainsSequence(byte[] haystack, byte[] needle) =>
        Enumerable.Range(0, haystack.Length - needle.Length + 1).Any(i => haystack.AsSpan(i, needle.Length).SequenceEqual(needle));
}
