using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using SolutionExplorer.Models;
using VerseKit.PluginSdk;

namespace SolutionExplorer.ViewModels;

public sealed partial class SolutionExplorerViewModel : ObservableObject
{
    private const StringComparison OIC = StringComparison.OrdinalIgnoreCase;

    private readonly IConnectionProvider _connectionProvider;
    private List<SolutionItem> _allSolutions = [];

    public ObservableCollection<SolutionItem> Solutions { get; } = [];
    public ObservableCollection<ComponentItem> Components { get; } = [];
    private List<ComponentItem> _allComponents = [];

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isComponentsLoading;
    [ObservableProperty] private bool _isExporting;
    [ObservableProperty] private string _statusMessage = "Connect to an environment to browse solutions.";
    [ObservableProperty] private string _filterText = string.Empty;
    [ObservableProperty] private string _componentsStatus = string.Empty;
    [ObservableProperty] private string _componentFilterText = string.Empty;
    [ObservableProperty] private string _exportStatus = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSolutionSelected), nameof(CanExportUnmanaged))]
    private SolutionItem? _selectedSolution;

    public bool IsSolutionSelected => SelectedSolution is not null;

    /// <summary>You can only export an unmanaged copy from an unmanaged solution.</summary>
    public bool CanExportUnmanaged => SelectedSolution is { IsManaged: false };

    /// <summary>Set by the view — native save dialog; returns chosen path or null.</summary>
    public Func<string, Task<string?>>? PickSavePathAsync { get; set; }

    public SolutionExplorerViewModel(IConnectionProvider connectionProvider)
    {
        _connectionProvider = connectionProvider;
        _connectionProvider.ConnectionChanged.Subscribe(client =>
            Dispatcher.UIThread.Post(() =>
            {
                if (client is { IsReady: true })
                {
                    _ = LoadSolutionsAsync(CancellationToken.None);
                }
                else
                {
                    _allSolutions = [];
                    Solutions.Clear();
                    Components.Clear();
                    SelectedSolution = null;
                    StatusMessage = "Connect to an environment to browse solutions.";
                }
            }));
    }

    private CancellationTokenSource? _filterDebounce;

    partial void OnFilterTextChanged(string value)
    {
        _filterDebounce?.Cancel();
        var cts = _filterDebounce = new CancellationTokenSource();
        _ = DebouncedFilterAsync(cts.Token);
    }

    private async Task DebouncedFilterAsync(CancellationToken ct)
    {
        try { await Task.Delay(200, ct); ApplyFilter(); }
        catch (OperationCanceledException) { }
    }

    private void ApplyFilter()
    {
        var f = FilterText?.Trim() ?? string.Empty;
        Solutions.Clear();
        foreach (var s in _allSolutions.Where(s => f.Length == 0
                     || s.FriendlyName.Contains(f, OIC)
                     || s.UniqueName.Contains(f, OIC)
                     || s.Publisher.Contains(f, OIC)))
            Solutions.Add(s);
        StatusMessage = $"{Solutions.Count} of {_allSolutions.Count} solution(s).";
    }

    [RelayCommand]
    private async Task LoadSolutionsAsync(CancellationToken ct)
    {
        IsLoading = true;
        StatusMessage = "Loading solutions…";
        Components.Clear();
        SelectedSolution = null;
        try
        {
            var client = await _connectionProvider.GetActiveConnectionAsync(ct);

            var query = new QueryExpression("solution")
            {
                ColumnSet = new ColumnSet("solutionid", "uniquename", "friendlyname",
                                          "version", "ismanaged", "publisherid"),
                Orders = { new OrderExpression("friendlyname", OrderType.Ascending) },
                PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
            };
            // Hide internal/invisible solutions (e.g. the per-component system ones).
            query.Criteria.AddCondition("isvisible", ConditionOperator.Equal, true);

            var solutions = new List<SolutionItem>();
            while (true)
            {
                var page = await client.RetrieveMultipleAsync(query, ct);
                solutions.AddRange(page.Entities.Select(e => new SolutionItem
                {
                    SolutionId = e.Id,
                    UniqueName = e.GetAttributeValue<string>("uniquename") ?? "",
                    FriendlyName = e.GetAttributeValue<string>("friendlyname") ?? "",
                    Version = e.GetAttributeValue<string>("version") ?? "",
                    Publisher = e.GetAttributeValue<EntityReference>("publisherid")?.Name ?? "",
                    IsManaged = e.GetAttributeValue<bool>("ismanaged")
                }));

                if (!page.MoreRecords) break;
                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = page.PagingCookie;
            }

            solutions = solutions.OrderBy(s => s.Title, StringComparer.OrdinalIgnoreCase).ToList();

            Dispatcher.UIThread.Post(() =>
            {
                _allSolutions = solutions;
                ApplyFilter();
            });
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() => StatusMessage = $"Error loading solutions: {ex.Message}");
        }
        finally
        {
            Dispatcher.UIThread.Post(() => IsLoading = false);
        }
    }

    partial void OnSelectedSolutionChanged(SolutionItem? value)
    {
        Components.Clear();
        _allComponents = [];
        ComponentsStatus = string.Empty;
        ComponentFilterText = string.Empty;
        ExportStatus = string.Empty;
        if (value is not null)
            _ = LoadComponentsAsync(value, CancellationToken.None);
    }

    partial void OnComponentFilterTextChanged(string value) => ApplyComponentFilter();

    private void ApplyComponentFilter()
    {
        var f = ComponentFilterText?.Trim() ?? string.Empty;
        Components.Clear();
        foreach (var c in _allComponents.Where(c => f.Length == 0
                     || c.Name.Contains(f, OIC)
                     || c.TypeName.Contains(f, OIC)
                     || c.Detail.Contains(f, OIC)))
            Components.Add(c);
        ComponentsStatus = $"{Components.Count} of {_allComponents.Count} component(s)";
    }

    // ── Component breakdown ────────────────────────────────────────────

    private async Task LoadComponentsAsync(SolutionItem solution, CancellationToken ct)
    {
        IsComponentsLoading = true;
        ComponentsStatus = "Loading components…";
        try
        {
            var client = await _connectionProvider.GetActiveConnectionAsync(ct);

            var items = (await BuildComponentsAsync(client, solution.SolutionId, ct))
                .OrderBy(i => i.TypeName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Dispatcher.UIThread.Post(() =>
            {
                _allComponents = items;
                ApplyComponentFilter();
            });
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() => ComponentsStatus = $"Error loading components: {ex.Message}");
        }
        finally
        {
            Dispatcher.UIThread.Post(() => IsComponentsLoading = false);
        }
    }

    // componenttype → the table + id/name columns to resolve a record-based
    // component's friendly name. Metadata components (entities) are resolved
    // separately via RetrieveAllEntities; anything not here falls back to its id.
    private static readonly Dictionary<int, (string Entity, string IdAttr, string[] NameAttrs)> RecordSources = new()
    {
        [20] = ("role", "roleid", ["name"]),
        [26] = ("savedquery", "savedqueryid", ["name"]),
        [29] = ("workflow", "workflowid", ["name"]),
        [31] = ("report", "reportid", ["name"]),
        [35] = ("template", "templateid", ["title"]),
        [59] = ("savedqueryvisualization", "savedqueryvisualizationid", ["name"]),
        [60] = ("systemform", "formid", ["name"]),
        [61] = ("webresource", "webresourceid", ["displayname", "name"]),
        [62] = ("sitemap", "sitemapid", ["sitemapnameunique"]),
        [63] = ("connectionrole", "connectionroleid", ["name"]),
        [90] = ("pluginassembly", "pluginassemblyid", ["name"]),
        [91] = ("sdkmessageprocessingstep", "sdkmessageprocessingstepid", ["name"]),
        [92] = ("serviceendpoint", "serviceendpointid", ["name"]),
        [150] = ("appmodule", "appmoduleid", ["name"]),
        [152] = ("pluginpackage", "pluginpackageid", ["name", "uniquename"]),
        [300] = ("connector", "connectorid", ["name"]),
        [371] = ("connectionreference", "connectionreferenceid", ["connectionreferencedisplayname"]),
        [380] = ("environmentvariabledefinition", "environmentvariabledefinitionid", ["displayname", "schemaname"]),
        [381] = ("environmentvariablevalue", "environmentvariablevalueid", ["schemaname"]),
    };

    /// <summary>Reads the solution's components and resolves a friendly name for each.</summary>
    private static async Task<List<ComponentItem>> BuildComponentsAsync(
        ServiceClient client, Guid solutionId, CancellationToken ct)
    {
        // 1. Raw components (type + object id).
        var raw = new List<(int Type, Guid ObjectId)>();
        var q = new QueryExpression("solutioncomponent")
        {
            ColumnSet = new ColumnSet("componenttype", "objectid"),
            Criteria = { Conditions = { new ConditionExpression("solutionid", ConditionOperator.Equal, solutionId) } },
            PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
        };
        while (true)
        {
            var page = await client.RetrieveMultipleAsync(q, ct);
            foreach (var e in page.Entities)
                raw.Add((e.GetAttributeValue<OptionSetValue>("componenttype")?.Value ?? -1,
                         e.GetAttributeValue<Guid>("objectid")));
            if (!page.MoreRecords) break;
            q.PageInfo.PageNumber++;
            q.PageInfo.PagingCookie = page.PagingCookie;
        }

        // 2. Resolve names for record-based component types (batched IN queries).
        var nameById = new Dictionary<Guid, string>();
        foreach (var group in raw.GroupBy(r => r.Type))
        {
            if (!RecordSources.TryGetValue(group.Key, out var src)) continue;
            var ids = group.Select(r => r.ObjectId).Distinct().ToList();
            for (var i = 0; i < ids.Count; i += 400)
            {
                var batch = ids.Skip(i).Take(400).Cast<object>().ToArray();
                var nq = new QueryExpression(src.Entity) { ColumnSet = new ColumnSet(src.NameAttrs) };
                nq.Criteria.AddCondition(src.IdAttr, ConditionOperator.In, batch);
                try
                {
                    foreach (var e in (await client.RetrieveMultipleAsync(nq, ct)).Entities)
                    {
                        var nm = src.NameAttrs.Select(a => e.GetAttributeValue<string>(a))
                                              .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
                        if (!string.IsNullOrWhiteSpace(nm)) nameById[e.Id] = nm!;
                    }
                }
                catch { /* a source table may not exist in this org — leave as id */ }
            }
        }

        // 3. Resolve entity (Table) components from metadata (objectid = MetadataId).
        Dictionary<Guid, (string Display, string Logical)>? entityMeta = null;
        if (raw.Any(r => r.Type == 1))
        {
            try
            {
                var resp = (RetrieveAllEntitiesResponse)await client.ExecuteAsync(
                    new RetrieveAllEntitiesRequest { EntityFilters = EntityFilters.Entity, RetrieveAsIfPublished = true }, ct);
                entityMeta = resp.EntityMetadata
                    .Where(m => m.MetadataId.HasValue)
                    .ToDictionary(m => m.MetadataId!.Value,
                        m => (m.DisplayName?.UserLocalizedLabel?.Label ?? m.LogicalName ?? "", m.LogicalName ?? ""));
            }
            catch { /* ignore — entities fall back to id */ }
        }

        // 3b. Resolve global choices / option sets (objectid = OptionSet MetadataId).
        Dictionary<Guid, string>? optionSetMeta = null;
        if (raw.Any(r => r.Type == 9))
        {
            try
            {
                var resp = (RetrieveAllOptionSetsResponse)await client.ExecuteAsync(
                    new RetrieveAllOptionSetsRequest { RetrieveAsIfPublished = true }, ct);
                optionSetMeta = resp.OptionSetMetadata
                    .Where(o => o.MetadataId.HasValue)
                    .ToDictionary(o => o.MetadataId!.Value,
                        o => (o as OptionSetMetadata)?.DisplayName?.UserLocalizedLabel?.Label ?? o.Name ?? "");
            }
            catch { /* ignore */ }
        }

        // 4. Build the rows. Anything we can't name shows "(unnamed)" with the
        // raw id dimmed beneath, rather than a bare GUID as the headline.
        var items = new List<ComponentItem>(raw.Count);
        foreach (var (type, objectId) in raw)
        {
            string name;
            var detail = "";
            if (type == 1 && entityMeta is not null && entityMeta.TryGetValue(objectId, out var em))
            {
                name = em.Display;
                detail = em.Logical;
            }
            else if (type == 9 && optionSetMeta is not null
                     && optionSetMeta.TryGetValue(objectId, out var os) && !string.IsNullOrWhiteSpace(os))
            {
                name = os;
            }
            else if (nameById.TryGetValue(objectId, out var resolved))
            {
                name = resolved;
            }
            else
            {
                name = "(unnamed)";
                detail = objectId.ToString();
            }

            items.Add(new ComponentItem { Name = name, TypeName = ComponentTypeName(type), Detail = detail });
        }
        return items;
    }

    // ── Export ─────────────────────────────────────────────────────────

    [RelayCommand]
    private Task ExportUnmanagedAsync() => ExportAsync(managed: false);

    [RelayCommand]
    private Task ExportManagedAsync() => ExportAsync(managed: true);

    private async Task ExportAsync(bool managed)
    {
        if (SelectedSolution is not { } solution || PickSavePathAsync is null || IsExporting) return;

        var suffix = managed ? "_managed" : "";
        var suggested = $"{SafeFileName(solution.UniqueName)}{suffix}.zip";
        var path = await PickSavePathAsync(suggested);
        if (string.IsNullOrEmpty(path)) return;

        IsExporting = true;
        ExportStatus = $"Exporting {(managed ? "managed" : "unmanaged")} solution…";
        try
        {
            var client = await _connectionProvider.GetActiveConnectionAsync(CancellationToken.None);
            var response = (ExportSolutionResponse)await client.ExecuteAsync(
                new ExportSolutionRequest { SolutionName = solution.UniqueName, Managed = managed },
                CancellationToken.None);

            await File.WriteAllBytesAsync(path, response.ExportSolutionFile);
            ExportStatus = $"Exported to {Path.GetFileName(path)} ({response.ExportSolutionFile.Length / 1024} KB)";
        }
        catch (Exception ex)
        {
            ExportStatus = $"Export failed: {ex.Message}";
        }
        finally
        {
            IsExporting = false;
        }
    }

    private static string SafeFileName(string name)
    {
        foreach (var ch in Path.GetInvalidFileNameChars())
            name = name.Replace(ch, '_');
        return name;
    }

    // Friendly names for the common solutioncomponent types; unknown → "Type {n}".
    private static string ComponentTypeName(int type) => type switch
    {
        1 => "Tables (Entity)",
        2 => "Columns (Attribute)",
        3 => "Relationships",
        9 => "Choices (Option Set)",
        10 => "Entity Relationships",
        20 => "Security Roles",
        26 => "Views (Saved Query)",
        29 => "Processes (Workflow / Flow)",
        31 => "Reports",
        35 => "Email Templates",
        36 => "Mail Merge Templates",
        37 => "Duplicate Rules",
        48 => "Field Security Profiles",
        59 => "Charts",
        60 => "Forms",
        61 => "Web Resources",
        62 => "Site Maps",
        63 => "Connection Roles",
        70 => "Field Permissions",
        71 => "Plug-in Types",
        90 => "Plug-in Assemblies",
        91 => "SDK Message Processing Steps",
        92 => "Service Endpoints",
        150 => "Apps (Model-driven)",
        152 => "Plugin Packages",
        300 => "Connectors",
        371 => "Connection References",
        380 => "Environment Variable Definitions",
        381 => "Environment Variable Values",
        -1 => "Unspecified",
        _ => $"Component type {type}"
    };
}
