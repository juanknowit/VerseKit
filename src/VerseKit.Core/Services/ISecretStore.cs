namespace VerseKit.Core.Services;

/// <summary>
/// Abstraction over secure secret storage.
/// Production: KeychainService (macOS Keychain).
/// Development: FileSecretStore (plain JSON, never use in prod).
/// </summary>
public interface ISecretStore
{
    Task WriteAsync(string account, string secret, CancellationToken ct = default);
    Task<string?> ReadAsync(string account, CancellationToken ct = default);
    Task DeleteAsync(string account, CancellationToken ct = default);
}
