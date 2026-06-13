using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VerseKit.Core.Models;
using VerseKit.Core.Services;

namespace VerseKit.App.ViewModels;

public partial class ConnectionViewModel(
    ConnectionManager connectionManager,
    ILogger<ConnectionViewModel> logger) : ViewModelBase
{
    /// <summary>Raised after a successful save + connect so the sidebar profile list refreshes.</summary>
    public event Action? ProfileSaved;

    /// <summary>Raised after the edited profile is deleted so the shell can close the form.</summary>
    public event Action? ProfileDeleted;

    /// <summary>Set by the view to show a file picker for the certificate.
    /// Returns the chosen path, or null if cancelled.</summary>
    public Func<Task<string?>>? PickCertificateAsync { get; set; }

    private const string DefaultClientId = "51f81489-12ee-4a9e-aaae-a2591f45987d";

    // When editing an existing profile, holds its name at edit start so a
    // rename can delete the old profile file (and its Keychain secret).
    private string? _editingOriginalName;

    [ObservableProperty] private string _formTitle = "New Connection";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteProfileCommand))]
    [NotifyPropertyChangedFor(nameof(ShowReauthenticate))]
    private bool _isEditing;

    /// <summary>Interactive / device-code methods have a cached user token
    /// that a re-authenticate can refresh; the app-only methods don't.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowReauthenticate))]
    private bool _isInteractiveMethod = true;

    /// <summary>Show the Re-authenticate button only when editing an
    /// interactive profile, and not while a connection is already running.</summary>
    public bool ShowReauthenticate => IsEditing && IsInteractiveMethod && !IsConnecting;

    /// <summary>Resets the form for creating a new connection.</summary>
    public void BeginNew()
    {
        _editingOriginalName = null;
        IsEditing = false;
        FormTitle = "New Connection";
        Name = string.Empty;
        EnvironmentUrl = string.Empty;
        ClientId = DefaultClientId;
        TenantId = null;
        ClientSecret = string.Empty;
        CertificatePath = string.Empty;
        Folder = string.Empty;
        SelectedAuthMethod = AuthMethod.Interactive;
        StatusMessage = null;
    }

    /// <summary>Pre-fills the form with an existing profile for editing.</summary>
    public void BeginEdit(ConnectionProfile profile)
    {
        _editingOriginalName = profile.Name;
        IsEditing = true;
        FormTitle = "Edit Connection";
        Name = profile.Name;
        EnvironmentUrl = profile.EnvironmentUrl;
        ClientId = profile.ClientId;
        TenantId = profile.TenantId;
        ClientSecret = string.Empty;
        CertificatePath = profile.CertificatePath ?? string.Empty;
        Folder = profile.Folder ?? string.Empty;
        SelectedAuthMethod = profile.AuthMethod;
        StatusMessage = null;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReauthenticateCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveProfileCommand))]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReauthenticateCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveProfileCommand))]
    private string _environmentUrl = string.Empty;

    // Microsoft public client ID commonly used for native Dataverse OAuth flows.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReauthenticateCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveProfileCommand))]
    private string _clientId = "51f81489-12ee-4a9e-aaae-a2591f45987d";

    [ObservableProperty] private string? _tenantId;
    [ObservableProperty] private string _clientSecret = string.Empty;
    [ObservableProperty] private string _certificatePath = string.Empty;
    [ObservableProperty] private string _folder = string.Empty;
    [ObservableProperty] private AuthMethod _selectedAuthMethod = AuthMethod.Interactive;
    [ObservableProperty] private string? _statusMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelConnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReauthenticateCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveProfileCommand))]
    [NotifyPropertyChangedFor(nameof(ShowReauthenticate))]
    private bool _isConnecting;

    [ObservableProperty] private bool _isClientSecretVisible;
    [ObservableProperty] private bool _isCertificateVisible;

    /// <summary>Whether the secret/password box is shown (ClientSecret value
    /// or Certificate password).</summary>
    [ObservableProperty] private bool _isSecretVisible;

    /// <summary>Contextual label for the secret box.</summary>
    [ObservableProperty] private string _secretLabel = "Client secret";

    private CancellationTokenSource? _connectCts;

    public IEnumerable<AuthMethod> AuthMethods => Enum.GetValues<AuthMethod>();

    partial void OnSelectedAuthMethodChanged(AuthMethod value)
    {
        IsClientSecretVisible = value == AuthMethod.ClientSecret;
        IsCertificateVisible = value == AuthMethod.Certificate;
        IsSecretVisible = value is AuthMethod.ClientSecret or AuthMethod.Certificate;
        SecretLabel = value == AuthMethod.Certificate ? "Certificate password" : "Client secret";
        IsInteractiveMethod = value is AuthMethod.Interactive or AuthMethod.DeviceCode;
    }

    [RelayCommand]
    private async Task BrowseCertificateAsync()
    {
        if (PickCertificateAsync is null) return;
        var path = await PickCertificateAsync();
        if (!string.IsNullOrWhiteSpace(path))
            CertificatePath = path;
    }

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private Task ConnectAsync() => DoConnectAsync(forceReauth: false);

    /// <summary>Re-establish the connection with a fresh sign-in, bypassing the
    /// cached token (account picker). Available when editing an interactive
    /// profile — e.g. after the token was revoked or to switch account.</summary>
    [RelayCommand(CanExecute = nameof(CanConnect))]
    private Task ReauthenticateAsync() => DoConnectAsync(forceReauth: true);

    private async Task DoConnectAsync(bool forceReauth)
    {
        // Cancellable + 2-minute cap so an abandoned browser login
        // can't leave the form stuck on "Connecting…" forever.
        var cts = _connectCts = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        IsConnecting = true;
        StatusMessage = forceReauth
            ? "Re-authenticating… (complete the sign-in in your browser)"
            : "Connecting… (complete the sign-in in your browser)";
        try
        {
            var profile = BuildProfile();

            await connectionManager.SaveProfileAsync(profile, ct: cts.Token);
            await CleanUpRenamedProfileAsync(profile.Name);
            await connectionManager.ConnectAsync(
                profile,
                secret: IsSecretVisible && !string.IsNullOrWhiteSpace(ClientSecret) ? ClientSecret : null,
                forceReauth: forceReauth,
                ct: cts.Token);

            StatusMessage = $"Connected to {profile.EnvironmentUrl}";
            ProfileSaved?.Invoke();
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Connection cancelled (or timed out after 2 minutes).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Connection failed: {ex.Message}";
            logger.LogError(ex, "Connection attempt failed for '{Name}'", Name);
        }
        finally
        {
            IsConnecting = false;
            if (_connectCts == cts) _connectCts = null;
            cts.Dispose();
        }
    }

    /// <summary>Saves the profile without connecting — for editing details
    /// like the tenant ID or URL without forcing a re-authentication.</summary>
    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task SaveProfileAsync()
    {
        try
        {
            var profile = BuildProfile();

            await connectionManager.SaveProfileAsync(
                profile,
                clientSecret: IsSecretVisible && !string.IsNullOrWhiteSpace(ClientSecret)
                    ? ClientSecret : null,
                ct: CancellationToken.None);
            await CleanUpRenamedProfileAsync(profile.Name);

            ProfileSaved?.Invoke();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save failed: {ex.Message}";
            logger.LogError(ex, "Profile save failed for '{Name}'", Name);
        }
    }

    private ConnectionProfile BuildProfile() => new()
    {
        Name = Name.Trim(),
        EnvironmentUrl = EnvironmentUrl.TrimEnd('/'),
        ClientId = ClientId,
        TenantId = string.IsNullOrWhiteSpace(TenantId) ? null : TenantId,
        AuthMethod = SelectedAuthMethod,
        CertificatePath = SelectedAuthMethod == AuthMethod.Certificate
            && !string.IsNullOrWhiteSpace(CertificatePath) ? CertificatePath.Trim() : null,
        Folder = string.IsNullOrWhiteSpace(Folder) ? null : Folder.Trim()
    };

    /// <summary>After saving under a new name while editing, removes the
    /// profile stored under the old name.</summary>
    private async Task CleanUpRenamedProfileAsync(string newName)
    {
        if (_editingOriginalName is not null && _editingOriginalName != newName)
            await connectionManager.DeleteProfileAsync(_editingOriginalName, CancellationToken.None);
        _editingOriginalName = newName;
    }

    [RelayCommand(CanExecute = nameof(IsConnecting))]
    private void CancelConnect() => _connectCts?.Cancel();

    /// <summary>Deletes the profile being edited (file + Keychain secret).</summary>
    [RelayCommand(CanExecute = nameof(IsEditing))]
    private async Task DeleteProfileAsync()
    {
        if (_editingOriginalName is null) return;
        try
        {
            // Deleting the active connection's profile also disconnects.
            if (connectionManager.ActiveConnectionName == _editingOriginalName)
                connectionManager.Disconnect();

            await connectionManager.DeleteProfileAsync(_editingOriginalName, CancellationToken.None);
            _editingOriginalName = null;
            IsEditing = false;
            ProfileDeleted?.Invoke();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Delete failed: {ex.Message}";
            logger.LogError(ex, "Profile delete failed for '{Name}'", Name);
        }
    }

    private bool CanConnect() =>
        !IsConnecting &&
        !string.IsNullOrWhiteSpace(Name) &&
        !string.IsNullOrWhiteSpace(EnvironmentUrl) &&
        !string.IsNullOrWhiteSpace(ClientId);
}
