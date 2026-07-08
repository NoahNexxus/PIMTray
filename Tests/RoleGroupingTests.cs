using PIMTray.Pim;
using Xunit;

namespace PIMTray.Tests;

public class RoleGroupingTests
{
    [Fact]
    public void BuildEntries_GroupsSameRoleName_AcrossTenants()
    {
        var roles = new[]
        {
            new EligibleRole("r1", "Global Administrator", "/", "Directory", "Prod"),
            new EligibleRole("r2", "Global Administrator", "/", "Directory", "Sandbox"),
            new EligibleRole("r3", "Billing Administrator", "/", "Directory", "Prod")
        };

        var entries = RoleGrouping.BuildEntries(roles);

        var crossTenant = Assert.Single(entries, e => e.IsCrossTenant);
        Assert.Equal(2, crossTenant.Roles.Count);
        Assert.Contains("Prod + Sandbox", crossTenant.Label);

        Assert.Equal(3, entries.Count(e => !e.IsCrossTenant));
    }

    [Fact]
    public void BuildEntries_DoesNotGroup_WhenRoleOnlyInOneTenant()
    {
        var roles = new[]
        {
            new EligibleRole("r1", "Billing Administrator", "/", "Directory", "Prod")
        };

        var entries = RoleGrouping.BuildEntries(roles);

        Assert.DoesNotContain(entries, e => e.IsCrossTenant);
        var entry = Assert.Single(entries);
        Assert.Equal("Billing Administrator — Prod", entry.Label);
    }

    [Fact]
    public void BuildEntries_IncludesScopeInLabel_ForNonDirectoryScope()
    {
        var roles = new[]
        {
            new EligibleRole("r1", "Helpdesk Administrator", "/administrativeUnits/au-1", "AU: EMEA", "Prod")
        };

        var entries = RoleGrouping.BuildEntries(roles);

        var entry = Assert.Single(entries);
        Assert.Equal("Helpdesk Administrator — Prod (AU: EMEA)", entry.Label);
    }
}
