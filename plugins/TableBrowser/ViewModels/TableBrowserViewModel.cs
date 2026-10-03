using System.Collections.ObjectModel;
using System.Text;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using TableBrowser.Models;
using VerseKit.PluginSdk;

namespace TableBrowser.ViewModels;

public sealed partial class TableBrowserViewModel : ObservableObject
{
    private readonly IConnectionProvider _connectionProvider;

    // Full unfiltered sets, cached so the filters are instant.
    private List<EntityItem> _allEntities = [];
    private List<AttributeItem> _allAttributes = [];
    private List<RelationshipItem> _allRelationships = [];
    private List<KeyItem> _allKeys = [];

    public ObservableCollection<EntityItem> Entities { get; } = [];
    public ObservableCollection<AttributeItem> Attributes { get; } = [];
    public ObservableCollection<RelationshipItem> Relationships { get; } = [];
    public ObservableCollection<KeyItem> Keys { get; } = [];

    /// <summary>Detail tab: 0 Columns, 1 Relationships, 2 Keys.</summary>
    [ObservableProperty] private int _detailTabIndex;

    /// <summary>Set by the view: shows a save dialog, returns the path or null.</summary>
    public Func<string, Task<string?>>? PickSavePathAsync { get; set; }

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isAttributesLoading;
    [ObservableProperty] private string _statusMessage = "Connect to an environment to browse metadata.";
    [ObservableProperty] private string _filterText = string.Empty;
    [ObservableProperty] private string _attributeFilterText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEntitySelected))]
    private EntityItem? _selectedEntity;

    public bool IsEntitySelected => SelectedEntity is not null;

    public TableBrowserViewModel(IConnectionProvider connectionProvider)
    {
        _connectionProvider = connectionProvider;
        _connectionProvider.ConnectionChanged.Subscribe(client =>
            Dispatcher.UIThread.Post(() =>
            {
                if (client is { IsReady: true })
                {
                    _ = LoadEntitiesAsync(CancellationToken.None);
                }
                else
                {
                    _allEntities = [];
                    Entities.Clear();
                    Attributes.Clear();
                    SelectedEntity = null;
                    StatusMessage = "Connect to an environment to browse metadata.";
                }
            }));
    }

    // Filter on every keystroke — local and cheap, and instant feedback
    // matters more than saving work (DESIGN.md §7, "respond immediately").
    partial void OnFilterTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var f = FilterText?.Trim() ?? string.Empty;
        Entities.Clear();
        foreach (var e in _allEntities.Where(e => f.Length == 0
                     || e.LogicalName.Contains(f, StringComparison.OrdinalIgnoreCase)
                     || e.DisplayName.Contains(f, StringComparison.OrdinalIgnoreCase)))
            Entities.Add(e);
        StatusMessage = $"{Entities.Count} of {_allEntities.Count} table(s).";
    }

    // One filter box serves all three detail tabs.
    partial void OnAttributeFilterTextChanged(string value) => ApplyDetailFilter();

    private void ApplyDetailFilter()
    {
        var f = AttributeFilterText?.Trim() ?? string.Empty;
        bool Match(params string[] fields) =>
            f.Length == 0 || fields.Any(x => x.Contains(f, StringComparison.OrdinalIgnoreCase));

        Attributes.Clear();
        foreach (var a in _allAttributes.Where(a => Match(a.LogicalName, a.DisplayName, a.AttributeType)))
            Attributes.Add(a);

        Relationships.Clear();
        foreach (var r in _allRelationships.Where(r => Match(r.SchemaName, r.RelatedTable, r.Via, r.Kind)))
            Relationships.Add(r);

        Keys.Clear();
        foreach (var k in _allKeys.Where(k => Match(k.LogicalName, k.DisplayName, k.Columns)))
            Keys.Add(k);
    }

    [RelayCommand]
    private async Task LoadEntitiesAsync(CancellationToken ct)
    {
        IsLoading = true;
        StatusMessage = "Loading tables…";
        Attributes.Clear();
        SelectedEntity = null;
        try
        {
            var client = await _connectionProvider.GetActiveConnectionAsync(ct);

            // Entity-level only (no attributes) keeps this fast; attributes are
            // fetched lazily when a table is selected.
            var response = (RetrieveAllEntitiesResponse)await client.ExecuteAsync(
                new RetrieveAllEntitiesRequest
                {
                    EntityFilters = EntityFilters.Entity,
                    RetrieveAsIfPublished = true
                }, ct);

            var items = response.EntityMetadata
                .Select(m => new EntityItem
                {
                    LogicalName = m.LogicalName ?? "",
                    DisplayName = m.DisplayName?.UserLocalizedLabel?.Label ?? "",
                    SchemaName = m.SchemaName ?? "",
                    IsCustom = m.IsCustomEntity ?? false,
                    IsManaged = m.IsManaged ?? false,
                    ObjectTypeCode = m.ObjectTypeCode,
                    PrimaryIdAttribute = m.PrimaryIdAttribute,
                    PrimaryNameAttribute = m.PrimaryNameAttribute
                })
                .OrderBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Dispatcher.UIThread.Post(() =>
            {
                _allEntities = items;
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

    partial void OnSelectedEntityChanged(EntityItem? value)
    {
        _allAttributes = [];
        _allRelationships = [];
        _allKeys = [];
        Attributes.Clear();
        Relationships.Clear();
        Keys.Clear();
        AttributeFilterText = string.Empty;
        if (value is not null)
            _ = LoadAttributesAsync(value, CancellationToken.None);
    }

    private async Task LoadAttributesAsync(EntityItem entity, CancellationToken ct)
    {
        IsAttributesLoading = true;
        try
        {
            var client = await _connectionProvider.GetActiveConnectionAsync(ct);
            // All = attributes + relationships + alternate keys in one call.
            var response = (RetrieveEntityResponse)await client.ExecuteAsync(
                new RetrieveEntityRequest
                {
                    LogicalName = entity.LogicalName,
                    EntityFilters = EntityFilters.All,
                    RetrieveAsIfPublished = true
                }, ct);
            var md = response.EntityMetadata;

            var attrs = (md.Attributes ?? [])
                .Select(a => new AttributeItem
                {
                    LogicalName = a.LogicalName ?? "",
                    DisplayName = a.DisplayName?.UserLocalizedLabel?.Label ?? "",
                    AttributeType = a.AttributeType?.ToString() ?? "—",
                    RequiredLevel = a.RequiredLevel?.Value.ToString() ?? "None",
                    IsCustom = a.IsCustomAttribute ?? false,
                    IsPrimaryId = a.IsPrimaryId ?? false,
                    IsPrimaryName = a.IsPrimaryName ?? false
                })
                .OrderBy(a => a.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var rels = (md.OneToManyRelationships ?? [])
                .Select(r => new RelationshipItem
                {
                    SchemaName = r.SchemaName ?? "", Kind = "1:N",
                    RelatedTable = r.ReferencingEntity ?? "", Via = r.ReferencingAttribute ?? "",
                    IsCustom = r.IsCustomRelationship ?? false
                })
                .Concat((md.ManyToOneRelationships ?? []).Select(r => new RelationshipItem
                {
                    SchemaName = r.SchemaName ?? "", Kind = "N:1",
                    RelatedTable = r.ReferencedEntity ?? "", Via = r.ReferencingAttribute ?? "",
                    IsCustom = r.IsCustomRelationship ?? false
                }))
                .Concat((md.ManyToManyRelationships ?? []).Select(r => new RelationshipItem
                {
                    SchemaName = r.SchemaName ?? "", Kind = "N:N",
                    RelatedTable = r.Entity1LogicalName == entity.LogicalName
                        ? r.Entity2LogicalName ?? "" : r.Entity1LogicalName ?? "",
                    Via = r.IntersectEntityName ?? "",
                    IsCustom = r.IsCustomRelationship ?? false
                }))
                .OrderBy(r => r.Kind).ThenBy(r => r.SchemaName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var keys = (md.Keys ?? [])
                .Select(k => new KeyItem
                {
                    LogicalName = k.LogicalName ?? "",
                    DisplayName = k.DisplayName?.UserLocalizedLabel?.Label ?? "",
                    Columns = string.Join(", ", k.KeyAttributes ?? []),
                    Status = k.EntityKeyIndexStatus.ToString()
                })
                .OrderBy(k => k.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Dispatcher.UIThread.Post(() =>
            {
                // A newer selection may have superseded this response.
                if (SelectedEntity != entity) return;
                _allAttributes = attrs;
                _allRelationships = rels;
                _allKeys = keys;
                ApplyDetailFilter();
            });
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() => StatusMessage = $"Error loading columns: {ex.Message}");
        }
        finally
        {
            Dispatcher.UIThread.Post(() => IsAttributesLoading = false);
        }
    }

    // ── Export ──────────────────────────────────────────────────────────

    /// <summary>Exports the active tab's (filtered) rows to CSV.</summary>
    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        if (SelectedEntity is not { } entity || PickSavePathAsync is null) return;

        var (name, headers, rows) = DetailTabIndex switch
        {
            1 => ("relationships",
                  new[] { "Schema name", "Type", "Related table", "Via", "Custom" },
                  Relationships.Select(r => new[] { r.SchemaName, r.Kind, r.RelatedTable, r.Via, r.IsCustom ? "Yes" : "No" }).ToList()),
            2 => ("keys",
                  new[] { "Display name", "Logical name", "Columns", "Status" },
                  Keys.Select(k => new[] { k.DisplayName, k.LogicalName, k.Columns, k.Status }).ToList()),
            _ => ("columns",
                  new[] { "Display name", "Logical name", "Type", "Required", "Custom" },
                  Attributes.Select(a => new[] { a.DisplayName, a.LogicalName, a.AttributeType, a.RequiredLevel, a.IsCustom ? "Yes" : "No" }).ToList()),
        };
        if (rows.Count == 0) { StatusMessage = "Nothing to export."; return; }

        var path = await PickSavePathAsync($"{entity.LogicalName}-{name}.csv");
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            await Task.Run(() =>
            {
                var sb = new StringBuilder();
                sb.AppendLine(string.Join(",", headers.Select(CsvEscape)));
                foreach (var r in rows)
                    sb.AppendLine(string.Join(",", r.Select(CsvEscape)));
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
            });
            StatusMessage = $"Exported {rows.Count:N0} {name} to {Path.GetFileName(path)}";
        }
        catch (Exception ex) { StatusMessage = $"Export failed: {ex.Message}"; }
    }

    private static string CsvEscape(string value) =>
        value.IndexOfAny([',', '"', '\n', '\r']) >= 0
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
}
