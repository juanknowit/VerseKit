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

    /// <summary>0 = Members, 1 = Table permissions.</summary>
    [ObservableProperty] private int _detailTabIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRoleSelected))]
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

    private void ApplyPrivilegeFilter()
    {
        var f = PrivilegeFilterText?.Trim() ?? string.Empty;
        Privileges.Clear();
        foreach (var r in _allPrivilegeRows.Where(r => f.Length == 0
                     || r.Table.Contains(f, OIC)
                     || r.LogicalName.Contains(f, OIC)))
            Privileges.Add(r);
        if (_allPrivilegeRows.Count > 0)
            PrivilegesStatus = $"{Privileges.Count} of {_allPrivilegeRows.Count} table(s)";
    }

    private async Task LoadPrivilegesAsync(RoleItem role, CancellationToken ct)
    {
        IsPrivilegesLoading = true;
        PrivilegesStatus = "Loading table permissions…";
        try
        {
            var client = await _connectionProvider.GetActiveConnectionAsync(ct);
            await EnsurePrivilegeMetadataAsync(client, ct);

            var response = (RetrieveRolePrivilegesRoleResponse)await client.ExecuteAsync(
                new RetrieveRolePrivilegesRoleRequest { RoleId = role.RoleId }, ct);

            var roleMap = new Dictionary<Guid, PrivilegeDepth>();
            foreach (var rp in response.RolePrivileges)
                roleMap[rp.PrivilegeId] = rp.Depth;

            var rows = new List<RolePrivilegeRow>(_entityPrivMeta.Count);
            foreach (var em in _entityPrivMeta)
            {
                var supported = new Dictionary<PrivilegeType, PrivilegeDepth?>();
                foreach (var (id, type) in em.Privileges)
                {
                    var depth = roleMap.TryGetValue(id, out var d) ? (PrivilegeDepth?)d : null;
                    // Keep a granted depth over an ungranted one if a type recurs.
                    if (!supported.TryGetValue(type, out var existing) || existing is null)
                        supported[type] = depth;
                }

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
