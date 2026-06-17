using FluentAssertions;
using VerseKit.Core.Services;
using Xunit;

namespace VerseKit.Tests;

public class TenantDiscoveryTests
{
    // The real Dataverse challenge — note the comma between authorization_uri and
    // resource_id, which is what defeats .NET's typed WWW-Authenticate parser. We
    // therefore parse the raw header string, so this exact shape must resolve.
    [Fact]
    public void ExtractTenantId_FindsTenant_InFullDataverseChallenge()
    {
        var challenge =
            "Bearer authorization_uri=https://login.microsoftonline.com/1d83f245-fbc2-4d0d-84ba-a52200c578b3/oauth2/authorize, resource_id=https://operations-clamponuat.crm4.dynamics.com/";

        DataverseClientFactory.ExtractTenantId([challenge])
            .Should().Be("1d83f245-fbc2-4d0d-84ba-a52200c578b3");
    }

    [Fact]
    public void ExtractTenantId_FindsTenant_AcrossSovereignClouds()
    {
        var challenge =
            "Bearer authorization_uri=https://login.microsoftonline.us/be1d8284-bcda-4ffc-803b-1be8ecbd2f92/oauth2/authorize, resource_id=https://org.crm9.dynamics.com/";

        DataverseClientFactory.ExtractTenantId([challenge])
            .Should().Be("be1d8284-bcda-4ffc-803b-1be8ecbd2f92");
    }

    [Fact]
    public void ExtractTenantId_ReturnsNull_WhenNoTenantPresent()
    {
        DataverseClientFactory.ExtractTenantId(["Bearer realm=\"\", error=\"invalid_token\""])
            .Should().BeNull();
        DataverseClientFactory.ExtractTenantId([]).Should().BeNull();
    }
}
