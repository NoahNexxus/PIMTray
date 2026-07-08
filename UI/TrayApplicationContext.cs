using PIMTray.Connections;
using PIMTray.Pim;

namespace PIMTray.UI;

public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _openWindowItem;
    private readonly ToolStripMenuItem _accountsRoot;
    private readonly ToolStripMenuItem _activateRoot;
    private readonly ToolStripMenuItem _refreshItem;
    private readonly ToolStripMenuItem _exitItem;
    private readonly ToolStripMenuItem _aboutItem;
    private readonly MainForm _mainForm;

    private AppConfig _cfg = null!;
    private List<ConnectionSession> _connections = new();

    public TrayApplicationContext()
    {
        _menu = new ContextMenuStrip();
        _statusItem = new ToolStripMenuItem("Loading...") { Enabled = false };
        _openWindowItem = new ToolStripMenuItem("Open PIM Tray", null, (_, _) => ShowMainWindow());
        _accountsRoot = new ToolStripMenuItem("Accounts");
        _activateRoot = new ToolStripMenuItem("Eligible roles");
        _refreshItem = new ToolStripMenuItem("Refresh roles", null, async (_, _) => await RefreshAllAsync()) { Enabled = false };
        _exitItem = new ToolStripMenuItem("Exit", null, (_, _) => ExitApp());
        _aboutItem = new ToolStripMenuItem("About...", null, (_, _) => ShowAbout());

        _menu.Items.AddRange(new ToolStripItem[]
        {
            _statusItem,
            new ToolStripSeparator(),
            _openWindowItem,
            new ToolStripSeparator(),
            _accountsRoot,
            _activateRoot,
            _refreshItem,
            new ToolStripSeparator(),
            _exitItem,
            _aboutItem
        });

        _icon = new NotifyIcon
        {
            Icon = AppIcon.Load(),
            Text = "PIM Tray - loading...",
            Visible = true,
            ContextMenuStrip = _menu
        };
        _icon.MouseClick += OnIconClick;
        _icon.MouseDoubleClick += (_, _) => ShowMainWindow();

        _mainForm = new MainForm();
        _mainForm.AccountSignInRequested += id => _ = SignInAsync(id);
        _mainForm.AccountSignOutRequested += id => _ = SignOutAsync(id);
        _mainForm.ManageAccountsRequested += ShowManageAccounts;
        _mainForm.RefreshRequested += () => _ = RefreshAllAsync();
        _mainForm.ActivateRolesRequested += roles => _ = ActivateRolesAsync(roles);
        _mainForm.AboutRequested += ShowAbout;

        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            _cfg = AppConfig.Load();
            _connections = _cfg.Connections.Select(CreateSession).ToList();
            RebuildTrayMenu();
            PushStateToMainForm();

            var restoreTasks = _connections.Select(async conn =>
            {
                try { await conn.TryRestoreAsync(); }
                catch (Exception ex) { ShowError($"Failed to restore session for {conn.Name}", ex); }
            });
            await Task.WhenAll(restoreTasks);
        }
        catch (Exception ex)
        {
            ShowError("PIM Tray failed to start", ex);
        }
    }

    private ConnectionSession CreateSession(ConnectionConfig cfg)
    {
        var session = new ConnectionSession(cfg);
        session.Changed += OnConnectionChanged;
        return session;
    }

    private void OnConnectionChanged()
    {
        RebuildTrayMenu();
        PushStateToMainForm();
    }

    private async Task SignInAsync(string connectionId)
    {
        var session = _connections.FirstOrDefault(c => c.Config.Id == connectionId);
        if (session is null) return;

        try
        {
            await session.SignInAsync();
            if (session.IsSignedIn)
                ShowInfo("Signed in", $"Signed in to {session.Name} as {session.Session!.Username}");
        }
        catch (Exception ex)
        {
            ShowError($"Sign-in to {session.Name} failed", ex);
        }
    }

    private async Task SignOutAsync(string connectionId)
    {
        var session = _connections.FirstOrDefault(c => c.Config.Id == connectionId);
        if (session is null) return;

        try
        {
            await session.SignOutAsync();
        }
        catch (Exception ex)
        {
            ShowError($"Sign-out from {session.Name} failed", ex);
        }
    }

    private async Task RefreshAllAsync()
    {
        var tasks = _connections.Where(c => c.IsSignedIn).Select(async conn =>
        {
            try { await conn.RefreshRolesAsync(); }
            catch (Exception ex) { ShowError($"Failed to load eligible roles for {conn.Name}", ex); }
        });
        await Task.WhenAll(tasks);
    }

    private async Task ActivateRolesAsync(IReadOnlyList<EligibleRole> roles)
    {
        if (roles.Count == 0) return;

        using var form = new ActivateRoleForm(roles, _cfg.Pim.DurationOptionsHours, _cfg.Pim.DefaultDurationHours);
        if (form.ShowDialog() != DialogResult.OK) return;

        var ok = new List<string>();
        var failures = new List<(string Role, string Error)>();

        foreach (var role in roles)
        {
            var session = _connections.FirstOrDefault(c =>
                c.IsSignedIn && c.Name.Equals(role.ConnectionName, StringComparison.OrdinalIgnoreCase));

            if (session is null)
            {
                failures.Add((Describe(role), $"Not signed in to {role.ConnectionName}"));
                continue;
            }

            try
            {
                await session.ActivateRoleAsync(role, form.Justification, form.Duration);
                ok.Add(Describe(role));
            }
            catch (Exception ex)
            {
                failures.Add((Describe(role), ex.Message));
            }
        }

        ReportActivationResult(ok, failures, form.Duration);
    }

    private static string Describe(EligibleRole role) => $"{role.RoleDisplayName} ({role.ConnectionName})";

    private void ReportActivationResult(
        IReadOnlyList<string> succeeded,
        IReadOnlyList<(string Role, string Error)> failed,
        TimeSpan duration)
    {
        var hours = $"{duration.TotalHours:0} hour(s)";

        if (failed.Count == 0)
        {
            var body = succeeded.Count == 1
                ? $"{succeeded[0]} for {hours}."
                : $"{succeeded.Count} roles activated for {hours}:\n - " + string.Join("\n - ", succeeded);
            ShowInfo("PIM activation requested", body);
            return;
        }

        if (succeeded.Count == 0)
        {
            var body = failed.Count == 1
                ? $"{failed[0].Role}: {failed[0].Error}"
                : string.Join("\n", failed.Select(f => $"{f.Role}: {f.Error}"));
            ShowError("PIM activation failed", new InvalidOperationException(body));
            return;
        }

        var mixedBody =
            $"OK ({succeeded.Count}): " + string.Join(", ", succeeded) + "\n" +
            $"Failed ({failed.Count}): " + string.Join("; ", failed.Select(f => $"{f.Role} - {f.Error}"));
        _icon.ShowBalloonTip(8000, "PIM activation - partial success", mixedBody, ToolTipIcon.Warning);
    }

    private void ShowManageAccounts()
    {
        using var form = new ManageAccountsForm(_connections.Select(c => c.Config));
        form.ShowDialog();
        if (form.ChangesMade)
            ReconcileConnections(form.Connections);
    }

    private void ReconcileConnections(List<ConnectionConfig> updated)
    {
        var updatedIds = updated.Select(c => c.Id).ToHashSet();

        foreach (var removed in _connections.Where(c => !updatedIds.Contains(c.Config.Id)).ToList())
        {
            _connections.Remove(removed);
            removed.Changed -= OnConnectionChanged;
            var toDispose = removed;
            _ = toDispose.SignOutAsync().ContinueWith(_ => toDispose.Dispose());
        }

        foreach (var cfg in updated)
        {
            var existing = _connections.FirstOrDefault(c => c.Config.Id == cfg.Id);
            if (existing is not null)
            {
                existing.UpdateConfig(cfg);
            }
            else
            {
                var session = CreateSession(cfg);
                _connections.Add(session);
                _ = session.TryRestoreAsync().ContinueWith(t =>
                {
                    if (t.Exception is not null) ShowError($"Failed to restore session for {session.Name}", t.Exception);
                }, TaskScheduler.FromCurrentSynchronizationContext());
            }
        }

        _cfg.Connections = updated;
        AppConfig.Save(_cfg);
        RebuildTrayMenu();
        PushStateToMainForm();
    }

    private void RebuildTrayMenu()
    {
        var signedInCount = _connections.Count(c => c.IsSignedIn);

        _statusItem.Text = _connections.Count == 0
            ? "No accounts configured"
            : $"{signedInCount} of {_connections.Count} accounts signed in";

        _accountsRoot.DropDownItems.Clear();
        foreach (var conn in _connections)
        {
            var id = conn.Config.Id;
            var label = conn.IsSignedIn ? $"{conn.Name} ({conn.Session!.Username})" : $"{conn.Name} (not signed in)";
            var item = new ToolStripMenuItem(label);
            item.DropDownItems.Add(new ToolStripMenuItem("Sign in", null, async (_, _) => await SignInAsync(id))
            { Enabled = !conn.IsSignedIn });
            item.DropDownItems.Add(new ToolStripMenuItem("Sign out", null, async (_, _) => await SignOutAsync(id))
            { Enabled = conn.IsSignedIn });
            _accountsRoot.DropDownItems.Add(item);
        }
        _accountsRoot.DropDownItems.Add(new ToolStripSeparator());
        _accountsRoot.DropDownItems.Add(new ToolStripMenuItem("Manage accounts...", null, (_, _) => ShowManageAccounts()));

        var allRoles = _connections.Where(c => c.IsSignedIn).SelectMany(c => c.Roles).ToList();
        _activateRoot.DropDownItems.Clear();
        if (allRoles.Count == 0)
        {
            var message = signedInCount == 0 ? "Sign in to see eligible roles" : "No eligible roles";
            _activateRoot.DropDownItems.Add(new ToolStripMenuItem(message) { Enabled = false });
        }
        else
        {
            foreach (var entry in RoleGrouping.BuildEntries(allRoles))
            {
                var captured = entry.Roles;
                _activateRoot.DropDownItems.Add(new ToolStripMenuItem(entry.Label, null,
                    async (_, _) => await ActivateRolesAsync(captured)));
            }
        }

        _refreshItem.Enabled = signedInCount > 0;

        var tooltip = signedInCount > 0
            ? $"PIM Tray - {signedInCount}/{_connections.Count} signed in"
            : "PIM Tray - not signed in";
        _icon.Text = tooltip.Length <= 127 ? tooltip : tooltip[..127];
    }

    private void PushStateToMainForm()
    {
        _mainForm.SetConnections(_connections);
        _mainForm.SetRoles(_connections.Where(c => c.IsSignedIn).SelectMany(c => c.Roles).ToList());
    }

    private void ShowMainWindow() => _mainForm.ShowAndFocus();

    private void OnIconClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
            ShowMainWindow();
    }

    private static void ShowAbout()
    {
        using var dlg = new AboutForm();
        dlg.ShowDialog();
    }

    private void ShowInfo(string title, string message)
        => _icon.ShowBalloonTip(5000, title, message, ToolTipIcon.Info);

    private void ShowError(string title, Exception ex)
        => _icon.ShowBalloonTip(8000, title, ex.Message, ToolTipIcon.Error);

    private void ExitApp()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _mainForm.RequestRealExit();
        foreach (var conn in _connections) conn.Dispose();
        ExitThread();
    }
}
