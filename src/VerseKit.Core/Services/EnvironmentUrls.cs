namespace VerseKit.Core.Services;

/// <summary>Canonical form for Dataverse environment URLs.</summary>
public static class EnvironmentUrls
{
    /// <summary>
    /// Adds a missing <c>https://</c>, trims whitespace, any query/fragment and
    /// trailing slashes. MSAL needs the scheme: the token scope is
    /// <c>{url}/.default</c>, and "org.crm4.dynamics.com/.default" is rejected
    /// (AADSTS70011). Online environments are always the bare host, so a pasted
    /// browser link (".../main.aspx?appid=…") is cut back to it; other hosts
    /// keep their path (on-premises URLs can include the org name).
    /// Input that still isn't a valid absolute URL is returned trimmed.
    /// </summary>
    public static string Normalize(string url)
    {
        var trimmed = url.Trim();
        if (trimmed.Length == 0) return trimmed;

        var withScheme = trimmed.Contains("://", StringComparison.Ordinal) ? trimmed : "https://" + trimmed;
        if (!Uri.TryCreate(withScheme, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
            return trimmed.TrimEnd('/');

        var path = IsOnlineHost(uri.Host) ? "" : uri.AbsolutePath;
        return $"{uri.Scheme}://{uri.Authority}{path}".TrimEnd('/');
    }

    // Dataverse online, including the sovereign clouds.
    private static readonly string[] OnlineSuffixes =
        [".dynamics.com", ".dynamics.cn", ".microsoftdynamics.us", ".appsplatform.us", ".microsoftdynamics.de"];

    private static bool IsOnlineHost(string host) =>
        OnlineSuffixes.Any(s => host.EndsWith(s, StringComparison.OrdinalIgnoreCase));
}
