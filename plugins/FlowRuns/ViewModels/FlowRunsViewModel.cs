using System.Collections.ObjectModel;
using System.Text;
using Avalonia.Threading;
using ClosedXML.Excel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using FlowRuns.Models;
using VerseKit.PluginSdk;

namespace FlowRuns.ViewModels;

public sealed partial class FlowRunsViewModel : ObservableObject
{
    // Cap the result set: the flowrun table can be huge, and this is a beta
    // read-only viewer, not a reporting warehouse.
    private const int MaxRows = 5000;
    private const int PageSize = 1000;

    private readonly IConnectionProvider _connectionProvider;
    private List<FlowRunItem> _allRuns = []; // fetched set (date + flow), before status filter

    public ObservableCollection<FlowRunItem> Runs { get; } = [];
    public ObservableCollection<FlowOption> Flows { get; } = [];
    public ObservableCollection<string> StatusFilters { get; } =
        ["All statuses", "Succeeded", "Failed", "Cancelled", "Running"];
    public ObservableCollection<DateRangeOption> DateRanges { get; } =
    [
        new() { Label = "Last 24 hours", Days = 1 },
        new() { Label = "Last 7 days", Days = 7 },
        new() { Label = "Last 14 days", Days = 14 },
        new() { Label = "Last 28 days", Days = 28 },
        new() { Label = "Last 60 days", Days = 60 },
    ];

    /// <summary>Set by the view to show a native save dialog (name, extension) → chosen path.</summary>
    public Func<string, string, Task<string?>>? PickSavePathAsync { get; set; }

    [ObservableProperty] private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowConnectPrompt))]
    [NotifyPropertyChangedFor(nameof(ShowContent))]
    private bool _isConnected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNotEnabled))]
    [NotifyPropertyChangedFor(nameof(ShowContent))]
    private bool _isFeatureEnabled = true;

    [ObservableProperty] private string _statusMessage = "Connect to an environment to view flow runs.";
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string _warningMessage = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResults))]
    private int _runCount;

    [ObservableProperty] private FlowOption? _selectedFlow;
    [ObservableProperty] private string _selectedStatus = "All statuses";
    [ObservableProperty] private DateRangeOption? _selectedDateRange;
    [ObservableProperty] private bool _selectAll;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailOpen))]
    private FlowRunItem? _selectedRun;

    public bool IsDetailOpen => SelectedRun is not null;
    public bool HasResults => RunCount > 0;
    public bool ShowConnectPrompt => !IsConnected;
    public bool ShowNotEnabled => IsConnected && !IsFeatureEnabled;
    public bool ShowContent => IsConnected && IsFeatureEnabled;

    public FlowRunsViewModel(IConnectionProvider connectionProvider)
    {
        _connectionProvider = connectionProvider;
        _selectedDateRange = DateRanges[1]; // Last 7 days
        _connectionProvider.ConnectionChanged.Subscribe(client =>
            Dispatcher.UIThread.Post(() =>
            {
                if (client is { IsReady: true })
                {
                    IsConnected = true;
                    _ = InitializeAsync(CancellationToken.None);
                }
                else
                {
                    IsConnected = false;
                    IsFeatureEnabled = true;
                    _allRuns = [];
                    Runs.Clear();
                    Flows.Clear();
                    SelectedRun = null;
                    SelectAll = false;
                    RunCount = 0;
                    Summary = "";
                    WarningMessage = "";
                    StatusMessage = "Connect to an environment to view flow runs.";
                }
            }));
    }

    private async Task InitializeAsync(CancellationToken ct)
    {
        IsLoading = true;
        StatusMessage = "Checking flow run history…";
        try
        {
            var client = await _connectionProvider.GetActiveConnectionAsync(ct);

            // Gate: cloud-flow-run-history-in-Dataverse must be on (TTL > 0).
            // A positively-read TTL of 0 means ingestion is off — show guidance
            // rather than an empty grid. If we can't read it, proceed anyway.
            var enabled = await IsRunHistoryEnabledAsync(client, ct);
            if (enabled == false)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    IsFeatureEnabled = false;
                    StatusMessage = "Cloud flow run history isn't enabled for this environment.";
                });
                return;
            }

            await LoadFlowsAsync(client, ct);
            await ReloadRunsAsync(ct);
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() => StatusMessage = $"Error: {ex.Message}");
        }
        finally
        {
            Dispatcher.UIThread.Post(() => IsLoading = false);
        }
    }

    /// <summary>Returns true/false if the org TTL is readable, null if it can't be determined.</summary>
    private static async Task<bool?> IsRunHistoryEnabledAsync(ServiceClient client, CancellationToken ct)
    {
        try
        {
            var org = (await client.RetrieveMultipleAsync(
                new QueryExpression("organization")
                {
                    ColumnSet = new ColumnSet("flowruntimetoliveinseconds"),
                    TopCount = 1
                }, ct)).Entities.FirstOrDefault();
            var ttl = org?.GetAttributeValue<int?>("flowruntimetoliveinseconds");
            if (ttl is null) return null;
            return ttl > 0;
        }
        catch
        {
            return null; // attribute/feature may not exist on this version
        }
    }

    private async Task LoadFlowsAsync(ServiceClient client, CancellationToken ct)
    {
        // Modern cloud flows: workflow category = 5, type = 1 (definition).
        var q = new QueryExpression("workflow")
        {
            ColumnSet = new ColumnSet("workflowid", "name")
        };
        q.Criteria.AddCondition("category", ConditionOperator.Equal, 5);
        q.Criteria.AddCondition("type", ConditionOperator.Equal, 1);
        q.AddOrder("name", OrderType.Ascending);

        var flows = new List<FlowOption>();
        try
        {
            foreach (var e in (await client.RetrieveMultipleAsync(q, ct)).Entities)
            {
                flows.Add(new FlowOption
                {
                    Id = e.Id,
                    Name = e.GetAttributeValue<string>("name") is { Length: > 0 } n ? n : "(unnamed flow)"
                });
            }
        }
        catch { /* non-fatal: the flow picker just stays at "All flows" */ }

        Dispatcher.UIThread.Post(() =>
        {
            Flows.Clear();
            Flows.Add(new FlowOption { Id = null, Name = "All flows" });
            foreach (var f in flows) Flows.Add(f);
            SelectedFlow = Flows[0];
        });
    }

    [RelayCommand]
    private async Task RefreshAsync() => await ReloadRunsAsync(CancellationToken.None);

    private async Task ReloadRunsAsync(CancellationToken ct)
    {
        IsLoading = true;
        StatusMessage = "Loading flow runs…";
        try
        {
            var client = await _connectionProvider.GetActiveConnectionAsync(ct);
            var days = SelectedDateRange?.Days ?? 7;
            var cutoff = DateTime.UtcNow.AddDays(-days);
            var flowId = SelectedFlow?.Id;

            // Build a workflowid → name map for runs whose lookup isn't pre-resolved.
            var flowNames = Flows.Where(f => f.Id.HasValue)
                                 .ToDictionary(f => f.Id!.Value, f => f.Name);

            var items = await Task.Run(() => FetchRuns(client, cutoff, flowId, flowNames, ct), ct);

            Dispatcher.UIThread.Post(() =>
            {
                _allRuns = items;
                ApplyStatusFilter();
                if (items.Count >= MaxRows)
                    WarningMessage = $"Showing the most recent {MaxRows:N0} runs. Narrow the date range or pick a flow to see more.";
                else
                    WarningMessage = "";
            });
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                _allRuns = [];
                ApplyStatusFilter();
                StatusMessage = $"Error loading runs: {ex.Message}";
            });
        }
        finally
        {
            Dispatcher.UIThread.Post(() => IsLoading = false);
        }
    }

    private static List<FlowRunItem> FetchRuns(
        ServiceClient client, DateTime cutoffUtc, Guid? flowId,
        Dictionary<Guid, string> flowNames, CancellationToken ct)
    {
        var qe = new QueryExpression("flowrun")
        {
            ColumnSet = new ColumnSet(
                "name", "status", "starttime", "endtime", "duration",
                "errorcode", "errormessage", "triggertype", "workflow",
                "ownerid", "parentrunid"),
            PageInfo = new PagingInfo { Count = PageSize, PageNumber = 1, PagingCookie = null }
        };
        qe.Criteria.AddCondition("starttime", ConditionOperator.GreaterEqual, cutoffUtc);
        if (flowId is { } id) qe.Criteria.AddCondition("workflow", ConditionOperator.Equal, id);
        qe.AddOrder("starttime", OrderType.Descending);

        var raw = new List<Entity>();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            EntityCollection page;
            try
            {
                page = client.RetrieveMultiple(qe);
            }
            catch when (qe.Orders.Count > 0)
            {
                // Elastic tables can reject server-side ordering — retry unordered
                // and sort client-side below.
                qe.Orders.Clear();
                qe.PageInfo = new PagingInfo { Count = PageSize, PageNumber = 1, PagingCookie = null };
                raw.Clear();
                continue;
            }

            raw.AddRange(page.Entities);
            if (!page.MoreRecords || raw.Count >= MaxRows) break;
            qe.PageInfo.PageNumber++;
            qe.PageInfo.PagingCookie = page.PagingCookie;
        }

        var items = raw.Take(MaxRows).Select(e =>
        {
            var wf = e.GetAttributeValue<EntityReference>("workflow");
            var name = wf?.Name;
            if (string.IsNullOrEmpty(name) && wf is not null && flowNames.TryGetValue(wf.Id, out var n))
                name = n;

            return new FlowRunItem
            {
                FlowName = string.IsNullOrEmpty(name) ? "(unknown flow)" : name!,
                Status = e.GetAttributeValue<string>("status") ?? "",
                Owner = e.GetAttributeValue<EntityReference>("ownerid")?.Name ?? "",
                RunId = e.GetAttributeValue<string>("name") ?? "",
                ParentRunId = e.GetAttributeValue<string>("parentrunid") ?? "",
                Start = e.GetAttributeValue<DateTime?>("starttime"),
                End = e.GetAttributeValue<DateTime?>("endtime"),
                DurationMs = e.Contains("duration") ? e.GetAttributeValue<long>("duration") : null,
                TriggerType = e.GetAttributeValue<string>("triggertype") ?? "",
                ErrorCode = e.GetAttributeValue<string>("errorcode") ?? "",
                ErrorMessage = e.GetAttributeValue<string>("errormessage") ?? ""
            };
        }).ToList();

        // Ensure newest-first even if we fell back to an unordered query.
        items.Sort((a, b) => Nullable.Compare(b.Start, a.Start));
        return items;
    }

    partial void OnSelectedFlowChanged(FlowOption? value)
    {
        if (IsConnected && IsFeatureEnabled) _ = ReloadRunsAsync(CancellationToken.None);
    }

    partial void OnSelectedDateRangeChanged(DateRangeOption? value)
    {
        if (IsConnected && IsFeatureEnabled) _ = ReloadRunsAsync(CancellationToken.None);
    }

    partial void OnSelectedStatusChanged(string value) => ApplyStatusFilter();

    partial void OnSelectAllChanged(bool value)
    {
        foreach (var r in Runs) r.IsSelected = value;
    }

    [RelayCommand]
    private void Inspect(FlowRunItem? run)
    {
        if (run is not null) SelectedRun = run;
    }

    [RelayCommand]
    private void CloseDetail() => SelectedRun = null;

    private void ApplyStatusFilter()
    {
        SelectedRun = null;
        var status = SelectedStatus;
        var filtered = status == "All statuses"
            ? _allRuns
            : _allRuns.Where(r => r.StatusKind == status).ToList();

        Runs.Clear();
        foreach (var r in filtered) Runs.Add(r);
        RunCount = filtered.Count;
        SelectAll = false;

        var ok = _allRuns.Count(r => r.StatusKind == "Succeeded");
        var fail = _allRuns.Count(r => r.StatusKind == "Failed");
        var cancel = _allRuns.Count(r => r.StatusKind == "Cancelled");
        var run = _allRuns.Count(r => r.StatusKind == "Running");
        Summary = _allRuns.Count == 0
            ? "No runs in this window."
            : $"{ok:N0} succeeded · {fail:N0} failed · {cancel:N0} cancelled" + (run > 0 ? $" · {run:N0} running" : "");
        StatusMessage = $"{filtered.Count:N0} of {_allRuns.Count:N0} run(s) shown.";
    }

    // ── Export ─────────────────────────────────────────────────────────

    private static readonly string[] Headers =
        ["Flow", "Status", "Run by", "Start", "End", "Duration", "Trigger", "Error code", "Error message", "Run ID"];

    private static string[] Row(FlowRunItem r) =>
    [
        r.FlowName, r.StatusKind, r.Owner, r.StartDisplay, r.EndDisplay,
        r.DurationDisplay, r.TriggerType, r.ErrorCode, r.ErrorMessage, r.RunId
    ];

    /// <summary>Ticked rows if any are selected; otherwise every shown run.</summary>
    private List<FlowRunItem> RowsToExport()
    {
        var selected = Runs.Where(r => r.IsSelected).ToList();
        return selected.Count > 0 ? selected : Runs.ToList();
    }

    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        if (!HasResults || PickSavePathAsync is null) return;
        var path = await PickSavePathAsync("flow-runs.csv", "csv");
        if (string.IsNullOrEmpty(path)) return;

        var rows = RowsToExport();
        try
        {
            await Task.Run(() =>
            {
                var sb = new StringBuilder();
                sb.AppendLine(string.Join(",", Headers.Select(CsvEscape)));
                foreach (var r in rows)
                    sb.AppendLine(string.Join(",", Row(r).Select(CsvEscape)));
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
            });
            StatusMessage = $"Exported {rows.Count:N0} run(s) to {Path.GetFileName(path)}";
        }
        catch (Exception ex) { StatusMessage = $"Export failed: {ex.Message}"; }
    }

    [RelayCommand]
    private async Task ExportExcelAsync()
    {
        if (!HasResults || PickSavePathAsync is null) return;
        var path = await PickSavePathAsync("flow-runs.xlsx", "xlsx");
        if (string.IsNullOrEmpty(path)) return;

        var rows = RowsToExport();
        try
        {
            await Task.Run(() =>
            {
                using var wb = new XLWorkbook();
                var ws = wb.AddWorksheet("Flow Runs");
                for (var c = 0; c < Headers.Length; c++)
                    ws.Cell(1, c + 1).Value = Headers[c];
                ws.Row(1).Style.Font.Bold = true;
                for (var i = 0; i < rows.Count; i++)
                {
                    var cells = Row(rows[i]);
                    for (var c = 0; c < cells.Length; c++)
                        ws.Cell(i + 2, c + 1).Value = cells[c];
                }
                ws.Columns().AdjustToContents();
                wb.SaveAs(path);
            });
            StatusMessage = $"Exported {rows.Count:N0} run(s) to {Path.GetFileName(path)}";
        }
        catch (Exception ex) { StatusMessage = $"Export failed: {ex.Message}"; }
    }

    private static string CsvEscape(string s)
    {
        if (s.Contains('"') || s.Contains(',') || s.Contains('\n') || s.Contains('\r'))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }
}
