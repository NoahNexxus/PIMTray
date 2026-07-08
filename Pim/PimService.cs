using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PIMTray.Pim;

public sealed class PimService
{
    private const string GraphBase = "https://graph.microsoft.com/v1.0";
    private readonly HttpClient _http;
    private readonly Dictionary<string, string> _scopeNameCache = new();

    public PimService(HttpClient http)
    {
        _http = http;
    }

    public void SetAccessToken(string accessToken)
    {
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }

    public async Task<IReadOnlyList<EligibleRole>> GetEligibleRolesAsync(
        string userObjectId, string connectionName, CancellationToken ct = default)
    {
        var filterValue = Uri.EscapeDataString(EscapeODataLiteral(userObjectId));
        var url = $"{GraphBase}/roleManagement/directory/roleEligibilitySchedules"
                + $"?$filter=principalId eq '{filterValue}'"
                + "&$expand=roleDefinition";

        using var resp = await _http.GetAsync(url, ct);
        await EnsureSuccessAsync(resp, ct);

        var payload = await resp.Content.ReadFromJsonAsync<EligibilityResponse>(cancellationToken: ct)
                       ?? new EligibilityResponse();

        var list = new List<EligibleRole>();
        foreach (var s in payload.Value)
        {
            var name = s.RoleDefinition?.DisplayName ?? s.RoleDefinitionId ?? "(unknown role)";
            var scope = await DescribeScopeAsync(s.DirectoryScopeId, ct);
            list.Add(new EligibleRole(
                RoleDefinitionId: s.RoleDefinitionId ?? "",
                RoleDisplayName: name,
                DirectoryScopeId: s.DirectoryScopeId ?? "/",
                ScopeDescription: scope,
                ConnectionName: connectionName));
        }

        return list
            .OrderBy(r => r.RoleDisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.ScopeDescription, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task ActivateRoleAsync(
        string userObjectId,
        EligibleRole role,
        string justification,
        TimeSpan duration,
        CancellationToken ct = default)
    {
        var body = new ActivateRequest
        {
            Action = "selfActivate",
            PrincipalId = userObjectId,
            RoleDefinitionId = role.RoleDefinitionId,
            DirectoryScopeId = role.DirectoryScopeId,
            Justification = justification,
            ScheduleInfo = new ScheduleInfo
            {
                StartDateTime = DateTimeOffset.UtcNow,
                Expiration = new ExpirationInfo
                {
                    Type = "AfterDuration",
                    Duration = $"PT{(int)duration.TotalHours}H"
                }
            }
        };

        using var resp = await _http.PostAsJsonAsync(
            $"{GraphBase}/roleManagement/directory/roleAssignmentScheduleRequests",
            body,
            new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull },
            ct);

        await EnsureSuccessAsync(resp, ct);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        if (resp.IsSuccessStatusCode) return;
        var body = await resp.Content.ReadAsStringAsync(ct);
        throw new PimApiException((int)resp.StatusCode, resp.ReasonPhrase ?? "", body);
    }

    private static string EscapeODataLiteral(string value) => value.Replace("'", "''");

    private const string AdministrativeUnitPrefix = "/administrativeUnits/";

    private async Task<string> DescribeScopeAsync(string? scopeId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(scopeId) || scopeId == "/") return "Directory";

        if (_scopeNameCache.TryGetValue(scopeId, out var cached)) return cached;

        var result = scopeId;
        if (scopeId.StartsWith(AdministrativeUnitPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var auId = Uri.EscapeDataString(scopeId[AdministrativeUnitPrefix.Length..]);
            try
            {
                using var resp = await _http.GetAsync(
                    $"{GraphBase}/directory/administrativeUnits/{auId}?$select=displayName", ct);
                if (resp.IsSuccessStatusCode)
                {
                    var au = await resp.Content.ReadFromJsonAsync<DisplayNameOnly>(cancellationToken: ct);
                    if (!string.IsNullOrWhiteSpace(au?.DisplayName))
                        result = $"AU: {au.DisplayName}";
                }
            }
            catch
            {
                // best effort - fall back to the raw scope id if resolution fails
            }
        }

        _scopeNameCache[scopeId] = result;
        return result;
    }

    private sealed class EligibilityResponse
    {
        [JsonPropertyName("value")] public List<EligibilitySchedule> Value { get; set; } = new();
    }

    private sealed class EligibilitySchedule
    {
        [JsonPropertyName("roleDefinitionId")] public string? RoleDefinitionId { get; set; }
        [JsonPropertyName("directoryScopeId")] public string? DirectoryScopeId { get; set; }
        [JsonPropertyName("roleDefinition")] public RoleDefinition? RoleDefinition { get; set; }
    }

    private sealed class RoleDefinition
    {
        [JsonPropertyName("displayName")] public string? DisplayName { get; set; }
    }

    private sealed class DisplayNameOnly
    {
        [JsonPropertyName("displayName")] public string? DisplayName { get; set; }
    }

    private sealed class ActivateRequest
    {
        [JsonPropertyName("action")] public string Action { get; set; } = "";
        [JsonPropertyName("principalId")] public string PrincipalId { get; set; } = "";
        [JsonPropertyName("roleDefinitionId")] public string RoleDefinitionId { get; set; } = "";
        [JsonPropertyName("directoryScopeId")] public string DirectoryScopeId { get; set; } = "/";
        [JsonPropertyName("justification")] public string Justification { get; set; } = "";
        [JsonPropertyName("scheduleInfo")] public ScheduleInfo ScheduleInfo { get; set; } = new();
    }

    private sealed class ScheduleInfo
    {
        [JsonPropertyName("startDateTime")] public DateTimeOffset StartDateTime { get; set; }
        [JsonPropertyName("expiration")] public ExpirationInfo Expiration { get; set; } = new();
    }

    private sealed class ExpirationInfo
    {
        [JsonPropertyName("type")] public string Type { get; set; } = "AfterDuration";
        [JsonPropertyName("duration")] public string Duration { get; set; } = "PT1H";
    }
}

public sealed class PimApiException : Exception
{
    public int StatusCode { get; }
    public string ResponseBody { get; }

    public PimApiException(int statusCode, string reason, string body)
        : base(BuildMessage(statusCode, reason, body))
    {
        StatusCode = statusCode;
        ResponseBody = body;
    }

    // Surfaces the Graph-provided error code/message when the body is the standard
    // { "error": { "code", "message" } } envelope, instead of dumping the raw response
    // (which can contain principal/role identifiers, or an unrelated HTML error page)
    // into user-facing dialogs and balloon tips. The full body stays on ResponseBody
    // for diagnostics.
    private static string BuildMessage(int statusCode, string reason, string body)
    {
        var detail = TryExtractGraphError(body);
        return detail is null
            ? $"Graph PIM call failed: HTTP {statusCode} {reason}."
            : $"Graph PIM call failed: HTTP {statusCode} {reason} - {detail}";
    }

    private static string? TryExtractGraphError(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("error", out var err)) return null;

            var code = err.TryGetProperty("code", out var c) ? c.GetString() : null;
            var msg = err.TryGetProperty("message", out var m) ? m.GetString() : null;
            var combined = string.Join(": ",
                new[] { code, msg }.Where(s => !string.IsNullOrWhiteSpace(s)));
            return string.IsNullOrWhiteSpace(combined) ? null : Truncate(combined, 300);
        }
        catch (JsonException)
        {
            // Non-JSON body (e.g. an HTML error page) - don't surface it raw.
            return null;
        }
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..max] + "...";
}
