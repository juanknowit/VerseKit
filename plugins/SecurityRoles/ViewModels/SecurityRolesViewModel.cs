using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SecurityRoles.Models;
using VerseKit.PluginSdk;

namespace SecurityRoles.ViewModels;

public sealed partial class SecurityRolesViewModel : ObservableObject
{
    private readonly IConnectionProvider _connectionProvider;

    // Full unfiltered set, cached so the filter is instant.
    private List<RoleItem> _allRoles = [];

    public ObservableCollection<RoleItem> Roles { get; } = [];
    public ObservableCollection<RoleMemberItem> Members { get; } = [];

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isMembersLoading;
    [ObservableProperty] private string _statusMessage = "Connect to an environment to browse security roles.";
    [ObservableProperty] private string _filterText = string.Empty;
    [ObservableProperty] private string _membersStatus = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRoleSelected))]
    private RoleItem? _selectedRole;

    public bool IsRoleSelected => SelectedRole is not null;

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
                    Roles.Clear();
                    Members.Clear();
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
                     || r.Name.Contains(f, StringComparison.OrdinalIgnoreCase)
                     || r.BusinessUnit.Contains(f, StringComparison.OrdinalIgnoreCase)))
            Roles.Add(r);
        StatusMessage = $"{Roles.Count} of {_allRoles.Count} role(s).";
    }

    [RelayCommand]
    private async Task LoadRolesAsync(CancellationToken ct)
    {
        IsLoading = true;
        StatusMessage = "Loading security roles…";
        Members.Clear();
        SelectedRole = null;
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
        if (value is not null)
            _ = LoadMembersAsync(value, CancellationToken.None);
    }

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
}
