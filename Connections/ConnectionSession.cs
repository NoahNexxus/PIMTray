using PIMTray.Auth;
using PIMTray.Pim;

namespace PIMTray.Connections;

// Wraps one signed-in (or signed-out) tenant connection: its own MSAL app/token cache,
// its own bearer token on its own HttpClient, and the eligible roles it last fetched.
// TrayApplicationContext/MainForm hold a list of these - one per configured tenant.
public sealed class ConnectionSession : IDisposable
{
    private readonly HttpClient _http = new();
    private readonly PimService _pim;
    private AuthService? _auth;
    private bool _busy;

    public ConnectionConfig Config { get; private set; }
    public AuthResult? Session { get; private set; }
    public IReadOnlyList<EligibleRole> Roles { get; private set; } = Array.Empty<EligibleRole>();

    public bool IsSignedIn => Session is not null;
    public string Name => Config.Name;

    public event Action? Changed;

    public ConnectionSession(ConnectionConfig config)
    {
        Config = config;
        _pim = new PimService(_http);
    }

    // Called after the Manage Accounts dialog edits this connection's settings.
    // If the tenant/app registration changed, the cached MSAL app is stale and must be rebuilt.
    public void UpdateConfig(ConnectionConfig config)
    {
        var authIdentityChanged = Config.TenantId != config.TenantId
            || Config.ClientId != config.ClientId
            || Config.RedirectUri != config.RedirectUri;

        Config = config;
        if (authIdentityChanged)
        {
            _auth = null;
            Session = null;
            Roles = Array.Empty<EligibleRole>();
        }
    }

    private async Task EnsureAuthAsync()
    {
        _auth ??= await AuthService.CreateAsync(Config);
    }

    public Task TryRestoreAsync(CancellationToken ct = default) => RunGuardedAsync(async () =>
    {
        await EnsureAuthAsync();
        var silent = await _auth!.TryGetTokenSilentAsync(ct);
        if (silent is null) return;

        Session = silent;
        _pim.SetAccessToken(silent.AccessToken);
        Roles = await _pim.GetEligibleRolesAsync(silent.UserObjectId, Name, ct);
    });

    public Task SignInAsync(CancellationToken ct = default) => RunGuardedAsync(async () =>
    {
        await EnsureAuthAsync();
        var result = await _auth!.SignInAsync(ct);
        Session = result;
        _pim.SetAccessToken(result.AccessToken);
        Roles = await _pim.GetEligibleRolesAsync(result.UserObjectId, Name, ct);
    });

    public Task SignOutAsync() => RunGuardedAsync(async () =>
    {
        if (_auth is not null) await _auth.SignOutAsync();
        Session = null;
        Roles = Array.Empty<EligibleRole>();
    });

    public Task RefreshRolesAsync(CancellationToken ct = default) => RunGuardedAsync(async () =>
    {
        if (Session is null) return;
        var fresh = await _auth!.TryGetTokenSilentAsync(ct) ?? Session;
        Session = fresh;
        _pim.SetAccessToken(fresh.AccessToken);
        Roles = await _pim.GetEligibleRolesAsync(fresh.UserObjectId, Name, ct);
    });

    public Task ActivateRoleAsync(EligibleRole role, string justification, TimeSpan duration, CancellationToken ct = default)
    {
        if (Session is null) throw new InvalidOperationException($"Not signed in to '{Name}'.");
        return _pim.ActivateRoleAsync(Session.UserObjectId, role, justification, duration, ct);
    }

    // Guards against overlapping sign-in/refresh calls on the same connection racing each
    // other - e.g. a double-clicked "Refresh" - which could otherwise let a stale response
    // overwrite a fresher one. Silently no-ops a call that arrives while one is already running.
    private async Task RunGuardedAsync(Func<Task> action)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            await action();
        }
        finally
        {
            _busy = false;
            Changed?.Invoke();
        }
    }

    public void Dispose() => _http.Dispose();
}
