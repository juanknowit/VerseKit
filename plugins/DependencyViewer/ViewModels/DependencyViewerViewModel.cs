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
using DependencyViewer.Models;
using VerseKit.PluginSdk;

namespace DependencyViewer.ViewModels;

public sealed partial class DependencyViewerViewModel : ObservableObject
{
    private const StringComparison OIC = StringComparison.OrdinalIgnoreCase;
    private const int EntityComponentType = 1;

    private readonly IConnectionProvider _connectionProvider;
    private List<EntityListItem> _allTables = [];

    public ObservableCollection<EntityListItem> Tables { get; } = [];
    public ObservableCollection<DependencyItem> Dependencies { get; } = [];

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isDependenciesLoading;
    [ObservableProperty] private string _statusMessage = "Connect to an environment to check dependencies.";
    [ObservableProperty] private string _filterText = string.Empty;
    [ObservableProperty] private string _dependenciesStatus = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTableSelected))]
    private EntityListItem? _selectedTable;

    public bool IsTableSelected => SelectedTable is not null;

    public DependencyViewerViewModel(IConnectionProvider connectionProvider)
    {
        _connectionProvider = connectionProvider;
        _connectionProvider.ConnectionChanged.Subscribe(client =>
            Dispatcher.UIThread.Post(() =>
            {
                if (client is { IsReady: true })
                {
                    _ = LoadTablesAsync(CancellationToken.None);
                }
                else
                {
                    _allTables = [];
                    Tables.Clear();
                    Dependencies.Clear();
                    SelectedTable = null;
                    StatusMessage = "Connect to an environment to check dependencies.";
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
        Tables.Clear();
        foreach (var t in _allTables.Where(t => f.Length == 0
                     || t.DisplayName.Contains(f, OIC) || t.LogicalName.Contains(f, OIC)))
            Tables.Add(t);
        StatusMessage = $"{Tables.Count} of {_allTables.Count} table(s).";
    }

    [RelayCommand]
    private async Task LoadTablesAsync(CancellationToken ct)
    {
        IsLoading = true;
        StatusMessage = "Loading tables…";
        Dependencies.Clear();
        SelectedTable = null;
        try
        {
            var client = await _connectionProvider.GetActiveConnectionAsync(ct);
            var resp = (RetrieveAllEntitiesResponse)await client.ExecuteAsync(
                new RetrieveAllEntitiesRequest { EntityFilters = EntityFilters.Entity, RetrieveAsIfPublished = true }, ct);

            var tables = resp.EntityMetadata
                .Where(m => m.MetadataId.HasValue)
                .Select(m => new EntityListItem
                {
                    MetadataId = m.MetadataId!.Value,
                    DisplayName = m.DisplayName?.UserLocalizedLabel?.Label ?? m.LogicalName ?? "",
                    LogicalName = m.LogicalName ?? ""
                })
                .OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Dispatcher.UIThread.Post(() =>
            {
                _allTables = tables;
                ApplyFilter();
            });
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() => StatusMessage = $"Error loading tables: {ex.Message}");
        }
        finally
        {
            Dispatcher.UIThread.Post(() => IsLoading = false);
        }
    }

    partial void OnSelectedTableChanged(EntityListItem? value)
    {
        Dependencies.Clear();
        DependenciesStatus = string.Empty;
        if (value is not null)
            _ = LoadDependenciesAsync(value, CancellationToken.None);
    }

    private async Task LoadDependenciesAsync(EntityListItem table, CancellationToken ct)
    {
        IsDependenciesLoading = true;
        DependenciesStatus = "Checking dependencies…";
        try
        {
            var client = await _connectionProvider.GetActiveConnectionAsync(ct);

            var resp = (RetrieveDependenciesForDeleteResponse)await client.ExecuteAsync(
                new RetrieveDependenciesForDeleteRequest
                {
                    ComponentType = EntityComponentType,
                    ObjectId = table.MetadataId
                }, ct);

            // Each "dependency" row: a dependent component that blocks deletion.
            var deps = resp.EntityCollection.Entities
                .Select(e => (
                    Type: e.GetAttributeValue<OptionSetValue>("dependentcomponenttype")?.Value ?? -1,
                    ObjectId: e.GetAttributeValue<Guid?>("dependentcomponentobjectid") ?? Guid.Empty,
                    Kind: DependencyKindLabel(e.GetAttributeValue<OptionSetValue>("dependencytype")?.Value ?? -1)))
                .Where(d => d.ObjectId != Guid.Empty)
                .ToList();

            var names = await ResolveNamesAsync(client, deps.Select(d => (d.Type, d.ObjectId)), ct);

            var items = deps
                .Select(d =>
                {
                    var has = names.TryGetValue(d.ObjectId, out var n);
                    return new DependencyItem
                    {
                        Name = has && !string.IsNullOrEmpty(n.Name) ? n.Name : "(unnamed)",
                        Detail = has && !string.IsNullOrEmpty(n.Detail) ? n.Detail
                                 : (has && !string.IsNullOrEmpty(n.Name) ? "" : d.ObjectId.ToString()),
                        TypeName = ComponentTypeName(d.Type),
                        DependencyKind = d.Kind
                    };
                })
                .OrderBy(i => i.TypeName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Dispatcher.UIThread.Post(() =>
            {
                Dependencies.Clear();
                foreach (var i in items) Dependencies.Add(i);
                DependenciesStatus = items.Count == 0
                    ? "No dependencies — this table is safe to delete."
                    : $"{items.Count} dependent component(s) — these block deletion.";
            });
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() => DependenciesStatus = $"Error: {ex.Message}");
        }
        finally
        {
            Dispatcher.UIThread.Post(() => IsDependenciesLoading = false);
        }
    }

    // ── Name resolution (shared shape with Solution Explorer) ───────────

    private static readonly Dictionary<int, (string Entity, string IdAttr, string[] NameAttrs)> RecordSources = new()
    {
        [20] = ("role", "roleid", ["name"]),
        [26] = ("savedquery", "savedqueryid", ["name"]),
        [29] = ("workflow", "workflowid", ["name"]),
        [31] = ("report", "reportid", ["name"]),
        [48] = ("fieldsecurityprofile", "fieldsecurityprofileid", ["name"]),
        [59] = ("savedqueryvisualization", "savedqueryvisualizationid", ["name"]),
        [60] = ("systemform", "formid", ["name"]),
        [61] = ("webresource", "webresourceid", ["displayname", "name"]),
        [90] = ("pluginassembly", "pluginassemblyid", ["name"]),
        [91] = ("sdkmessageprocessingstep", "sdkmessageprocessingstepid", ["name"]),
        [92] = ("serviceendpoint", "serviceendpointid", ["name"]),
        [150] = ("appmodule", "appmoduleid", ["name"]),
        [300] = ("connector", "connectorid", ["name"]),
        [371] = ("connectionreference", "connectionreferenceid", ["connectionreferencedisplayname"]),
        [380] = ("environmentvariabledefinition", "environmentvariabledefinitionid", ["displayname", "schemaname"]),
    };

    private static async Task<Dictionary<Guid, (string Name, string Detail)>> ResolveNamesAsync(
        ServiceClient client, IEnumerable<(int Type, Guid Id)> comps, CancellationToken ct)
    {
        var list = comps.ToList();
        var result = new Dictionary<Guid, (string, string)>();

        Dictionary<Guid, (string Display, string Logical)>? entityMeta = null;
        Dictionary<Guid, string>? optionSetMeta = null;

        foreach (var group in list.GroupBy(c => c.Type))
        {
            if (RecordSources.TryGetValue(group.Key, out var src))
            {
                var ids = group.Select(c => c.Id).Distinct().ToList();
                for (var i = 0; i < ids.Count; i += 400)
                {
                    var batch = ids.Skip(i).Take(400).Cast<object>().ToArray();
                    var q = new QueryExpression(src.Entity) { ColumnSet = new ColumnSet(src.NameAttrs) };
                    q.Criteria.AddCondition(src.IdAttr, ConditionOperator.In, batch);
                    try
                    {
                        foreach (var e in (await client.RetrieveMultipleAsync(q, ct)).Entities)
                        {
                            var nm = src.NameAttrs.Select(a => e.GetAttributeValue<string>(a))
                                                  .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
                            if (!string.IsNullOrWhiteSpace(nm)) result[e.Id] = (nm!, "");
                        }
                    }
                    catch { /* source table may not exist — leave unresolved */ }
                }
            }
            else if (group.Key == 1)
            {
                entityMeta ??= await LoadEntityMetaAsync(client, ct);
            }
            else if (group.Key == 9)
            {
                optionSetMeta ??= await LoadOptionSetMetaAsync(client, ct);
            }
        }

        foreach (var (type, id) in list)
        {
            if (result.ContainsKey(id)) continue;
            if (type == 1 && entityMeta is not null && entityMeta.TryGetValue(id, out var em))
                result[id] = (em.Display, em.Logical);
            else if (type == 9 && optionSetMeta is not null && optionSetMeta.TryGetValue(id, out var os))
                result[id] = (os, "");
        }

        return result;
    }

    private static async Task<Dictionary<Guid, (string, string)>> LoadEntityMetaAsync(ServiceClient client, CancellationToken ct)
    {
        var resp = (RetrieveAllEntitiesResponse)await client.ExecuteAsync(
            new RetrieveAllEntitiesRequest { EntityFilters = EntityFilters.Entity, RetrieveAsIfPublished = true }, ct);
        return resp.EntityMetadata.Where(m => m.MetadataId.HasValue)
            .ToDictionary(m => m.MetadataId!.Value,
                m => (m.DisplayName?.UserLocalizedLabel?.Label ?? m.LogicalName ?? "", m.LogicalName ?? ""));
    }

    private static async Task<Dictionary<Guid, string>> LoadOptionSetMetaAsync(ServiceClient client, CancellationToken ct)
    {
        var resp = (RetrieveAllOptionSetsResponse)await client.ExecuteAsync(
            new RetrieveAllOptionSetsRequest { RetrieveAsIfPublished = true }, ct);
        return resp.OptionSetMetadata.Where(o => o.MetadataId.HasValue)
            .ToDictionary(o => o.MetadataId!.Value,
                o => (o as OptionSetMetadata)?.DisplayName?.UserLocalizedLabel?.Label ?? o.Name ?? "");
    }

    private static string DependencyKindLabel(int type) => type switch
    {
        1 => "Solution internal",
        2 => "Published",
        3 => "Unpublished",
        _ => ""
    };

    private static string ComponentTypeName(int type) => type switch
    {
        1 => "Table",
        2 => "Column",
        3 => "Relationship",
        9 => "Choice (Option Set)",
        10 => "Entity Relationship",
        20 => "Security Role",
        26 => "View",
        29 => "Process",
        31 => "Report",
        48 => "Field Security Profile",
        59 => "Chart",
        60 => "Form",
        61 => "Web Resource",
        70 => "Field Permission",
        90 => "Plug-in Assembly",
        91 => "SDK Message Processing Step",
        92 => "Service Endpoint",
        150 => "App (Model-driven)",
        300 => "Connector",
        371 => "Connection Reference",
        380 => "Environment Variable Definition",
        _ => $"Component type {type}"
    };
}
