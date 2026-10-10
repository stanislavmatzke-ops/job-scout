namespace JobScout.Core.Domain;

/// <summary>
/// The user's current CV file. Kept so the profile can be re-analysed (new notes, new prompt version)
/// without asking for the file again. Personal data – deleted together with the user.
/// </summary>
public sealed class StoredCv
{
    public const int MaxBytes = 5 * 1024 * 1024;

    public Guid UserId { get; private set; }
    public string FileName { get; private set; } = "";
    public string MediaType { get; private set; } = "";
    public byte[] Content { get; private set; } = [];
    public DateTimeOffset UploadedAt { get; private set; }

    private StoredCv() { }

    public StoredCv(Guid userId, string fileName, string mediaType, byte[] content, DateTimeOffset now)
    {
        UserId = userId;
        Replace(fileName, mediaType, content, now);
    }

    public void Replace(string fileName, string mediaType, byte[] content, DateTimeOffset now)
    {
        if (content.Length == 0)
            throw new ArgumentException("CV je prázdné.", nameof(content));
        if (content.Length > MaxBytes)
            throw new ArgumentException($"CV je větší než {MaxBytes / 1024 / 1024} MB.", nameof(content));

        FileName = fileName;
        MediaType = mediaType;
        Content = content;
        UploadedAt = now;
    }
}
