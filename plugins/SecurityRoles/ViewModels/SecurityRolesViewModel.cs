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
using SecurityRoles.Models;
using SecurityRoles.Services;
using VerseKit.PluginSdk;

namespace SecurityRoles.ViewModels;

public sealed partial class SecurityRolesViewModel : ObservableObject
{
    private const StringComparison OIC = StringComparison.OrdinalIgnoreCase;

    private readonly IConnectionProvider _connectionProvider;

    // Full unfiltered set, cached so the filter is instant.
    private List<RoleItem> _allRoles = [];

    // Per-environment privilege metadata (table → its privileges). Cached because
    // RetrieveAllEntities with Privileges is an expensive call; reused for every role.
    private List<EntityPrivMeta> _entityPrivMeta = [];

    private List<RolePrivilegeRow> _allPrivilegeRows = [];
    private Guid? _privilegesLoadedForRole;

    public ObservableCollection<RoleItem> Roles { get; } = [];
    public ObservableCollection<RoleMemberItem> Members { get; } = [];
    public ObservableCollection<RolePrivilegeRow> Privileges { get; } = [];

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isMembersLoading;
    [ObservableProperty] private bool _isPrivilegesLoading;
    [ObservableProperty] private string _statusMessage = "Connect to an environment to browse security roles.";
    [ObservableProperty] private string _filterText = string.Empty;
    [ObservableProperty] private string _membersStatus = string.Empty;
    [ObservableProperty] private string _privilegeFilterText = string.Empty;
    [ObservableProperty] private string _privilegesStatus = string.Empty;
    [ObservableProperty] private bool _showOnlyAssigned = true;

    /// <summary>0 = Members, 1 = Table permissions, 2 = Compare.</summary>
    [ObservableProperty] private int _detailTabIndex;

    // ── Compare two roles ──────────────────────────────────────────────
    private Dictionary<Guid, PrivilegeDepth> _roleMapA = new();
    private List<RoleComparisonRow> _allComparisonRows = [];

    public ObservableCollection<RoleItem> CompareCandidates { get; } = [];
    public ObservableCollection<RoleComparisonRow> Comparisons { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CompareRoleName))]
    private RoleItem? _compareRole;

    [ObservableProperty] private bool _isCompareLoading;
    [ObservableProperty] private string _compareFilterText = string.Empty;
    [ObservableProperty] private string _compareStatus = string.Empty;
    [ObservableProperty] private bool _showOnlyDifferences = true;
    [ObservableProperty] private bool _hasDifferences;

    public string SelectedRoleName => SelectedRole?.Title ?? "Role A";
    public string CompareRoleName => CompareRole?.Title ?? "Role B";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRoleSelected), nameof(SelectedRoleName))]
    private RoleItem? _selectedRole;

    public bool IsRoleSelected => SelectedRole is not null;

    /// <summary>
    /// Set by the view: prompts for a save path (suggested file name → chosen path or null).
    /// Lives here because the file picker needs the window's StorageProvider.
    /// </summary>
    public Func<string, Task<string?>>? PickSavePathAsync { get; set; }

    public SecurityRolesViewModel(IConnectionProvider connectionProvider)
    {
        _connectionProvider = connectionProvider;
        _connectionProvider.ConnectionChanged.Subscribe(client =>
            Dispatcher.UIThread.Post(() =>
            {
                if (client is { IsReady: true })
                {
                    _ = LoadRolesAsync(CancellationToken.None);
                }
                else
                {
                    _allRoles = [];
                    _entityPrivMeta = [];
                    Roles.Clear();
                    Members.Clear();
                    Privileges.Clear();
                    SelectedRole = null;
                    StatusMessage = "Connect to an environment to browse security roles.";
                }
            }));
    }

    // Filter on every keystroke — local and cheap, and instant feedback
    // matters more than saving work (DESIGN.md §7, "respond immediately").
    partial void OnFilterTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var f = FilterText?.Trim() ?? string.Empty;
        Roles.Clear();
        foreach (var r in _allRoles.Where(r => f.Length == 0
                     || r.Name.Contains(f, OIC)
                     || r.BusinessUnit.Contains(f, OIC)))
            Roles.Add(r);
        StatusMessage = $"{Roles.Count} of {_allRoles.Count} role(s).";
    }

    [RelayCommand]
    private async Task LoadRolesAsync(CancellationToken ct)
    {
        IsLoading = true;
        StatusMessage = "Loading security roles…";
        Members.Clear();
        Privileges.Clear();
        SelectedRole = null;
        _entityPrivMeta = []; // new/refreshed environment → drop cached metadata
        try
        {
            var client = await _connectionProvider.GetActiveConnectionAsync(ct);

            var query = new QueryExpression("role")
            {
                ColumnSet = new ColumnSet("roleid", "name", "businessunitid", "ismanaged", "modifiedon"),
                Orders = { new OrderExpression("name", OrderType.Ascending) },
                PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
            };

            var roles = new List<RoleItem>();
            while (true)
            {
                var page = await client.RetrieveMultipleAsync(query, ct);
                roles.AddRange(page.Entities.Select(e => new RoleItem
                {
                    RoleId = e.Id,
                    Name = e.GetAttributeValue<string>("name") ?? "",
                    BusinessUnit = e.GetAttributeValue<EntityReference>("businessunitid")?.Name ?? "",
                    IsManaged = e.GetAttributeValue<bool>("ismanaged"),
                    ModifiedOn = e.Contains("modifiedon")
                        ? e.GetAttributeValue<DateTime>("modifiedon")
                        : null
                }));

                if (!page.MoreRecords) break;
                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = page.PagingCookie;
            }

            roles = roles.OrderBy(r => r.Title, StringComparer.OrdinalIgnoreCase).ToList();

            Dispatcher.UIThread.Post(() =>
            {
                _allRoles = roles;
                ApplyFilter();
            });
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() => StatusMessage = $"Error loading roles: {ex.Message}");
        }
        finally
        {
            Dispatcher.UIThread.Post(() => IsLoading = false);
        }
    }

    partial void OnSelectedRoleChanged(RoleItem? value)
    {
        Members.Clear();
        MembersStatus = string.Empty;
        Privileges.Clear();
        _allPrivilegeRows = [];
        _privilegesLoadedForRole = null;
        PrivilegeFilterText = string.Empty;
        PrivilegesStatus = string.Empty;
        _roleMapA = new();

        // Reset the comparison whenever the primary role changes.
        CompareRole = null;
        Comparisons.Clear();
        _allComparisonRows = [];
        CompareStatus = string.Empty;
        HasDifferences = false;
        CompareCandidates.Clear();
        foreach (var r in _allRoles.Where(r => r.RoleId != value?.RoleId))
            CompareCandidates.Add(r);

        if (value is not null)
        {
            _ = LoadMembersAsync(value, CancellationToken.None);
            if (DetailTabIndex == 1)
                _ = LoadPrivilegesAsync(value, CancellationToken.None);
        }
    }

    partial void OnDetailTabIndexChanged(int value)
    {
        if (value == 1 && SelectedRole is { } role && _privilegesLoadedForRole != role.RoleId)
            _ = LoadPrivilegesAsync(role, CancellationToken.None);
    }

    // ── Members ────────────────────────────────────────────────────────

    private async Task LoadMembersAsync(RoleItem role, CancellationToken ct)
    {
        IsMembersLoading = true;
        try
        {
            var client = await _connectionProvider.GetActiveConnectionAsync(ct);

            // Users assigned to the role (via the systemuserroles intersect).
            var userQuery = new QueryExpression("systemuser")
            {
                ColumnSet = new ColumnSet("fullname", "domainname", "internalemailaddress", "isdisabled"),
                Orders = { new OrderExpression("fullname", OrderType.Ascending) }
            };
            var userLink = userQuery.AddLink("systemuserroles", "systemuserid", "systemuserid");
            userLink.LinkCriteria.AddCondition("roleid", ConditionOperator.Equal, role.RoleId);

            // Teams assigned to the role (via the teamroles intersect).
            var teamQuery = new QueryExpression("team")
            {
                ColumnSet = new ColumnSet("name"),
                Orders = { new OrderExpression("name", OrderType.Ascending) }
            };
            var teamLink = teamQuery.AddLink("teamroles", "teamid", "teamid");
            teamLink.LinkCriteria.AddCondition("roleid", ConditionOperator.Equal, role.RoleId);

            var userResult = await client.RetrieveMultipleAsync(userQuery, ct);
            var teamResult = await client.RetrieveMultipleAsync(teamQuery, ct);

            var members = new List<RoleMemberItem>();

            members.AddRange(teamResult.Entities.Select(e => new RoleMemberItem
            {
                Name = e.GetAttributeValue<string>("name") ?? "",
                Secondary = "Team",
                Kind = "TEAM"
            }));

            members.AddRange(userResult.Entities.Select(e => new RoleMemberItem
            {
                Name = e.GetAttributeValue<string>("fullname") ?? "",
                Secondary = e.GetAttributeValue<string>("internalemailaddress")
                            ?? e.GetAttributeValue<string>("domainname") ?? "",
                Kind = "USER",
                IsDisabled = e.GetAttributeValue<bool>("isdisabled")
            }));

            var userCount = userResult.Entities.Count;
            var teamCount = teamResult.Entities.Count;

            Dispatcher.UIThread.Post(() =>
            {
                Members.Clear();
                foreach (var m in members) Members.Add(m);
                MembersStatus = $"{userCount} user(s), {teamCount} team(s)";
            });
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() => MembersStatus = $"Error loading members: {ex.Message}");
        }
        finally
        {
            Dispatcher.UIThread.Post(() => IsMembersLoading = false);
        }
    }

    // ── Table permissions (privilege matrix) ───────────────────────────

    partial void OnPrivilegeFilterTextChanged(string value) => ApplyPrivilegeFilter();

    partial void OnShowOnlyAssignedChanged(bool value) => ApplyPrivilegeFilter();

    private void ApplyPrivilegeFilter()
    {
        var f = PrivilegeFilterText?.Trim() ?? string.Empty;

        var scoped = _allPrivilegeRows.Where(r => !ShowOnlyAssigned || r.HasAnyAccess).ToList();

        Privileges.Clear();
        foreach (var r in scoped.Where(r => f.Length == 0
                     || r.Table.Contains(f, OIC)
                     || r.LogicalName.Contains(f, OIC)))
            Privileges.Add(r);

        if (_allPrivilegeRows.Count > 0)
        {
            var denominator = ShowOnlyAssigned ? scoped.Count : _allPrivilegeRows.Count;
            var label = ShowOnlyAssigned ? "assigned table(s)" : "table(s)";
            PrivilegesStatus = $"{Privileges.Count} of {denominator} {label}";
        }
    }

    private async Task LoadPrivilegesAsync(RoleItem role, CancellationToken ct)
    {
        IsPrivilegesLoading = true;
        PrivilegesStatus = "Loading table permissions…";
        try
        {
            var client = await _connectionProvider.GetActiveConnectionAsync(ct);
            await EnsurePrivilegeMetadataAsync(client, ct);

            var roleMap = await GetRoleMapAsync(client, role.RoleId, ct);

            var rows = new List<RolePrivilegeRow>(_entityPrivMeta.Count);
            foreach (var em in _entityPrivMeta)
            {
                var supported = SupportedFor(em, roleMap);
                rows.Add(new RolePrivilegeRow
                {
                    Table = em.Table,
                    LogicalName = em.LogicalName,
                    Owner = em.Owner,
                    Create = BuildCell(supported, PrivilegeType.Create),
                    Read = BuildCell(supported, PrivilegeType.Read),
                    Write = BuildCell(supported, PrivilegeType.Write),
                    Delete = BuildCell(supported, PrivilegeType.Delete),
                    Append = BuildCell(supported, PrivilegeType.Append),
                    AppendTo = BuildCell(supported, PrivilegeType.AppendTo),
                    Assign = BuildCell(supported, PrivilegeType.Assign),
                    Share = BuildCell(supported, PrivilegeType.Share)
                });
            }

            rows = rows.OrderBy(r => r.Title, StringComparer.OrdinalIgnoreCase).ToList();

            Dispatcher.UIThread.Post(() =>
            {
                _allPrivilegeRows = rows;
                _privilegesLoadedForRole = role.RoleId;
                _roleMapA = roleMap;
                ApplyPrivilegeFilter();
            });
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() => PrivilegesStatus = $"Error loading permissions: {ex.Message}");
        }
        finally
        {
            Dispatcher.UIThread.Post(() => IsPrivilegesLoading = false);
        }
    }

    private async Task EnsurePrivilegeMetadataAsync(ServiceClient client, CancellationToken ct)
    {
        if (_entityPrivMeta.Count > 0) return;

        var response = (RetrieveAllEntitiesResponse)await client.ExecuteAsync(
            new RetrieveAllEntitiesRequest
            {
                EntityFilters = EntityFilters.Entity | EntityFilters.Privileges,
                RetrieveAsIfPublished = true
            }, ct);

        var list = new List<EntityPrivMeta>();
        foreach (var m in response.EntityMetadata)
        {
            var privs = (m.Privileges ?? [])
                .Where(p => p.PrivilegeType != PrivilegeType.None)
                .Select(p => (p.PrivilegeId, p.PrivilegeType))
                .ToList();
            if (privs.Count == 0) continue;

            list.Add(new EntityPrivMeta(
                m.DisplayName?.UserLocalizedLabel?.Label ?? m.LogicalName ?? "",
                m.LogicalName ?? "",
                OwnerLabel(m.OwnershipType),
                privs));
        }

        _entityPrivMeta = list;
    }

    private static AccessCell BuildCell(IReadOnlyDictionary<PrivilegeType, PrivilegeDepth?> supported, PrivilegeType type)
    {
        if (!supported.TryGetValue(type, out var depth))
            return AccessCell.NotApplicable;

        return depth switch
        {
            null => new AccessCell { Applicable = true, Short = "None", Full = "None", Color = "#F2F2F7", TextColor = "#AEAEB2" },
            PrivilegeDepth.Basic => new AccessCell { Applicable = true, Short = "User", Full = "User", Color = "#E7F7EC", TextColor = "#1E7A37" },
            PrivilegeDepth.Local => new AccessCell { Applicable = true, Short = "BU", Full = "Business Unit", Color = "#DBF1F6", TextColor = "#0B6B79" },
            PrivilegeDepth.Deep => new AccessCell { Applicable = true, Short = "P:C", Full = "Parent: Child Business Units", Color = "#E2EAFF", TextColor = "#1A40C2" },
            PrivilegeDepth.Global => new AccessCell { Applicable = true, Short = "Org", Full = "Organization", Color = "#D9EEDD", TextColor = "#10672A" },
            _ => AccessCell.NotApplicable
        };
    }

    private static string OwnerLabel(OwnershipTypes? ownership)
    {
        if (ownership is null) return "—";
        if (ownership.Value.HasFlag(OwnershipTypes.OrganizationOwned)) return "Org";
        if (ownership.Value.HasFlag(OwnershipTypes.BusinessOwned)) return "BU";
        if (ownership.Value.HasFlag(OwnershipTypes.UserOwned)) return "User/Team";
        return "—";
    }

    // ── Export to Excel ────────────────────────────────────────────────

    /// <summary>Exports whichever detail tab is currently open.</summary>
    [RelayCommand]
    private Task ExportAsync() => DetailTabIndex switch
    {
        1 => ExportPrivilegesAsync(),
        2 => ExportComparisonAsync(),
        _ => ExportMembersAsync(),
    };

    private async Task ExportComparisonAsync()
    {
        if (SelectedRole is not { } roleA || PickSavePathAsync is null) return;
        if (CompareRole is not { } roleB) { CompareStatus = "Pick a role to compare with first."; return; }
        if (Comparisons.Count == 0) { CompareStatus = "Nothing to export."; return; }

        var path = await PickSavePathAsync(SafeFileName($"{roleA.Title} vs {roleB.Title}") + ".xlsx");
        if (string.IsNullOrEmpty(path)) return;

        var snapshot = Comparisons.ToList(); // what's shown (respects the filters)
        try
        {
            await Task.Run(() => RoleExcelExporter.ExportComparison(path, roleA, roleB, snapshot));
            CompareStatus = $"Exported {snapshot.Count} row(s) to {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            CompareStatus = $"Export failed: {ex.Message}";
        }
    }

    // ── Compare two roles ──────────────────────────────────────────────

    private static readonly (PrivilegeType Type, string Label)[] PrivOrder =
    [
        (PrivilegeType.Create, "Create"), (PrivilegeType.Read, "Read"),
        (PrivilegeType.Write, "Write"), (PrivilegeType.Delete, "Delete"),
        (PrivilegeType.Append, "Append"), (PrivilegeType.AppendTo, "Append To"),
        (PrivilegeType.Assign, "Assign"), (PrivilegeType.Share, "Share"),
    ];

    partial void OnCompareRoleChanged(RoleItem? value)
    {
        Comparisons.Clear();
        _allComparisonRows = [];
        HasDifferences = false;
        CompareStatus = string.Empty;
        if (value is not null && SelectedRole is { } roleA)
            _ = LoadCompareAsync(roleA, value, CancellationToken.None);
    }

    partial void OnCompareFilterTextChanged(string value) => ApplyCompareFilter();
    partial void OnShowOnlyDifferencesChanged(bool value) => ApplyCompareFilter();

    private async Task LoadCompareAsync(RoleItem roleA, RoleItem roleB, CancellationToken ct)
    {
        IsCompareLoading = true;
        CompareStatus = $"Comparing with {roleB.Title}…";
        try
        {
            var client = await _connectionProvider.GetActiveConnectionAsync(ct);
            await EnsurePrivilegeMetadataAsync(client, ct);

            // Reuse role A's map when the Table permissions tab already loaded it.
            var mapA = _privilegesLoadedForRole == roleA.RoleId && _roleMapA.Count > 0
                ? _roleMapA
                : await GetRoleMapAsync(client, roleA.RoleId, ct);
            var mapB = await GetRoleMapAsync(client, roleB.RoleId, ct);
            var rows = BuildComparisonRows(mapA, mapB);

            Dispatcher.UIThread.Post(() =>
            {
                if (SelectedRole != roleA || CompareRole != roleB) return; // superseded
                _allComparisonRows = rows;
                ApplyCompareFilter();
            });
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() => CompareStatus = $"Error comparing: {ex.Message}");
        }
        finally
        {
            Dispatcher.UIThread.Post(() => IsCompareLoading = false);
        }
    }

    private List<RoleComparisonRow> BuildComparisonRows(
        Dictionary<Guid, PrivilegeDepth> mapA, Dictionary<Guid, PrivilegeDepth> mapB)
    {
        var rows = new List<RoleComparisonRow>();
        foreach (var em in _entityPrivMeta)
        {
            var sa = SupportedFor(em, mapA);
            var sb = SupportedFor(em, mapB);
            foreach (var (type, label) in PrivOrder)
            {
                if (!sa.ContainsKey(type)) continue; // table doesn't support this privilege
                var cellA = BuildCell(sa, type);
                var cellB = BuildCell(sb, type);
                rows.Add(new RoleComparisonRow
                {
                    Table = em.Table,
                    LogicalName = em.LogicalName,
                    Privilege = label,
                    CellA = cellA,
                    CellB = cellB,
                    Differs = cellA.Short != cellB.Short,
                });
            }
        }
        return rows
            .OrderBy(r => r.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => Array.FindIndex(PrivOrder, p => p.Label == r.Privilege))
            .ToList();
    }

    private void ApplyCompareFilter()
    {
        var f = CompareFilterText?.Trim() ?? string.Empty;
        Comparisons.Clear();
        foreach (var r in _allComparisonRows.Where(r => (!ShowOnlyDifferences || r.Differs)
                     && (f.Length == 0 || r.Table.Contains(f, OIC) || r.LogicalName.Contains(f, OIC))))
            Comparisons.Add(r);

        if (CompareRole is null) { CompareStatus = string.Empty; HasDifferences = false; return; }
        var diffRows = _allComparisonRows.Where(r => r.Differs).ToList();
        HasDifferences = diffRows.Count > 0;
        CompareStatus = diffRows.Count == 0
            ? "Identical table permissions — no differences."
            : $"{diffRows.Count} difference(s) across {diffRows.Select(r => r.LogicalName).Distinct().Count()} table(s)";
    }

    /// <summary>A role's privileges: privilege id → depth.</summary>
    private static async Task<Dictionary<Guid, PrivilegeDepth>> GetRoleMapAsync(
        ServiceClient client, Guid roleId, CancellationToken ct)
    {
        var response = (RetrieveRolePrivilegesRoleResponse)await client.ExecuteAsync(
            new RetrieveRolePrivilegesRoleRequest { RoleId = roleId }, ct);
        var map = new Dictionary<Guid, PrivilegeDepth>();
        foreach (var rp in response.RolePrivileges)
            map[rp.PrivilegeId] = rp.Depth;
        return map;
    }

    /// <summary>The depth granted per privilege type on a table (null = supported
    /// but not granted; absent = the table lacks that privilege).</summary>
    private static Dictionary<PrivilegeType, PrivilegeDepth?> SupportedFor(
        EntityPrivMeta em, Dictionary<Guid, PrivilegeDepth> map)
    {
        var supported = new Dictionary<PrivilegeType, PrivilegeDepth?>();
        foreach (var (id, type) in em.Privileges)
        {
            var depth = map.TryGetValue(id, out var d) ? (PrivilegeDepth?)d : null;
            // Keep a granted depth over an ungranted one if a type recurs.
            if (!supported.TryGetValue(type, out var existing) || existing is null)
                supported[type] = depth;
        }
        return supported;
    }

    [RelayCommand]
    private async Task ExportMembersAsync()
    {
        if (SelectedRole is not { } role || PickSavePathAsync is null) return;
        if (Members.Count == 0) { MembersStatus = "Nothing to export."; return; }

        var path = await PickSavePathAsync(SafeFileName($"{role.Title} - members") + ".xlsx");
        if (string.IsNullOrEmpty(path)) return;

        var snapshot = Members.ToList();
        try
        {
            await Task.Run(() => RoleExcelExporter.ExportMembers(path, role, snapshot));
            MembersStatus = $"Exported {snapshot.Count} member(s) to {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            MembersStatus = $"Export failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ExportPrivilegesAsync()
    {
        if (SelectedRole is not { } role || PickSavePathAsync is null) return;
        if (Privileges.Count == 0) { PrivilegesStatus = "Nothing to export."; return; }

        var path = await PickSavePathAsync(SafeFileName($"{role.Title} - table permissions") + ".xlsx");
        if (string.IsNullOrEmpty(path)) return;

        var snapshot = Privileges.ToList();
        try
        {
            await Task.Run(() => RoleExcelExporter.ExportPrivileges(path, role, snapshot));
            PrivilegesStatus = $"Exported {snapshot.Count} table(s) to {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            PrivilegesStatus = $"Export failed: {ex.Message}";
        }
    }

    private static string SafeFileName(string name)
    {
        foreach (var ch in Path.GetInvalidFileNameChars())
            name = name.Replace(ch, '_');
        return name;
    }

    private sealed record EntityPrivMeta(
        string Table,
        string LogicalName,
        string Owner,
        IReadOnlyList<(Guid Id, PrivilegeType Type)> Privileges);
}
