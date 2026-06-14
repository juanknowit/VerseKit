using System.Collections.ObjectModel;
using AccessChecker.Models;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using VerseKit.PluginSdk;

namespace AccessChecker.ViewModels;

public sealed partial class AccessCheckerViewModel : ObservableObject
{
    private const StringComparison OIC = StringComparison.OrdinalIgnoreCase;

    private readonly IConnectionProvider _connectionProvider;

    private List<UserItem> _allUsers = [];
    private List<EntityPrivMeta> _entityPrivMeta = [];
    private List<PrivilegeRow> _allPrivilegeRows = [];
    private Guid? _accessLoadedForUser;

    public ObservableCollection<UserItem> Users { get; } = [];
    public ObservableCollection<UserRoleItem> Roles { get; } = [];
    public ObservableCollection<PrivilegeRow> Privileges { get; } = [];

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isRolesLoading;
    [ObservableProperty] private bool _isAccessLoading;
    [ObservableProperty] private string _statusMessage = "Connect to an environment to check user access.";
    [ObservableProperty] private string _filterText = string.Empty;
    [ObservableProperty] private string _rolesStatus = string.Empty;
    [ObservableProperty] private string _privilegeFilterText = string.Empty;
    [ObservableProperty] private string _accessStatus = string.Empty;
    [ObservableProperty] private bool _showOnlyAssigned = true;

    /// <summary>0 = Effective access (matrix), 1 = Roles.</summary>
    [ObservableProperty] private int _detailTabIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUserSelected))]
    private UserItem? _selectedUser;

    public bool IsUserSelected => SelectedUser is not null;

    public AccessCheckerViewModel(IConnectionProvider connectionProvider)
    {
        _connectionProvider = connectionProvider;
        _connectionProvider.ConnectionChanged.Subscribe(client =>
            Dispatcher.UIThread.Post(() =>
            {
                if (client is { IsReady: true })
                {
                    _ = LoadUsersAsync(CancellationToken.None);
                }
                else
                {
                    _allUsers = [];
                    _entityPrivMeta = [];
                    Users.Clear();
                    Roles.Clear();
                    Privileges.Clear();
                    SelectedUser = null;
                    StatusMessage = "Connect to an environment to check user access.";
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
        Users.Clear();
        foreach (var u in _allUsers.Where(u => f.Length == 0
                     || u.Name.Contains(f, OIC)
                     || u.Secondary.Contains(f, OIC)))
            Users.Add(u);
        StatusMessage = $"{Users.Count} of {_allUsers.Count} user(s).";
    }

    [RelayCommand]
    private async Task LoadUsersAsync(CancellationToken ct)
    {
        IsLoading = true;
        StatusMessage = "Loading users…";
        Roles.Clear();
        Privileges.Clear();
        SelectedUser = null;
        _entityPrivMeta = [];
        try
        {
            var client = await _connectionProvider.GetActiveConnectionAsync(ct);

            var query = new QueryExpression("systemuser")
            {
                ColumnSet = new ColumnSet("systemuserid", "fullname", "internalemailaddress",
                                          "domainname", "isdisabled", "businessunitid"),
                Orders = { new OrderExpression("fullname", OrderType.Ascending) },
                PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
            };
            // Exclude application/non-interactive system accounts from the list.
            query.Criteria.AddCondition("accessmode", ConditionOperator.NotEqual, 4); // 4 = Non-interactive

            var users = new List<UserItem>();
            while (true)
            {
                var page = await client.RetrieveMultipleAsync(query, ct);
                users.AddRange(page.Entities.Select(e => new UserItem
                {
                    UserId = e.Id,
                    Name = e.GetAttributeValue<string>("fullname") ?? "",
                    Secondary = e.GetAttributeValue<string>("internalemailaddress")
                                ?? e.GetAttributeValue<string>("domainname") ?? "",
                    BusinessUnit = e.GetAttributeValue<EntityReference>("businessunitid")?.Name ?? "",
                    IsDisabled = e.GetAttributeValue<bool>("isdisabled")
                }));

                if (!page.MoreRecords) break;
                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = page.PagingCookie;
            }

            users = users.OrderBy(u => u.Title, StringComparer.OrdinalIgnoreCase).ToList();

            Dispatcher.UIThread.Post(() =>
            {
                _allUsers = users;
                ApplyFilter();
            });
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() => StatusMessage = $"Error loading users: {ex.Message}");
        }
        finally
        {
            Dispatcher.UIThread.Post(() => IsLoading = false);
        }
    }

    partial void OnSelectedUserChanged(UserItem? value)
    {
        Roles.Clear();
        RolesStatus = string.Empty;
        Privileges.Clear();
        _allPrivilegeRows = [];
        _accessLoadedForUser = null;
        PrivilegeFilterText = string.Empty;
        AccessStatus = string.Empty;

        if (value is not null)
        {
            _ = LoadRolesAsync(value, CancellationToken.None);
            _ = LoadAccessAsync(value, CancellationToken.None);
        }
    }

    // ── Roles (direct + via teams) ─────────────────────────────────────

    private async Task LoadRolesAsync(UserItem user, CancellationToken ct)
    {
        IsRolesLoading = true;
        try
        {
            var client = await _connectionProvider.GetActiveConnectionAsync(ct);

            // Direct roles (systemuserroles intersect).
            var directQuery = new QueryExpression("role")
            {
                ColumnSet = new ColumnSet("name", "businessunitid"),
                Orders = { new OrderExpression("name", OrderType.Ascending) }
            };
            var dLink = directQuery.AddLink("systemuserroles", "roleid", "roleid");
            dLink.LinkCriteria.AddCondition("systemuserid", ConditionOperator.Equal, user.UserId);

            // Roles via the teams the user belongs to (role → teamroles → team → teammembership).
            var teamQuery = new QueryExpression("role")
            {
                ColumnSet = new ColumnSet("name", "businessunitid"),
                Orders = { new OrderExpression("name", OrderType.Ascending) }
            };
            var trLink = teamQuery.AddLink("teamroles", "roleid", "roleid");
            var teamLink = trLink.AddLink("team", "teamid", "teamid");
            teamLink.Columns = new ColumnSet("name");
            teamLink.EntityAlias = "tm";
            var memLink = teamLink.AddLink("teammembership", "teamid", "teamid");
            memLink.LinkCriteria.AddCondition("systemuserid", ConditionOperator.Equal, user.UserId);

            var directResult = await client.RetrieveMultipleAsync(directQuery, ct);
            var teamResult = await client.RetrieveMultipleAsync(teamQuery, ct);

            var roles = new List<UserRoleItem>();
            foreach (var e in directResult.Entities)
                roles.Add(new UserRoleItem
                {
                    Name = e.GetAttributeValue<string>("name") ?? "",
                    Source = "Direct",
                    BusinessUnit = e.GetAttributeValue<EntityReference>("businessunitid")?.Name ?? ""
                });

            foreach (var e in teamResult.Entities)
                roles.Add(new UserRoleItem
                {
                    Name = e.GetAttributeValue<string>("name") ?? "",
                    Source = (e.GetAttributeValue<AliasedValue>("tm.name")?.Value as string) ?? "Team",
                    BusinessUnit = e.GetAttributeValue<EntityReference>("businessunitid")?.Name ?? ""
                });

            var direct = directResult.Entities.Count;
            var viaTeam = teamResult.Entities.Count;

            Dispatcher.UIThread.Post(() =>
            {
                Roles.Clear();
                foreach (var r in roles) Roles.Add(r);
                RolesStatus = $"{direct} direct, {viaTeam} via team(s)";
            });
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() => RolesStatus = $"Error loading roles: {ex.Message}");
        }
        finally
        {
            Dispatcher.UIThread.Post(() => IsRolesLoading = false);
        }
    }

    // ── Effective access matrix ────────────────────────────────────────

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
            var label = ShowOnlyAssigned ? "accessible table(s)" : "table(s)";
            AccessStatus = $"{Privileges.Count} of {denominator} {label}";
        }
    }

    private async Task LoadAccessAsync(UserItem user, CancellationToken ct)
    {
        IsAccessLoading = true;
        AccessStatus = "Computing effective access…";
        try
        {
            var client = await _connectionProvider.GetActiveConnectionAsync(ct);
            await EnsurePrivilegeMetadataAsync(client, ct);

            // Effective access = the union of every role the user holds, directly
            // and via team membership. Aggregate the deepest depth per privilege.
            var roleIds = await GetEffectiveRoleIdsAsync(client, user.UserId, ct);
            var userMap = new Dictionary<Guid, PrivilegeDepth>();
            foreach (var roleId in roleIds)
            {
                ct.ThrowIfCancellationRequested();
                var resp = (RetrieveRolePrivilegesRoleResponse)await client.ExecuteAsync(
                    new RetrieveRolePrivilegesRoleRequest { RoleId = roleId }, ct);
                foreach (var rp in resp.RolePrivileges)
                    if (!userMap.TryGetValue(rp.PrivilegeId, out var existing) || rp.Depth > existing)
                        userMap[rp.PrivilegeId] = rp.Depth;
            }

            var rows = new List<PrivilegeRow>(_entityPrivMeta.Count);
            foreach (var em in _entityPrivMeta)
            {
                var supported = new Dictionary<PrivilegeType, PrivilegeDepth?>();
                foreach (var (id, type) in em.Privileges)
                {
                    var depth = userMap.TryGetValue(id, out var d) ? (PrivilegeDepth?)d : null;
                    if (!supported.TryGetValue(type, out var existing) || existing is null)
                        supported[type] = depth;
                }

                rows.Add(new PrivilegeRow
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
                _accessLoadedForUser = user.UserId;
                ApplyPrivilegeFilter();
            });
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() => AccessStatus = $"Error computing access: {ex.Message}");
        }
        finally
        {
            Dispatcher.UIThread.Post(() => IsAccessLoading = false);
        }
    }

    /// <summary>The distinct role ids the user has — directly and via every team
    /// they belong to.</summary>
    private static async Task<List<Guid>> GetEffectiveRoleIdsAsync(
        ServiceClient client, Guid userId, CancellationToken ct)
    {
        var ids = new HashSet<Guid>();

        // Direct (systemuserroles intersect).
        var directQuery = new QueryExpression("role") { ColumnSet = new ColumnSet("roleid") };
        var dLink = directQuery.AddLink("systemuserroles", "roleid", "roleid");
        dLink.LinkCriteria.AddCondition("systemuserid", ConditionOperator.Equal, userId);
        foreach (var e in (await client.RetrieveMultipleAsync(directQuery, ct)).Entities)
            ids.Add(e.Id);

        // Via teams (role → teamroles → teammembership).
        var teamQuery = new QueryExpression("role") { ColumnSet = new ColumnSet("roleid") };
        var trLink = teamQuery.AddLink("teamroles", "roleid", "roleid");
        var memLink = trLink.AddLink("teammembership", "teamid", "teamid");
        memLink.LinkCriteria.AddCondition("systemuserid", ConditionOperator.Equal, userId);
        foreach (var e in (await client.RetrieveMultipleAsync(teamQuery, ct)).Entities)
            ids.Add(e.Id);

        return ids.ToList();
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

    private sealed record EntityPrivMeta(
        string Table,
        string LogicalName,
        string Owner,
        IReadOnlyList<(Guid Id, PrivilegeType Type)> Privileges);
}
