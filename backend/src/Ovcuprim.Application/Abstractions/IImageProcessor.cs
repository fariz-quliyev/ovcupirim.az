namespace Ovcuprim.Application.Abstractions;

/// <summary>One generated size of an uploaded image, already encoded and ready to store.</summary>
public sealed record ImageRendition(string Name, byte[] Content, int Width, int Height);

/// <summary>
/// The result of re-encoding an upload. Re-encoding is what strips EXIF and anything else hidden
/// in the original file, so the bytes written to storage are never the bytes that were uploaded.
/// </summary>
public sealed record ProcessedImageSet(
    int Width,
    int Height,
    string ContentType,
    string FileExtension,
    ImageRendition Master,
    IReadOnlyList<ImageRendition> Variants);

public enum ImageProcessingFailure
{
    None = 0,

    /// <summary>The bytes are not an image this build can decode.</summary>
    Undecodable = 1,

    /// <summary>
    /// A recognised format for which no decoder is installed — HEIC, the one format the upload
    /// contract no longer declares as accepted (B-3), but bytes are sniffed regardless of the
    /// declared content type, so a spoofed one still lands here rather than as a corrupt file.
    /// </summary>
    UnsupportedFormat = 2,

    /// <summary>Decodable, but outside the accepted pixel bounds.</summary>
    OutOfBounds = 3
}

public sealed record ImageProcessingResult(ProcessedImageSet? Value, ImageProcessingFailure Failure)
{
    public bool Succeeded => Value is not null;

    public static ImageProcessingResult Ok(ProcessedImageSet set) => new(set, ImageProcessingFailure.None);

    public static ImageProcessingResult Fail(ImageProcessingFailure failure) => new(null, failure);
}

/// <summary>Decodes, bounds-checks and re-encodes an uploaded image into the sizes the site serves.</summary>
public interface IImageProcessor
{
    Task<ImageProcessingResult> ProcessAsync(Stream input, CancellationToken cancellationToken = default);
}
