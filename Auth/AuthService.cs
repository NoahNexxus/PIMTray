using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;

namespace PIMTray.Auth;

public sealed class AuthService
{
    private static readonly string[] Scopes =
    {
        "https://graph.microsoft.com/RoleEligibilitySchedule.Read.Directory",
        "https://graph.microsoft.com/RoleAssignmentSchedule.ReadWrite.Directory",
        "https://graph.microsoft.com/User.Read"
    };

    private readonly IPublicClientApplication _app;
    private IAccount? _account;

    private AuthService(IPublicClientApplication app)
    {
        _app = app;
    }

    public static async Task<AuthService> CreateAsync(ConnectionConfig cfg)
    {
        var app = PublicClientApplicationBuilder
            .Create(cfg.ClientId)
            .WithAuthority(AzureCloudInstance.AzurePublic, cfg.TenantId)
            .WithRedirectUri(cfg.RedirectUri)
            .WithClientName("PIMTray")
            .WithClientVersion("1.1.1")
            .Build();

        await AttachTokenCacheAsync(app, cfg.Id);
        return new AuthService(app);
    }

    private static async Task AttachTokenCacheAsync(IPublicClientApplication app, string connectionId)
    {
        var cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PIMTray");
        Directory.CreateDirectory(cacheDir);

        // Each connection gets its own cache file (keyed by stable connection Id, not the
        // user-editable Name) so signing in to one tenant never disturbs another's cached tokens.
        // The Id comes from appsettings.json, which a user could hand-edit, so it's sanitized
        // to filename-safe characters before use - otherwise a value like "..\\..\\x" could
        // steer the cache file outside cacheDir. Legitimate GUID ("N") ids pass through unchanged.
        var safeId = SanitizeForFileName(connectionId);
        var props = new StorageCreationPropertiesBuilder($"msal_cache_{safeId}.bin", cacheDir).Build();
        var helper = await MsalCacheHelper.CreateAsync(props);
        helper.RegisterCache(app.UserTokenCache);
    }

    private static string SanitizeForFileName(string value)
    {
        if (string.IsNullOrEmpty(value)) return "default";
        var chars = value.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray();
        return new string(chars);
    }

    public async Task<AuthResult> SignInAsync(CancellationToken ct = default)
    {
        var accounts = await _app.GetAccountsAsync();
        _account = accounts.FirstOrDefault();

        AuthenticationResult result;
        if (_account is not null)
        {
            try
            {
                result = await _app.AcquireTokenSilent(Scopes, _account).ExecuteAsync(ct);
            }
            catch (MsalUiRequiredException)
            {
                result = await AcquireInteractiveAsync(ct);
            }
        }
        else
        {
            result = await AcquireInteractiveAsync(ct);
        }

        _account = result.Account;
        return new AuthResult(result.AccessToken, result.Account.Username, GetUserObjectId(result));
    }

    public async Task<AuthResult?> TryGetTokenSilentAsync(CancellationToken ct = default)
    {
        var accounts = await _app.GetAccountsAsync();
        _account = accounts.FirstOrDefault();
        if (_account is null) return null;

        try
        {
            var result = await _app.AcquireTokenSilent(Scopes, _account).ExecuteAsync(ct);
            return new AuthResult(result.AccessToken, result.Account.Username, GetUserObjectId(result));
        }
        catch (MsalUiRequiredException)
        {
            return null;
        }
    }

    public async Task SignOutAsync()
    {
        var accounts = await _app.GetAccountsAsync();
        foreach (var a in accounts)
            await _app.RemoveAsync(a);
        _account = null;
    }

    private Task<AuthenticationResult> AcquireInteractiveAsync(CancellationToken ct)
    {
        return _app.AcquireTokenInteractive(Scopes)
            .WithUseEmbeddedWebView(false)
            .WithPrompt(Prompt.SelectAccount)
            .ExecuteAsync(ct);
    }

    private static string GetUserObjectId(AuthenticationResult r)
    {
        // Prefer the `oid` claim directly - it's what Graph's principalId actually matches.
        // AuthenticationResult.UniqueId normally mirrors it but can fall back to `sub` for
        // some guest/B2B token shapes, which would silently mismatch the PIM principal.
        var oid = r.ClaimsPrincipal?.FindFirst("oid")?.Value;
        return !string.IsNullOrEmpty(oid) ? oid : r.UniqueId;
    }
}

public sealed record AuthResult(string AccessToken, string Username, string UserObjectId);
