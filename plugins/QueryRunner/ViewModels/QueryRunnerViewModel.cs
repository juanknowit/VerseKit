using System.Collections.ObjectModel;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Avalonia.Threading;
using ClosedXML.Excel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using QueryRunner.Models;
using VerseKit.PluginSdk;

namespace QueryRunner.ViewModels;

public sealed partial class QueryRunnerViewModel : ObservableObject
{
    private const string FetchSample =
        "<fetch top=\"50\">\n  <entity name=\"account\">\n    <attribute name=\"name\" />\n    <attribute name=\"telephone1\" />\n    <order attribute=\"name\" />\n  </entity>\n</fetch>";
    private const string ODataSample = "accounts?$select=name,telephone1&$top=50";

    private readonly IConnectionProvider _connectionProvider;

    public ObservableCollection<ResultRow> Rows { get; } = [];
    public IReadOnlyList<string> ResultColumns { get; private set; } = [];

    /// <summary>Raised after a query completes so the view can rebuild grid columns.</summary>
    public event Action? ResultsReady;

    public string[] Modes { get; } = ["FetchXML", "OData"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOData))]
    private int _modeIndex;

    public bool IsOData => ModeIndex == 1;

    [ObservableProperty] private string _queryText = FetchSample;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _status = "Connect to an environment, then run a query.";
    [ObservableProperty] private bool _hasResults;

    /// <summary>Set by the view — save dialog. Args: suggested name, extension.</summary>
    public Func<string, string, Task<string?>>? PickSavePathAsync { get; set; }

    public QueryRunnerViewModel(IConnectionProvider connectionProvider)
    {
        _connectionProvider = connectionProvider;
        _connectionProvider.ConnectionChanged.Subscribe(client =>
            Dispatcher.UIThread.Post(() =>
            {
                if (client is not { IsReady: true })
                {
                    Rows.Clear();
                    ResultColumns = [];
                    HasResults = false;
                    ResultsReady?.Invoke();
                    Status = "Connect to an environment, then run a query.";
                }
            }));
    }

    partial void OnModeIndexChanged(int value)
    {
        // Swap the sample only if the user hasn't typed their own query yet.
        if (QueryText is FetchSample or ODataSample or "")
            QueryText = value == 1 ? ODataSample : FetchSample;
    }

    [RelayCommand]
    private async Task RunAsync()
    {
        if (IsRunning) return;
        var query = QueryText?.Trim() ?? "";
        if (query.Length == 0) { Status = "Enter a query first."; return; }

        IsRunning = true;
        Status = "Running…";
        try
        {
            var client = await _connectionProvider.GetActiveConnectionAsync(CancellationToken.None);

            (List<string> cols, List<ResultRow> rows, bool more) result = IsOData
                ? await RunODataAsync(client, query, CancellationToken.None)
                : await RunFetchAsync(client, query, CancellationToken.None);

            Dispatcher.UIThread.Post(() =>
            {
                ResultColumns = result.cols;
                Rows.Clear();
                foreach (var r in result.rows) Rows.Add(r);
                HasResults = ResultColumns.Count > 0;
                ResultsReady?.Invoke();
                Status = $"{result.rows.Count} row(s)" +
                         (result.more ? " (more available — raise the top/limit)" : "") +
                         $" · {ResultColumns.Count} column(s)";
            });
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                Rows.Clear();
                ResultColumns = [];
                HasResults = false;
                ResultsReady?.Invoke();
                Status = $"Query failed: {ex.Message}";
            });
        }
        finally
        {
            Dispatcher.UIThread.Post(() => IsRunning = false);
        }
    }

    // ── FetchXML ───────────────────────────────────────────────────────

    private static async Task<(List<string>, List<ResultRow>, bool)> RunFetchAsync(
        ServiceClient client, string fetchXml, CancellationToken ct)
    {
        var ec = await client.RetrieveMultipleAsync(new FetchExpression(fetchXml), ct);

        var cols = new List<string>();
        var seen = new HashSet<string>();
        foreach (var e in ec.Entities)
            foreach (var key in e.Attributes.Keys)
                if (seen.Add(key)) cols.Add(key);

        var rows = ec.Entities
            .Select(e => new ResultRow { Cells = cols.Select(c => FormatAttribute(e, c)).ToArray() })
            .ToList();

        return (cols, rows, ec.MoreRecords);
    }

    private static string FormatAttribute(Entity e, string attr)
    {
        if (e.FormattedValues.Contains(attr)) return e.FormattedValues[attr];
        return e.Contains(attr) ? FormatValue(e[attr]) : "";
    }

    private static string FormatValue(object? v) => v switch
    {
        null => "",
        AliasedValue av => FormatValue(av.Value),
        EntityReference er => er.Name ?? er.Id.ToString(),
        OptionSetValue os => os.Value.ToString(),
        Money m => m.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        DateTime dt => dt.ToString("yyyy-MM-dd HH:mm"),
        bool b => b ? "true" : "false",
        _ => v.ToString() ?? ""
    };

    // ── OData (Web API) ────────────────────────────────────────────────

    private static async Task<(List<string>, List<ResultRow>, bool)> RunODataAsync(
        ServiceClient client, string query, CancellationToken ct)
    {
        var authority = client.ConnectedOrgUriActual.GetLeftPart(UriPartial.Authority);
        var url = $"{authority}/api/data/v9.2/{query.TrimStart('/')}";

        using var http = new HttpClient();
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", client.CurrentAccessToken);
        req.Headers.Accept.ParseAdd("application/json");
        req.Headers.Add("OData-MaxVersion", "4.0");
        req.Headers.Add("OData-Version", "4.0");
        req.Headers.Add("Prefer", "odata.include-annotations=\"OData.Community.Display.V1.FormattedValue\"");

        using var resp = await http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"{(int)resp.StatusCode}: {ExtractODataError(body)}");

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var records = new List<JsonElement>();
        var more = false;
        if (root.TryGetProperty("value", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            records.AddRange(arr.EnumerateArray());
            more = root.TryGetProperty("@odata.nextLink", out _);
        }
        else
        {
            records.Add(root); // single-entity response
        }

        // Columns: union of non-annotation property names, in first-seen order.
        var cols = new List<string>();
        var seen = new HashSet<string>();
        foreach (var rec in records)
            foreach (var prop in rec.EnumerateObject())
                if (!prop.Name.Contains('@') && seen.Add(prop.Name))
                    cols.Add(prop.Name);

        var rows = records.Select(rec => new ResultRow
        {
            Cells = cols.Select(c => FormatJson(rec, c)).ToArray()
        }).ToList();

        return (cols, rows, more);
    }

    private static string FormatJson(JsonElement rec, string col)
    {
        // Prefer the formatted-value annotation when present.
        if (rec.TryGetProperty($"{col}@OData.Community.Display.V1.FormattedValue", out var fmt))
            return fmt.GetString() ?? "";
        if (!rec.TryGetProperty(col, out var v)) return "";
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() ?? "",
            JsonValueKind.Null => "",
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => v.GetRawText(),
            _ => v.GetRawText()
        };
    }

    private static string ExtractODataError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err) &&
                err.TryGetProperty("message", out var msg))
                return msg.GetString() ?? body;
        }
        catch { /* not json */ }
        return body.Length > 300 ? body[..300] : body;
    }

    // ── Export ─────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        if (!HasResults || PickSavePathAsync is null) return;
        var path = await PickSavePathAsync("query-results.csv", "csv");
        if (string.IsNullOrEmpty(path)) return;

        var cols = ResultColumns;
        var rows = Rows.ToList();
        try
        {
            await Task.Run(() =>
            {
                var sb = new StringBuilder();
                sb.AppendLine(string.Join(",", cols.Select(CsvEscape)));
                foreach (var r in rows)
                    sb.AppendLine(string.Join(",", r.Cells.Select(CsvEscape)));
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
            });
            Status = $"Exported {rows.Count} row(s) to {Path.GetFileName(path)}";
        }
        catch (Exception ex) { Status = $"Export failed: {ex.Message}"; }
    }

    [RelayCommand]
    private async Task ExportExcelAsync()
    {
        if (!HasResults || PickSavePathAsync is null) return;
        var path = await PickSavePathAsync("query-results.xlsx", "xlsx");
        if (string.IsNullOrEmpty(path)) return;

        var cols = ResultColumns.ToList();
        var rows = Rows.ToList();
        try
        {
            await Task.Run(() =>
            {
                using var wb = new XLWorkbook();
                var ws = wb.AddWorksheet("Results");
                for (var c = 0; c < cols.Count; c++)
                    ws.Cell(1, c + 1).Value = cols[c];
                ws.Row(1).Style.Font.Bold = true;
                for (var r = 0; r < rows.Count; r++)
                    for (var c = 0; c < cols.Count; c++)
                        ws.Cell(r + 2, c + 1).Value = rows[r].Cells[c];
                ws.Columns().AdjustToContents();
                wb.SaveAs(path);
            });
            Status = $"Exported {rows.Count} row(s) to {Path.GetFileName(path)}";
        }
        catch (Exception ex) { Status = $"Export failed: {ex.Message}"; }
    }

    private static string CsvEscape(string s)
    {
        if (s.Contains('"') || s.Contains(',') || s.Contains('\n') || s.Contains('\r'))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }
}
