namespace VerseKit.Core.Models;

public enum AuthMethod
{
    Interactive,
    ClientSecret,
    Certificate,
    DeviceCode
}

public sealed class ConnectionProfile
{
    public required string Name { get; init; }
    public required string EnvironmentUrl { get; init; }
    public required AuthMethod AuthMethod { get; init; }
    public required string ClientId { get; init; }
    public string? TenantId { get; init; }
    public string? RedirectUri { get; init; } = "http://localhost";

    /// <summary>Optional folder/group this connection belongs to in the
    /// sidebar (e.g. a client name). Null/empty = ungrouped (top level).</summary>
    public string? Folder { get; init; }

    /// <summary>Path to a PKCS#12 (.pfx/.p12) file for Certificate auth.
    /// The file path is not sensitive; the cert's password (if any) is the
    /// secret and is stored in the Keychain like a client secret.</summary>
    public string? CertificatePath { get; init; }
    // Secrets (client secret, certificate password) are stored in Keychain
    // and never persisted on this object.
}
