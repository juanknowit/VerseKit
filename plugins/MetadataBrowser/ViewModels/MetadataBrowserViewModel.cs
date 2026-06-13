using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using MetadataBrowser.Models;
using VerseKit.PluginSdk;

namespace MetadataBrowser.ViewModels;

public sealed partial class MetadataBrowserViewModel : ObservableObject
{
    private readonly IConnectionProvider _connectionProvider;

    // Full unfiltered sets, cached so the filters are instant.
    private List<EntityItem> _allEntities = [];
    private List<AttributeItem> _allAttributes = [];

    public ObservableCollection<EntityItem> Entities { get; } = [];
    public ObservableCollection<AttributeItem> Attributes { get; } = [];

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isAttributesLoading;
    [ObservableProperty] private string _statusMessage = "Connect to an environment to browse metadata.";
    [ObservableProperty] private string _filterText = string.Empty;
    [ObservableProperty] private string _attributeFilterText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEntitySelected))]
    private EntityItem? _selectedEntity;

    public bool IsEntitySelected => SelectedEntity is not null;

    public MetadataBrowserViewModel(IConnectionProvider connectionProvider)
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
        Entities.Clear();
        foreach (var e in _allEntities.Where(e => f.Length == 0
                     || e.LogicalName.Contains(f, StringComparison.OrdinalIgnoreCase)
                     || e.DisplayName.Contains(f, StringComparison.OrdinalIgnoreCase)))
            Entities.Add(e);
        StatusMessage = $"{Entities.Count} of {_allEntities.Count} table(s).";
    }

    partial void OnAttributeFilterTextChanged(string value) => ApplyAttributeFilter();

    private void ApplyAttributeFilter()
    {
        var f = AttributeFilterText?.Trim() ?? string.Empty;
        Attributes.Clear();
        foreach (var a in _allAttributes.Where(a => f.Length == 0
                     || a.LogicalName.Contains(f, StringComparison.OrdinalIgnoreCase)
                     || a.DisplayName.Contains(f, StringComparison.OrdinalIgnoreCase)
                     || a.AttributeType.Contains(f, StringComparison.OrdinalIgnoreCase)))
            Attributes.Add(a);
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
        Attributes.Clear();
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
            var response = (RetrieveEntityResponse)await client.ExecuteAsync(
                new RetrieveEntityRequest
                {
                    LogicalName = entity.LogicalName,
                    EntityFilters = EntityFilters.Attributes,
                    RetrieveAsIfPublished = true
                }, ct);

            var attrs = (response.EntityMetadata.Attributes ?? [])
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

            Dispatcher.UIThread.Post(() =>
            {
                _allAttributes = attrs;
                ApplyAttributeFilter();
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
}
