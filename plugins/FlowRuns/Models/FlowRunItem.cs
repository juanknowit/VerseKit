using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FlowRuns.Models;

/// <summary>One cloud flow run from the <c>flowrun</c> table.</summary>
public sealed partial class FlowRunItem : ObservableObject
{
    /// <summary>Ticked in the grid to include this run in an export.</summary>
    [ObservableProperty] private bool _isSelected;

    public required string FlowName { get; init; }
    public required string Status { get; init; }
    public string Owner { get; init; } = "";
    public string RunId { get; init; } = "";
    public string ParentRunId { get; init; } = "";
    public DateTime? Start { get; init; }
    public DateTime? End { get; init; }
    public long? DurationMs { get; init; }
    public string TriggerType { get; init; } = "";
    public string ErrorCode { get; init; } = "";
    public string ErrorMessage { get; init; } = "";

    public string StartDisplay => Start?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "";
    public string EndDisplay => End?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "";

    public bool HasParent => !string.IsNullOrEmpty(ParentRunId);
    public bool HasError => !string.IsNullOrEmpty(ErrorCode) || !string.IsNullOrEmpty(ErrorMessage);

    public string DurationDisplay
    {
        get
        {
            var ms = DurationMs ?? (End.HasValue && Start.HasValue
                ? (long)(End.Value - Start.Value).TotalMilliseconds
                : (long?)null);
            if (ms is not { } m || m < 0) return "";
            var t = TimeSpan.FromMilliseconds(m);
            if (t.TotalSeconds < 1) return $"{m} ms";
            if (t.TotalMinutes < 1) return $"{t.TotalSeconds:0.0} s";
            if (t.TotalHours < 1) return $"{(int)t.TotalMinutes}m {t.Seconds}s";
            return $"{(int)t.TotalHours}h {t.Minutes}m";
        }
    }

    /// <summary>Normalised status bucket: Succeeded / Failed / Cancelled / Running / the raw value.</summary>
    public string StatusKind
    {
        get
        {
            var s = Status?.Trim() ?? "";
            if (s.StartsWith("Succe", StringComparison.OrdinalIgnoreCase)) return "Succeeded";
            if (s.StartsWith("Fail", StringComparison.OrdinalIgnoreCase)) return "Failed";
            if (s.StartsWith("Cancel", StringComparison.OrdinalIgnoreCase)) return "Cancelled";
            if (s.StartsWith("Run", StringComparison.OrdinalIgnoreCase)) return "Running";
            return string.IsNullOrEmpty(s) ? "Unknown" : s;
        }
    }

    public IBrush StatusBrush => StatusKind switch
    {
        "Succeeded" => new SolidColorBrush(Color.Parse("#34C759")),
        "Failed" => new SolidColorBrush(Color.Parse("#FF3B30")),
        "Cancelled" => new SolidColorBrush(Color.Parse("#FF9500")),
        "Running" => new SolidColorBrush(Color.Parse("#007AFF")),
        _ => new SolidColorBrush(Color.Parse("#8E8E93")),
    };
}
