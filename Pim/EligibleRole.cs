namespace PIMTray.Pim;

public sealed record EligibleRole(
    string RoleDefinitionId,
    string RoleDisplayName,
    string DirectoryScopeId,
    string ScopeDescription,
    string ConnectionName);
