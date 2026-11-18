namespace LingoCue.Core.Formats;

public interface ISubtitleFormat
{
    /// <summary>Display name, e.g. "SubRip".</summary>
    string Name { get; }

    /// <summary>Lowercase extensions with leading dot; the first is the default.</summary>
    IReadOnlyList<string> Extensions { get; }

    /// <summary>Cheap content sniffing used when the extension is missing or wrong.</summary>
    bool LooksLike(string content);

    /// <summary>Parses decoded text. Never throws for malformed blocks; they are skipped and reported in <see cref="SubtitleDocument.Warnings"/>.</summary>
    SubtitleDocument Parse(string content);

    /// <summary>Serializes with <c>\n</c> line endings; the file writer applies the requested newline and encoding.</summary>
    string Write(SubtitleDocument document);
}
