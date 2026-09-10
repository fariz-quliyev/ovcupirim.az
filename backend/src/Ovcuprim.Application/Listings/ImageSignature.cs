namespace Ovcuprim.Application.Listings;

public enum ImageSignatureKind
{
    Unknown = 0,
    Jpeg = 1,
    Png = 2,
    WebP = 3,
    Heic = 4
}

/// <summary>
/// Identifies an upload by its leading bytes. A browser-supplied content type can say anything,
/// so the magic number decides whether a file is treated as an image at all.
/// </summary>
public static class ImageSignature
{
    private const int HeaderLength = 32;

    public static async Task<ImageSignatureKind> DetectAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        var header = new byte[HeaderLength];
        var read = 0;

        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        while (read < HeaderLength)
        {
            var chunk = await stream.ReadAsync(header.AsMemory(read, HeaderLength - read), cancellationToken);

            if (chunk == 0)
            {
                break;
            }

            read += chunk;
        }

        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        return Detect(header.AsSpan(0, read));
    }

    public static ImageSignatureKind Detect(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return ImageSignatureKind.Jpeg;
        }

        if (header.Length >= 8
            && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47
            && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
        {
            return ImageSignatureKind.Png;
        }

        // RIFF....WEBP
        if (header.Length >= 12
            && header[0] == 'R' && header[1] == 'I' && header[2] == 'F' && header[3] == 'F'
            && header[8] == 'W' && header[9] == 'E' && header[10] == 'B' && header[11] == 'P')
        {
            return ImageSignatureKind.WebP;
        }

        // ISO base media file with an HEIF brand: ....ftyp{heic|heix|hevc|heim|heis|mif1|msf1}
        if (header.Length >= 12
            && header[4] == 'f' && header[5] == 't' && header[6] == 'y' && header[7] == 'p')
        {
            var brand = System.Text.Encoding.ASCII.GetString(header[8..12]);

            if (brand is "heic" or "heix" or "hevc" or "heim" or "heis" or "mif1" or "msf1")
            {
                return ImageSignatureKind.Heic;
            }
        }

        return ImageSignatureKind.Unknown;
    }
}
