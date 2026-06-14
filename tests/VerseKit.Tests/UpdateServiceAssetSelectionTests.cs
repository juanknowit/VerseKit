using System.Runtime.InteropServices;
using FluentAssertions;
using VerseKit.App.Services;
using Xunit;

namespace VerseKit.Tests;

public class UpdateServiceAssetSelectionTests
{
    private static List<(string Name, string Url)> TwoArchRelease() =>
    [
        ("VerseKit-0.4.1-osx-arm64.zip",        "https://x/arm64.zip"),
        ("VerseKit-0.4.1-osx-arm64.zip.sha256", "https://x/arm64.zip.sha256"),
        ("VerseKit-0.4.1-osx-x64.zip",          "https://x/x64.zip"),
        ("VerseKit-0.4.1-osx-x64.zip.sha256",   "https://x/x64.zip.sha256"),
    ];

    [Fact]
    public void Arm64_machine_gets_the_arm64_zip_and_its_checksum()
    {
        var (url, name, checksum) = UpdateService.SelectAsset(TwoArchRelease(), Architecture.Arm64);

        name.Should().Be("VerseKit-0.4.1-osx-arm64.zip");
        url.Should().Be("https://x/arm64.zip");
        checksum.Should().Be("https://x/arm64.zip.sha256");
    }

    [Fact]
    public void X64_machine_gets_the_x64_zip_not_the_first_listed_arm64()
    {
        // This is the regression guard: assets list arm64 first, but an Intel
        // machine must still receive the x64 build (the old code took the first).
        var (url, name, checksum) = UpdateService.SelectAsset(TwoArchRelease(), Architecture.X64);

        name.Should().Be("VerseKit-0.4.1-osx-x64.zip");
        url.Should().Be("https://x/x64.zip");
        checksum.Should().Be("https://x/x64.zip.sha256");
    }

    [Fact]
    public void Falls_back_to_the_only_mac_zip_when_no_arch_specific_asset()
    {
        var assets = new List<(string, string)>
        {
            ("VerseKit-0.3.0-osx.zip", "https://x/legacy.zip"),
        };

        var (url, name, _) = UpdateService.SelectAsset(assets, Architecture.X64);

        name.Should().Be("VerseKit-0.3.0-osx.zip");
        url.Should().Be("https://x/legacy.zip");
    }

    [Fact]
    public void Returns_nulls_and_no_checksum_when_no_matching_zip()
    {
        var assets = new List<(string, string)>
        {
            ("SomethingElse-linux.tar.gz", "https://x/other"),
            ("notes.txt",                  "https://x/notes"),
        };

        UpdateService.SelectAsset(assets, Architecture.Arm64)
            .Should().Be((null, null, null));
    }

    [Fact]
    public void Ignores_zips_that_are_not_versekit_mac_builds()
    {
        var assets = new List<(string, string)>
        {
            ("OtherApp-osx-arm64.zip", "https://x/other.zip"),
            ("VerseKit-0.4.1-win.zip", "https://x/win.zip"),
        };

        UpdateService.SelectAsset(assets, Architecture.Arm64)
            .Url.Should().BeNull();
    }
}
