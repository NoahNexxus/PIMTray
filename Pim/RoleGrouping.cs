namespace PIMTray.Pim;

// Builds the menu entries shown in the tray and main-window "Roles" lists from the
// combined set of eligible roles across every signed-in tenant connection. Roles that
// share a display name across two or more tenants get one grouped entry so the same
// role can be activated in every matching tenant with a single click.
public static class RoleGrouping
{
    public sealed record RoleMenuEntry(string Label, IReadOnlyList<EligibleRole> Roles, bool IsCrossTenant);

    public static IReadOnlyList<RoleMenuEntry> BuildEntries(IReadOnlyList<EligibleRole> roles)
    {
        var entries = new List<RoleMenuEntry>();

        var crossTenantGroups = roles
            .GroupBy(r => r.RoleDisplayName, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Select(r => r.ConnectionName).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var group in crossTenantGroups)
        {
            var members = group
                .OrderBy(r => r.ConnectionName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var tenants = string.Join(" + ", members.Select(r => r.ConnectionName));
            entries.Add(new RoleMenuEntry($"{group.Key} — Activate in {tenants}", members, IsCrossTenant: true));
        }

        var individual = roles
            .OrderBy(r => r.ConnectionName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.RoleDisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.ScopeDescription, StringComparer.OrdinalIgnoreCase);

        foreach (var r in individual)
        {
            var label = r.ScopeDescription == "Directory"
                ? $"{r.RoleDisplayName} — {r.ConnectionName}"
                : $"{r.RoleDisplayName} — {r.ConnectionName} ({r.ScopeDescription})";
            entries.Add(new RoleMenuEntry(label, new[] { r }, IsCrossTenant: false));
        }

        return entries;
    }
}
