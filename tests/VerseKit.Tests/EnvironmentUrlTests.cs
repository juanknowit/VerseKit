using FluentAssertions;
using VerseKit.Core.Services;
using Xunit;

namespace VerseKit.Tests;

public class EnvironmentUrlTests
{
    [Theory]
    // The AADSTS70011 case: no scheme → "host/.default" is an invalid MSAL scope.
    [InlineData("operations-clamponuat.crm4.dynamics.com", "https://operations-clamponuat.crm4.dynamics.com")]
    [InlineData("  contoso.crm.dynamics.com/  ", "https://contoso.crm.dynamics.com")]
    [InlineData("https://contoso.crm.dynamics.com/", "https://contoso.crm.dynamics.com")]
    [InlineData("https://Contoso.CRM.dynamics.com", "https://contoso.crm.dynamics.com")]
    // A pasted browser link to an online environment → just the environment.
    [InlineData("https://contoso.crm.dynamics.com/main.aspx?appid=1#x", "https://contoso.crm.dynamics.com")]
    [InlineData("contoso.crm.microsoftdynamics.us/main.aspx", "https://contoso.crm.microsoftdynamics.us")]
    [InlineData("https://crm.contoso.local:8443/ContosoOrg/", "https://crm.contoso.local:8443/ContosoOrg")]
    public void Normalize_produces_a_scheme_qualified_url(string input, string expected) =>
        EnvironmentUrls.Normalize(input).Should().Be(expected);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_leaves_empty_input_empty(string input) =>
        EnvironmentUrls.Normalize(input).Should().BeEmpty();
}
