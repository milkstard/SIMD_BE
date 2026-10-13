namespace IncidentHub.Domain.Incidents;

/// <summary>Which uploads are accepted and how a client-supplied file name is made safe to store and echo back.</summary>
public static class AttachmentRules
{
    /// <summary>Longest header the signature checks need (<c>RIFF....WEBP</c>).</summary>
    public const int SignatureLength = 12;

    private static readonly Dictionary<string, string[]> ContentTypesByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = ["image/png"],
        [".jpg"] = ["image/jpeg"],
        [".jpeg"] = ["image/jpeg"],
        [".gif"] = ["image/gif"],
        [".webp"] = ["image/webp"],
        [".txt"] = ["text/plain"],
        [".log"] = ["text/plain"],
        [".json"] = ["application/json"],
    };

    /// <summary>True when the extension is allowed and the content type is the one that extension implies. SVG is never allowed.</summary>
    public static bool IsAllowed(string? fileName, string? contentType)
    {
        if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(contentType))
        {
            return false;
        }

        var extension = Path.GetExtension(SanitizeFileName(fileName));
        return ContentTypesByExtension.TryGetValue(extension, out var allowed)
            && allowed.Contains(NormalizeContentType(contentType), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The media type without parameters, e.g. <c>text/plain; charset=utf-8</c> becomes <c>text/plain</c>.</summary>
    public static string NormalizeContentType(string contentType)
    {
        var separator = contentType.IndexOf(';');
        return (separator < 0 ? contentType : contentType[..separator]).Trim().ToLowerInvariant();
    }

    /// <summary>True for text and JSON; for images the leading bytes must carry the magic number of the declared type.</summary>
    public static bool MatchesSignature(string contentType, ReadOnlySpan<byte> header) =>
        NormalizeContentType(contentType) switch
        {
            "image/png" => header.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
            "image/jpeg" => header.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }),
            "image/gif" => header.StartsWith("GIF87a"u8) || header.StartsWith("GIF89a"u8),
            "image/webp" => header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header.Slice(8, 4).SequenceEqual("WEBP"u8),
            _ => true,
        };

    /// <summary>
    /// Control characters plus invisible format characters (bidi overrides such as U+202E, zero-width) and line/paragraph
    /// separators, which could make a name read differently from what it is (<c>gnp.exe</c> shown as <c>exe.png</c>).
    /// </summary>
    private static bool IsHiddenOrControl(char c) =>
        char.IsControl(c) || char.GetUnicodeCategory(c) is System.Globalization.UnicodeCategory.Format
            or System.Globalization.UnicodeCategory.LineSeparator or System.Globalization.UnicodeCategory.ParagraphSeparator;

    public static bool IsImage(string contentType) => NormalizeContentType(contentType).StartsWith("image/", StringComparison.Ordinal);

    /// <summary>
    /// Drops any directory part (either separator), control characters and quotes, trims, and caps the length at
    /// <see cref="Attachment.FileNameMaxLength"/> keeping the extension. Falls back to <c>file</c> when nothing is left.
    /// </summary>
    public static string SanitizeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return "file";
        }

        var name = fileName.Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        name = new string(name.Where(c => !IsHiddenOrControl(c) && c != '"').ToArray()).Trim().Trim('.');

        if (name.Length == 0)
        {
            return "file";
        }

        if (name.Length > Attachment.FileNameMaxLength)
        {
            var extension = Path.GetExtension(name);
            if (extension.Length > 20)
            {
                extension = string.Empty;
            }

            name = name[..(Attachment.FileNameMaxLength - extension.Length)] + extension;
        }

        return name;
    }
}
