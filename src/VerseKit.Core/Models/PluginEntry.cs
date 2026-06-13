using VerseKit.PluginSdk;

namespace VerseKit.Core.Models;

public sealed class PluginEntry
{
    public required IVerseKitPlugin Plugin { get; init; }
    public required string AssemblyPath { get; init; }
    public bool IsActivated { get; set; }
}
