using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;
using Ovcuprim.Application.Abstractions;

namespace Ovcuprim.Infrastructure.Imaging;

/// <summary>
/// Decodes an upload, checks its dimensions and re-encodes every size as WebP. Re-encoding is the
/// security step as much as the sizing one: the stored bytes are produced here, so EXIF and any
/// payload smuggled into the original never reach storage.
/// </summary>
public sealed class ImageSharpProcessor : IImageProcessor
{
    private const int MinDimension = 200;
    private const int MaxDimension = 10_000;
    private const int MasterWidth = 1600;

    private static readonly (string Name, int Width, int Height)[] Variants =
    [
        ("thumb", 160, 160),
        ("card", 400, 300),
        ("detail", 1280, 0),
        ("og", 1200, 630)
    ];

    public async Task<ImageProcessingResult> ProcessAsync(Stream input, CancellationToken cancellationToken = default)
    {
        if (input.CanSeek)
        {
            input.Position = 0;
        }

        Image image;

        try
        {
            image = await Image.LoadAsync(input, cancellationToken);
        }
        catch (UnknownImageFormatException)
        {
            // HEIC is no longer accepted at the API boundary (B-3), but a declared content type is
            // only ever a claim — a client can call a .heic file "image/jpeg" and reach here anyway.
            // ImageSharp ships no HEIC decoder regardless, so this stays reported distinctly from an
            // ordinarily corrupt file.
            return ImageProcessingResult.Fail(IsHeic(input)
                ? ImageProcessingFailure.UnsupportedFormat
                : ImageProcessingFailure.Undecodable);
        }
        catch (InvalidImageContentException)
        {
            return ImageProcessingResult.Fail(ImageProcessingFailure.Undecodable);
        }

        using (image)
        {
            if (image.Width < MinDimension || image.Height < MinDimension
                || image.Width > MaxDimension || image.Height > MaxDimension)
            {
                return ImageProcessingResult.Fail(ImageProcessingFailure.OutOfBounds);
            }

            // Honour the EXIF orientation once, then drop the metadata entirely.
            image.Mutate(x => x.AutoOrient());
            image.Metadata.ExifProfile = null;
            image.Metadata.IptcProfile = null;
            image.Metadata.XmpProfile = null;

            var master = await RenderAsync(image, "master", MasterWidth, 0, cancellationToken);
            var variants = new List<ImageRendition>(Variants.Length);

            foreach (var (name, width, height) in Variants)
            {
                variants.Add(await RenderAsync(image, name, width, height, cancellationToken));
            }

            return ImageProcessingResult.Ok(new ProcessedImageSet(
                master.Width,
                master.Height,
                "image/webp",
                ".webp",
                master,
                variants));
        }
    }

    private static async Task<ImageRendition> RenderAsync(
        Image source, string name, int width, int height, CancellationToken cancellationToken)
    {
        // Height 0 means "keep the aspect ratio and bound the width", and a small original is
        // never enlarged — upscaling only wastes bytes. Fixed-size variants still crop to fill.
        var bounded = height == 0
            ? new Size(Math.Min(width, source.Width), 0)
            : new Size(width, height);

        using var copy = source.Clone(context => context.Resize(new ResizeOptions
        {
            Size = bounded,
            Mode = height == 0 ? ResizeMode.Max : ResizeMode.Crop,
            Position = AnchorPositionMode.Center
        }));

        using var buffer = new MemoryStream();
        await copy.SaveAsync(buffer, new WebpEncoder { Quality = 82 }, cancellationToken);

        return new ImageRendition(name, buffer.ToArray(), copy.Width, copy.Height);
    }

    private static bool IsHeic(Stream input)
    {
        if (!input.CanSeek)
        {
            return false;
        }

        input.Position = 0;
        Span<byte> header = stackalloc byte[12];
        var read = input.Read(header);
        input.Position = 0;

        return read == 12
            && header[4] == 'f' && header[5] == 't' && header[6] == 'y' && header[7] == 'p';
    }
}
