using System.Collections.ObjectModel;
using System.Text;
using Acornima;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using ResourceManager.Models;
using VerseKit.PluginSdk;

namespace ResourceManager.ViewModels;

public sealed partial class WebResourcesViewModel : ObservableObject
{
    private readonly IConnectionProvider _connectionProvider;

    /// <summary>Set by the view to show a confirmation dialog
    /// (title, message, confirm-button label, destructive?). Returns true to
    /// proceed. Left null in tests/headless — treated as "confirmed".</summary>
    public Func<string, string, string, bool, Task<bool>>? ConfirmAsync { get; set; }

    private Task<bool> ConfirmOrProceedAsync(string title, string message,
        string confirmLabel, bool destructive = false) =>
        ConfirmAsync?.Invoke(title, message, confirmLabel, destructive) ?? Task.FromResult(true);

    /// <summary>Set by the view to prompt for a new web resource. Receives the
    /// publisher prefix; returns the chosen (name, type) or null if cancelled.</summary>
    public Func<string, Task<(string Name, WebResourceType Type)?>>? PromptNewResourceAsync { get; set; }

    // ── Collections ─────────────────────────────────────────────────
    /// <summary>Tree shown in the left pane: SolutionGroup nodes when
    /// grouping (All Solutions), WebResourceItem leaves otherwise.
    /// Swapped wholesale on rebuild — assigning a new list once is far
    /// cheaper than thousands of CollectionChanged events.</summary>
    [ObservableProperty] private List<object> _resourceTree = [];
    public ObservableCollection<SolutionItem> Solutions { get; } = [];

    // Full unfiltered result set + solution membership, cached so the
    // name filter can be applied client-side in realtime.
    private List<WebResourceItem> _allItems = [];
    private Dictionary<Guid, List<string>> _membership = new();
    private bool _loadedAllSolutions;

    // Paging state for "Load more"
    private int _pageNumber = 1;
    private string? _pagingCookie;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreCommand))]
    private bool _hasMoreResults;

    // ── Loading flags ────────────────────────────────────────────────
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isEditorLoading;

    // ── Status messages ──────────────────────────────────────────────
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string _editorStatus = string.Empty;

    // ── Solution / filter ────────────────────────────────────────────
    [ObservableProperty] private string _filterText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateWebResourceCommand))]
    private SolutionItem? _selectedSolution;

    /// <summary>New resources need a concrete solution (target + prefix).</summary>
    public bool CanCreate => SelectedSolution is { IsReal: true };

    // ── Editor state ─────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTextResource))]
    [NotifyPropertyChangedFor(nameof(IsEditorVisible))]
    [NotifyPropertyChangedFor(nameof(IsBinaryResource))]
    [NotifyPropertyChangedFor(nameof(IsNothingSelected))]
    [NotifyPropertyChangedFor(nameof(IsScriptResource))]
    [NotifyCanExecuteChangedFor(nameof(SaveResourceCommand))]
    [NotifyCanExecuteChangedFor(nameof(PublishResourceCommand))]
    [NotifyCanExecuteChangedFor(nameof(CheckSyntaxCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteResourceCommand))]
    private WebResourceItem? _selectedResource;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveResourceCommand))]
    private bool _isDirty;

    [ObservableProperty] private string _editorText = string.Empty;

    /// <summary>Currently selected tree node — group header or resource.</summary>
    [ObservableProperty] private object? _selectedNode;

    partial void OnSelectedNodeChanged(object? value)
    {
        // Clicking a group header keeps the current editor content.
        if (value is WebResourceItem item)
            SelectedResource = item;
    }

    // Debounce the filter so the tree rebuilds when typing pauses,
    // not on every keystroke.
    private CancellationTokenSource? _filterDebounce;

    partial void OnFilterTextChanged(string value)
    {
        _filterDebounce?.Cancel();
        var cts = _filterDebounce = new CancellationTokenSource();
        _ = DebouncedRebuildAsync(cts.Token);
    }

    private async Task DebouncedRebuildAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(250, ct);
            RebuildTree();
        }
        catch (OperationCanceledException) { /* superseded by newer keystroke */ }
    }

    // Suppress dirty-tracking while loading content
    private bool _suppressDirty;
    private string _loadedText = string.Empty;

    // ── Computed properties ──────────────────────────────────────────

    public bool IsTextResource => SelectedResource is not null &&
        SelectedResource.ResourceType is
            WebResourceType.WebPage or
            WebResourceType.CssStylesheet or
            WebResourceType.Script or
            WebResourceType.Data or
            WebResourceType.Xsl or
            WebResourceType.Resx;

    public bool IsBinaryResource => SelectedResource is not null && !IsTextResource;
    public bool IsEditorVisible => SelectedResource is not null;
    public bool IsNothingSelected => SelectedResource is null;
    public bool IsScriptResource => SelectedResource?.ResourceType == WebResourceType.Script;

    // ── Constructor ──────────────────────────────────────────────────

    public WebResourcesViewModel(IConnectionProvider connectionProvider)
    {
        _connectionProvider = connectionProvider;
        _connectionProvider.ConnectionChanged.Subscribe(newClient =>
            Dispatcher.UIThread.Post(() =>
            {
                if (newClient is { IsReady: true })
                {
                    _ = LoadSolutionsAsync(CancellationToken.None);
                }
                else
                {
                    Solutions.Clear();
                    ResourceTree = [];
                    _allItems = [];
                    _membership = new();
                    SelectedResource = null;
                    StatusMessage = "Connect to an environment to load solutions.";
                }
            }));
    }

    // ── Editor tracking ──────────────────────────────────────────────

    partial void OnEditorTextChanged(string value)
    {
        if (!_suppressDirty)
            IsDirty = value != _loadedText;
    }

    partial void OnSelectedResourceChanged(WebResourceItem? value)
    {
        _suppressDirty = true;
        EditorText = string.Empty;
        _suppressDirty = false;
        _loadedText = string.Empty;
        IsDirty = false;
        EditorStatus = string.Empty;

        if (value is not null)
        {
            if (IsTextResource)
                _ = LoadContentAsync(value, CancellationToken.None);
            else
                EditorStatus = "Binary resource — download not yet supported.";
        }
    }

    // ── Solution loading ─────────────────────────────────────────────

    [RelayCommand]
    private async Task LoadSolutionsAsync(CancellationToken ct)
    {
        IsLoading = true;
        StatusMessage = "Loading solutions…";
        ResourceTree = [];
        _allItems = [];
        _membership = new();
        Solutions.Clear();
        SelectedResource = null;

        try
        {
            var client = await _connectionProvider.GetActiveConnectionAsync(ct);
            var solutions = await FetchSolutionsAsync(client, ct);

            Solutions.Add(SolutionItem.All);
            foreach (var s in solutions)
                Solutions.Add(s);

            SelectedSolution = SolutionItem.All;
            StatusMessage = $"{solutions.Count} solution(s) — pick one and click Load.";
            Log($"Loaded {solutions.Count} solutions.");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading solutions: {ex.Message}";
            Log($"Error loading solutions: {ex}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task<List<SolutionItem>> FetchSolutionsAsync(ServiceClient client, CancellationToken ct)
    {
        var query = new QueryExpression("solution")
        {
            ColumnSet = new ColumnSet("solutionid", "friendlyname", "uniquename", "version"),
            Criteria = new FilterExpression()
        };
        query.Criteria.AddCondition("isvisible", ConditionOperator.Equal, true);
        query.Criteria.AddCondition("uniquename", ConditionOperator.NotEqual, "Default");
        query.Criteria.AddCondition("ismanaged", ConditionOperator.Equal, false);
        query.Orders.Add(new OrderExpression("friendlyname", OrderType.Ascending));

        // Join the publisher to get its customization prefix, used when
        // naming new web resources created into this solution.
        var pub = query.AddLink("publisher", "publisherid", "publisherid", JoinOperator.LeftOuter);
        pub.EntityAlias = "pub";
        pub.Columns = new ColumnSet("customizationprefix");

        var result = await client.RetrieveMultipleAsync(query, ct);
        return result.Entities.Select(e => new SolutionItem
        {
            Id = e.Id,
            FriendlyName = e.GetAttributeValue<string>("friendlyname") ?? e.Id.ToString(),
            UniqueName = e.GetAttributeValue<string>("uniquename") ?? "",
            Version = e.GetAttributeValue<string>("version") ?? "",
            PublisherPrefix =
                e.GetAttributeValue<AliasedValue>("pub.customizationprefix")?.Value as string ?? ""
        }).ToList();
    }

    // ── Web resource loading ─────────────────────────────────────────

    [RelayCommand]
    private async Task LoadAsync(CancellationToken ct)
    {
        IsLoading = true;
        SelectedResource = null;
        var loadingAll = SelectedSolution is null || SelectedSolution.Id == Guid.Empty;
        var label = loadingAll ? "all solutions" : SelectedSolution!.FriendlyName;
        StatusMessage = $"Loading web resources from {label}…";

        try
        {
            var client = await _connectionProvider.GetActiveConnectionAsync(ct);

            // First page of resources and (for All Solutions) the
            // solution-membership map, fetched in parallel.
            var pageTask = FetchPageAsync(client, SelectedSolution, pageNumber: 1,
                pagingCookie: null, ct);
            var membershipTask = loadingAll
                ? FetchMembershipAsync(client, ct)
                : Task.FromResult(new Dictionary<Guid, List<string>>());

            await Task.WhenAll(pageTask, membershipTask);
            var (items, more, cookie) = pageTask.Result;
            var membership = membershipTask.Result;

            Dispatcher.UIThread.Post(() =>
            {
                _allItems = items;
                _membership = membership;
                _loadedAllSolutions = loadingAll;
                _pageNumber = 1;
                _pagingCookie = cookie;
                HasMoreResults = more;
                RebuildTree();
            });
            Log($"Loaded {items.Count} web resources from {label} (more: {more}).");
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() => StatusMessage = $"Error: {ex.Message}");
            Log($"Error loading web resources: {ex}");
        }
        finally
        {
            Dispatcher.UIThread.Post(() => IsLoading = false);
        }
    }

    /// <summary>Appends the next 5,000 resources to the cached list.</summary>
    [RelayCommand(CanExecute = nameof(HasMoreResults))]
    private async Task LoadMoreAsync(CancellationToken ct)
    {
        IsLoading = true;
        StatusMessage = "Loading more resources…";
        try
        {
            var client = await _connectionProvider.GetActiveConnectionAsync(ct);
            var (items, more, cookie) = await FetchPageAsync(client, SelectedSolution,
                _pageNumber + 1, _pagingCookie, ct);

            Dispatcher.UIThread.Post(() =>
            {
                _allItems.AddRange(items);
                _pageNumber++;
                _pagingCookie = cookie;
                HasMoreResults = more;
                RebuildTree();
            });
            Log($"Loaded {items.Count} more web resources (page {_pageNumber + 1}).");
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() => StatusMessage = $"Error: {ex.Message}");
            Log($"Error loading more web resources: {ex}");
        }
        finally
        {
            Dispatcher.UIThread.Post(() => IsLoading = false);
        }
    }

    // ── Create new web resource ──────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateWebResourceAsync(CancellationToken ct)
    {
        if (SelectedSolution is not { IsReal: true } solution) return;
        if (PromptNewResourceAsync is null) return;

        var prefix = string.IsNullOrWhiteSpace(solution.PublisherPrefix)
            ? "new" : solution.PublisherPrefix;

        var choice = await PromptNewResourceAsync(prefix);
        if (choice is not { } pick) return; // cancelled

        // Compose the full unique name: "{prefix}_{typed}" with the right
        // extension, unless the user already typed the prefix/extension.
        var name = ComposeResourceName(pick.Name, prefix);

        EditorStatus = $"Creating {name}…";
        try
        {
            var client = await _connectionProvider.GetActiveConnectionAsync(ct);

            // Reject duplicates up front for a clearer message than the
            // server's SQL uniqueness error.
            if (_allItems.Any(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                EditorStatus = $"A web resource named '{name}' already exists.";
                return;
            }

            var entity = new Entity("webresource")
            {
                ["name"] = name,
                ["displayname"] = name,
                ["webresourcetype"] = new OptionSetValue((int)pick.Type),
                // Empty but valid content so the row is immediately editable.
                ["content"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Empty))
            };

            var id = await client.CreateAsync(entity, ct);

            // Add it to the chosen solution (component type 61 = Web Resource).
            var add = new OrganizationRequest("AddSolutionComponent")
            {
                ["ComponentId"] = id,
                ["ComponentType"] = 61,
                ["SolutionUniqueName"] = solution.UniqueName,
                ["AddRequiredComponents"] = false
            };
            await client.ExecuteAsync(add, ct);

            var item = new WebResourceItem
            {
                Id = id,
                Name = name,
                DisplayName = name,
                ResourceType = pick.Type,
                IsManaged = false,
                ModifiedOn = DateTime.Now
            };

            Dispatcher.UIThread.Post(() =>
            {
                _allItems.Add(item);
                if (_membership.TryGetValue(id, out var l)) l.Add(solution.FriendlyName);
                else _membership[id] = [solution.FriendlyName];
                RebuildTree();
                SelectedResource = item;        // open it in the editor
                EditorStatus = "Created. Edit the content, Save, then Publish.";
            });
            Log($"Created web resource '{name}' in solution '{solution.UniqueName}'.");
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() => EditorStatus = $"Create failed: {ex.Message}");
            Log($"Create failed for '{name}': {ex.Message}");
        }
    }

    /// <summary>Builds a valid unique web resource name from user input.
    /// Dynamics requires the publisher prefix ("{prefix}_"); the file
    /// extension is left to the user — we don't impose one.</summary>
    private static string ComposeResourceName(string typed, string prefix)
    {
        typed = typed.Trim().TrimStart('/');

        if (!typed.StartsWith(prefix + "_", StringComparison.OrdinalIgnoreCase))
            typed = $"{prefix}_{typed}";

        return typed;
    }

    /// <summary>Applies the name filter to the cached result set and
    /// rebuilds the tree. Pure client-side — instant as you type.</summary>
    private void RebuildTree()
    {
        var filter = FilterText?.Trim() ?? string.Empty;
        var filtered = _allItems
            .Where(i => filter.Length == 0
                || i.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || i.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var tree = new List<object>();

        if (_loadedAllSolutions && _membership.Count > 0)
        {
            var groups = new SortedDictionary<string, List<WebResourceItem>>(
                StringComparer.OrdinalIgnoreCase);
            var ungrouped = new List<WebResourceItem>();

            foreach (var item in filtered)
            {
                if (_membership.TryGetValue(item.Id, out var solutionNames))
                {
                    foreach (var name in solutionNames)
                    {
                        if (!groups.TryGetValue(name, out var list))
                            groups[name] = list = [];
                        list.Add(item);
                    }
                }
                else
                {
                    ungrouped.Add(item);
                }
            }

            foreach (var (name, list) in groups)
                tree.Add(new SolutionGroup { Name = name, Items = list });
            if (ungrouped.Count > 0)
                tree.Add(new SolutionGroup { Name = "Default Solution", Items = ungrouped });

            ResourceTree = tree;
            StatusMessage = $"{filtered.Count} resource(s) in {tree.Count} solution(s)."
                + (HasMoreResults ? " More available." : "");
        }
        else
        {
            tree.AddRange(filtered);
            ResourceTree = tree;
            StatusMessage = $"{filtered.Count} resource(s)."
                + (HasMoreResults ? " More available." : "");
        }
    }

    /// <summary>Fetches one 5,000-row page of web resources. Returns the
    /// items plus paging state for the optional next page.</summary>
    private static async Task<(List<WebResourceItem> Items, bool More, string? Cookie)>
        FetchPageAsync(ServiceClient client, SolutionItem? solution,
                       int pageNumber, string? pagingCookie, CancellationToken ct)
    {
        var query = new QueryExpression("webresource")
        {
            // Do NOT include "content" here — lazy load when selected
            ColumnSet = new ColumnSet("name", "displayname", "webresourcetype",
                                      "description", "ismanaged", "modifiedon"),
            Orders = { new OrderExpression("name", OrderType.Ascending) },
            PageInfo = new PagingInfo
            {
                PageNumber = pageNumber,
                Count = 5000,
                PagingCookie = pagingCookie
            }
        };

        if (solution is { Id: var sid } && sid != Guid.Empty)
        {
            var link = query.AddLink("solutioncomponent", "webresourceid", "objectid",
                JoinOperator.Inner);
            link.LinkCriteria.AddCondition("solutionid", ConditionOperator.Equal, sid);
            link.LinkCriteria.AddCondition("componenttype", ConditionOperator.Equal, 61);
        }

        var page = await client.RetrieveMultipleAsync(query, ct);

        var items = page.Entities.Select(e => new WebResourceItem
        {
            Id = e.Id,
            Name = e.GetAttributeValue<string>("name") ?? "",
            DisplayName = e.GetAttributeValue<string>("displayname") ?? "",
            ResourceType = (WebResourceType)(
                e.GetAttributeValue<OptionSetValue>("webresourcetype")?.Value ?? 1),
            Description = e.GetAttributeValue<string>("description"),
            IsManaged = e.GetAttributeValue<bool>("ismanaged"),
            ModifiedOn = e.GetAttributeValue<DateTime?>("modifiedon")
        }).ToList();

        return (items, page.MoreRecords, page.PagingCookie);
    }

    /// <summary>Maps webresource id → friendly names of the visible
    /// unmanaged solutions it belongs to (componenttype 61).</summary>
    private static async Task<Dictionary<Guid, List<string>>> FetchMembershipAsync(
        ServiceClient client, CancellationToken ct)
    {
        var map = new Dictionary<Guid, List<string>>();

        var query = new QueryExpression("solutioncomponent")
        {
            ColumnSet = new ColumnSet("objectid"),
            PageInfo = new PagingInfo { PageNumber = 1, Count = 5000 }
        };
        query.Criteria.AddCondition("componenttype", ConditionOperator.Equal, 61);

        var link = query.AddLink("solution", "solutionid", "solutionid");
        link.EntityAlias = "sol";
        link.Columns = new ColumnSet("friendlyname");
        link.LinkCriteria.AddCondition("isvisible", ConditionOperator.Equal, true);
        link.LinkCriteria.AddCondition("uniquename", ConditionOperator.NotEqual, "Default");

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var page = await client.RetrieveMultipleAsync(query, ct);

            foreach (var e in page.Entities)
            {
                var objectId = e.GetAttributeValue<Guid>("objectid");
                var name = e.GetAttributeValue<AliasedValue>("sol.friendlyname")?.Value as string;
                if (objectId == Guid.Empty || name is null) continue;

                if (!map.TryGetValue(objectId, out var list))
                    map[objectId] = list = [];
                if (!list.Contains(name))
                    list.Add(name);
            }

            if (!page.MoreRecords) break;
            query.PageInfo.PageNumber++;
            query.PageInfo.PagingCookie = page.PagingCookie;
        }
        return map;
    }

    // ── Content loading ──────────────────────────────────────────────

    private async Task LoadContentAsync(WebResourceItem item, CancellationToken ct)
    {
        IsEditorLoading = true;
        EditorStatus = "Loading content…";

        try
        {
            var client = await _connectionProvider.GetActiveConnectionAsync(ct);
            var entity = await client.RetrieveAsync("webresource", item.Id,
                new ColumnSet("content"), ct);

            var base64 = entity.GetAttributeValue<string>("content") ?? string.Empty;
            string text;

            if (!string.IsNullOrEmpty(base64))
            {
                var bytes = Convert.FromBase64String(base64);
                text = Encoding.UTF8.GetString(bytes);
                // Strip BOM
                if (text.Length > 0 && text[0] == '﻿')
                    text = text[1..];
            }
            else
            {
                text = string.Empty;
            }

            _loadedText = text;

            Dispatcher.UIThread.Post(() =>
            {
                _suppressDirty = true;
                EditorText = text;
                _suppressDirty = false;
                IsDirty = false;
                EditorStatus = $"Ready  ({base64.Length} chars base64)";
            });

            Log($"Loaded content for: {item.Name}");
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() => EditorStatus = $"Load failed: {ex.Message}");
            Log($"Error loading content for {item.Name}: {ex.Message}");
        }
        finally
        {
            Dispatcher.UIThread.Post(() => IsEditorLoading = false);
        }
    }

    // ── JavaScript syntax check (Acornima parser) ────────────────────

    /// <summary>
    /// Parses the editor text as JavaScript. Returns null when valid,
    /// otherwise the parser error message (includes line/column).
    /// Tries script first, then module, so both classic D365 form
    /// scripts and modern ES modules pass.
    /// </summary>
    private string? ValidateJavaScript()
    {
        var code = EditorText;
        if (string.IsNullOrWhiteSpace(code)) return null;

        try
        {
            new Parser().ParseScript(code);
            return null;
        }
        catch (Exception scriptError)
        {
            try
            {
                new Parser().ParseModule(code);
                return null;
            }
            catch
            {
                return scriptError.Message;
            }
        }
    }

    [RelayCommand(CanExecute = nameof(IsScriptResource))]
    private void CheckSyntax()
    {
        var error = ValidateJavaScript();
        EditorStatus = error is null
            ? "✓ No syntax errors."
            : $"Syntax error: {error}";
        Log(error is null
            ? $"Syntax OK: {SelectedResource?.Name}"
            : $"Syntax error in {SelectedResource?.Name}: {error}");
    }

    // ── Save ─────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveResourceAsync(CancellationToken ct)
    {
        if (SelectedResource is null) return;

        EditorStatus = "Saving…";
        try
        {
            // Stash the current server content locally before overwriting,
            // so an accidental save is recoverable from disk.
            BackupContent(SelectedResource, _loadedText);

            var client = await _connectionProvider.GetActiveConnectionAsync(ct);
            var entity = new Entity("webresource", SelectedResource.Id);
            entity["content"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(EditorText));

            await client.UpdateAsync(entity, ct);
            _loadedText = EditorText;

            var syntaxWarning = IsScriptResource ? ValidateJavaScript() : null;
            Dispatcher.UIThread.Post(() =>
            {
                IsDirty = false;
                EditorStatus = syntaxWarning is null
                    ? "Saved. Click Publish to make changes live."
                    : $"Saved — but check syntax: {syntaxWarning}";
            });
            Log($"Saved: {SelectedResource.Name}");
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() => EditorStatus = $"Save failed: {ex.Message}");
            Log($"Save error for {SelectedResource.Name}: {ex.Message}");
        }
    }

    private bool CanSave() => SelectedResource is not null && IsDirty && IsTextResource;

    // ── Publish ──────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanPublish))]
    private async Task PublishResourceAsync(CancellationToken ct)
    {
        if (SelectedResource is null) return;

        // Refuse to publish JavaScript that doesn't parse — a broken script
        // takes down every form it is registered on.
        if (IsScriptResource && ValidateJavaScript() is { } syntaxError)
        {
            EditorStatus = $"Publish blocked — syntax error: {syntaxError}";
            Log($"Publish blocked for {SelectedResource.Name}: {syntaxError}");
            return;
        }

        // Publishing makes the change live for every user of this
        // environment — confirm before doing it.
        var env = _connectionProvider.ActiveConnectionName ?? "the connected environment";
        var confirmed = await ConfirmOrProceedAsync(
            "Publish web resource?",
            $"This makes '{SelectedResource.Name}' live for all users of {env}.\n\nContinue?",
            "Publish");
        if (!confirmed)
        {
            EditorStatus = "Publish cancelled.";
            return;
        }

        // Auto-save unsaved changes first
        if (IsDirty)
            await SaveResourceAsync(ct);

        EditorStatus = "Publishing…";
        try
        {
            var client = await _connectionProvider.GetActiveConnectionAsync(ct);

            var request = new OrganizationRequest("PublishXml")
            {
                ["ParameterXml"] =
                    $"<importexportxml><webresources><webresource>{SelectedResource.Id:B}</webresource></webresources></importexportxml>"
            };

            await client.ExecuteAsync(request, ct);

            Dispatcher.UIThread.Post(() => EditorStatus = "Published successfully.");
            Log($"Published: {SelectedResource.Name}");
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() => EditorStatus = $"Publish failed: {ex.Message}");
            Log($"Publish error for {SelectedResource.Name}: {ex.Message}");
        }
    }

    private bool CanPublish() => SelectedResource is not null && IsTextResource;

    // ── Delete ───────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteResourceAsync(CancellationToken ct)
    {
        if (SelectedResource is not { } target) return;

        var confirmed = await ConfirmOrProceedAsync(
            "Delete web resource?",
            $"'{target.Name}' will be permanently deleted from " +
            $"{_connectionProvider.ActiveConnectionName ?? "the environment"}.\n\n" +
            "This can't be undone. If the resource is still referenced by a form, " +
            "ribbon or site map, the delete will be rejected.",
            "Delete", destructive: true);
        if (!confirmed)
        {
            EditorStatus = "Delete cancelled.";
            return;
        }

        // Stash a copy of the current content first, so a wrong delete is
        // at least recoverable from disk.
        BackupContent(target, _loadedText);

        EditorStatus = $"Deleting {target.Name}…";
        try
        {
            var client = await _connectionProvider.GetActiveConnectionAsync(ct);
            await client.DeleteAsync("webresource", target.Id, ct);

            Dispatcher.UIThread.Post(() =>
            {
                _allItems.RemoveAll(i => i.Id == target.Id);
                _membership.Remove(target.Id);
                SelectedResource = null;   // close the editor
                RebuildTree();
                EditorStatus = $"Deleted '{target.Name}'.";
            });
            Log($"Deleted web resource '{target.Name}'.");
        }
        catch (Exception ex)
        {
            // Dependency errors land here — surface them, don't swallow.
            Dispatcher.UIThread.Post(() => EditorStatus = $"Delete failed: {ex.Message}");
            Log($"Delete failed for '{target.Name}': {ex.Message}");
        }
    }

    // Managed resources can't be deleted; don't offer it for them.
    private bool CanDelete() => SelectedResource is { IsManaged: false };

    // ── Backup ───────────────────────────────────────────────────────

    private static readonly string _backupDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".local", "share", "versekit", "backups");

    /// <summary>Writes the pre-save server content to a timestamped file so
    /// an overwrite can be recovered. Best effort — never blocks the save.</summary>
    private static void BackupContent(WebResourceItem resource, string content)
    {
        if (string.IsNullOrEmpty(content)) return;
        try
        {
            Directory.CreateDirectory(_backupDir);
            var safe = string.Concat(resource.Name.Split(Path.GetInvalidFileNameChars()));
            var path = Path.Combine(_backupDir, $"{safe}-{DateTime.Now:yyyyMMdd-HHmmss}.bak");
            File.WriteAllText(path, content);
            Log($"Backed up previous content of {resource.Name} → {path}");
        }
        catch (Exception ex)
        {
            Log($"Backup failed for {resource.Name}: {ex.Message}");
        }
    }

    // ── Logging ──────────────────────────────────────────────────────

    private static readonly string _logPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".local", "share", "versekit", "logs",
        $"plugin-webresources-{DateTime.Now:yyyyMMdd}.log");

    private static void Log(string message)
    {
        try { File.AppendAllText(_logPath,
            $"{DateTime.Now:HH:mm:ss.fff}  {message}{Environment.NewLine}"); }
        catch { /* best effort */ }
    }

}
