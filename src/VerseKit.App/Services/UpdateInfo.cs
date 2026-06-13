namespace VerseKit.App.Services;

public sealed record UpdateInfo(
    string Version,
    string ReleasePageUrl,
    string? DownloadUrl,
    string ReleaseNotes,
    string? ChecksumUrl = null
);
