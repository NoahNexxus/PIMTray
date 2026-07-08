using System.Net;
using System.Text;
using PIMTray.Pim;
using Xunit;

namespace PIMTray.Tests;

public class PimServiceTests
{
    [Fact]
    public async Task GetEligibleRolesAsync_EscapesQuotesInUserObjectId_InODataFilter()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{ "value": [] }""", Encoding.UTF8, "application/json")
        });
        var service = new PimService(new HttpClient(handler));

        await service.GetEligibleRolesAsync("abc' or '1'='1", "Prod");

        var url = handler.Requests[0].RequestUri!.ToString();
        // The literal quote must be doubled (OData escaping) then percent-encoded,
        // so a raw unescaped "'" must never appear in the query string.
        Assert.DoesNotContain("abc' or", url);
        Assert.Contains("abc%27%27", url);
    }

    [Fact]
    public async Task GetEligibleRolesAsync_ParsesResponse_AndTagsConnectionName()
    {
        var json = """
        {
          "value": [
            {
              "roleDefinitionId": "role-1",
              "directoryScopeId": "/",
              "roleDefinition": { "displayName": "Global Administrator" }
            }
          ]
        }
        """;
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
        var service = new PimService(new HttpClient(handler));

        var roles = await service.GetEligibleRolesAsync("user-1", "Sandbox");

        var role = Assert.Single(roles);
        Assert.Equal("Global Administrator", role.RoleDisplayName);
        Assert.Equal("Directory", role.ScopeDescription);
        Assert.Equal("Sandbox", role.ConnectionName);
    }

    [Fact]
    public async Task GetEligibleRolesAsync_Throws_OnFailureResponse()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("insufficient privileges")
        });
        var service = new PimService(new HttpClient(handler));

        await Assert.ThrowsAsync<PimApiException>(() => service.GetEligibleRolesAsync("user-1", "Prod"));
    }

    [Fact]
    public async Task GetEligibleRolesAsync_SurfacesGraphErrorCodeAndMessage_ButNotRawBody()
    {
        const string errorBody = """
        { "error": { "code": "Authorization_RequestDenied", "message": "Insufficient privileges." } }
        """;
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent(errorBody)
        });
        var service = new PimService(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<PimApiException>(
            () => service.GetEligibleRolesAsync("user-1", "Prod"));

        // The parsed code/message is user-facing; the raw JSON body is not dumped into the message.
        Assert.Contains("Authorization_RequestDenied", ex.Message);
        Assert.Contains("Insufficient privileges.", ex.Message);
        Assert.DoesNotContain("\"error\"", ex.Message);
        // The full body remains available for diagnostics.
        Assert.Equal(errorBody, ex.ResponseBody);
    }

    [Fact]
    public async Task GetEligibleRolesAsync_DoesNotSurfaceNonJsonBody_InMessage()
    {
        const string htmlBody = "<html><body>Gateway error 502 - internal-host.example</body></html>";
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent(htmlBody)
        });
        var service = new PimService(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<PimApiException>(
            () => service.GetEligibleRolesAsync("user-1", "Prod"));

        // A non-JSON body (e.g. an HTML error page) must not be echoed into the message.
        Assert.DoesNotContain("internal-host.example", ex.Message);
        Assert.DoesNotContain("<html>", ex.Message);
        Assert.Equal(htmlBody, ex.ResponseBody);
    }

    [Fact]
    public async Task ActivateRoleAsync_PostsExpectedBody()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Created));
        var service = new PimService(new HttpClient(handler));
        var role = new EligibleRole("role-1", "Global Administrator", "/", "Directory", "Prod");

        await service.ActivateRoleAsync("user-1", role, "on-call incident", TimeSpan.FromHours(2));

        var body = handler.RequestBodies[0];
        Assert.Contains("\"action\":\"selfActivate\"", body);
        Assert.Contains("\"principalId\":\"user-1\"", body);
        Assert.Contains("\"duration\":\"PT2H\"", body);
        Assert.Contains("\"justification\":\"on-call incident\"", body);
    }
}
