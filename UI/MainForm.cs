using PIMTray.Connections;
using PIMTray.Pim;

namespace PIMTray.UI;

public sealed class MainForm : Form
{
    public event Action<string>? AccountSignInRequested;
    public event Action<string>? AccountSignOutRequested;
    public event Action? ManageAccountsRequested;
    public event Action? RefreshRequested;
    public event Action<IReadOnlyList<EligibleRole>>? ActivateRolesRequested;
    public event Action? AboutRequested;

    private readonly ListView _list;
    private readonly Button _activateBtn;
    private readonly Button _refreshBtn;
    private readonly StatusStrip _status;
    private readonly ToolStripStatusLabel _statusLabel;

    private readonly ToolStripMenuItem _accountsMenu;
    private readonly ToolStripMenuItem _rolesMenu;
    private readonly ToolStripMenuItem _refreshMenu;
    private readonly ToolStripMenuItem _activateSelectedMenu;

    private IReadOnlyList<EligibleRole> _roles = Array.Empty<EligibleRole>();
    private bool _reallyExit;

    public MainForm()
    {
        Text = "PIM Tray";
        Icon = AppIcon.Load();
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(620, 380);
        MinimumSize = new Size(460, 300);
        Font = new Font("Segoe UI", 9F);
        ShowInTaskbar = true;

        var menu = new MenuStrip();

        var hideMenu = new ToolStripMenuItem("&Hide to tray", null, (_, _) => Hide());
        var exitMenu = new ToolStripMenuItem("E&xit", null, (_, _) => RequestExit());

        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) { Hide(); e.Handled = true; }
        };

        var fileMenu = new ToolStripMenuItem("&File");
        fileMenu.DropDownItems.AddRange(new ToolStripItem[] { hideMenu, exitMenu });

        _accountsMenu = new ToolStripMenuItem("&Accounts");
        // Populated dynamically by SetConnections().

        _refreshMenu = new ToolStripMenuItem("&Refresh", null, (_, _) => RefreshRequested?.Invoke())
        { ShortcutKeys = Keys.F5, Enabled = false };
        _activateSelectedMenu = new ToolStripMenuItem("&Activate selected...", null, (_, _) => ActivateSelected())
        { ShortcutKeys = Keys.Control | Keys.Enter, Enabled = false };

        var selectAllMenu = new ToolStripMenuItem("Select &all", null, (_, _) => SetAllChecked(true))
        { ShortcutKeys = Keys.Control | Keys.A };
        var clearMenu = new ToolStripMenuItem("&Clear selection", null, (_, _) => SetAllChecked(false));

        _rolesMenu = new ToolStripMenuItem("&Roles");
        _rolesMenu.DropDownItems.Add(_refreshMenu);
        _rolesMenu.DropDownItems.Add(_activateSelectedMenu);
        _rolesMenu.DropDownItems.Add(new ToolStripSeparator());
        _rolesMenu.DropDownItems.Add(selectAllMenu);
        _rolesMenu.DropDownItems.Add(clearMenu);
        _rolesMenu.DropDownItems.Add(new ToolStripSeparator());
        // Dynamic role entries get appended after the separator in SetRoles().

        var aboutMenu = new ToolStripMenuItem("&About...", null, (_, _) => AboutRequested?.Invoke());
        var helpMenu = new ToolStripMenuItem("&Help");
        helpMenu.DropDownItems.Add(aboutMenu);

        menu.Items.AddRange(new ToolStripItem[] { fileMenu, _accountsMenu, _rolesMenu, helpMenu });
        MainMenuStrip = menu;

        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = true,
            HideSelection = false,
            GridLines = false,
            CheckBoxes = true
        };
        _list.ItemChecked += (_, _) => UpdateActivateEnabled();
        _list.Columns.Add("Role", 220);
        _list.Columns.Add("Account", 120);
        _list.Columns.Add("Scope", 220);
        _list.DoubleClick += (_, _) => ActivateSelected();
        _list.SelectedIndexChanged += (_, _) => UpdateActivateEnabled();

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 44,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8)
        };

        _activateBtn = new Button
        {
            Text = "Activate...",
            Width = 110,
            Height = 28,
            Enabled = false
        };
        _activateBtn.Click += (_, _) => ActivateSelected();

        _refreshBtn = new Button
        {
            Text = "Refresh",
            Width = 90,
            Height = 28,
            Enabled = false
        };
        _refreshBtn.Click += (_, _) => RefreshRequested?.Invoke();

        buttonPanel.Controls.Add(_activateBtn);
        buttonPanel.Controls.Add(_refreshBtn);

        _statusLabel = new ToolStripStatusLabel("Not signed in") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        _status = new StatusStrip();
        _status.Items.Add(_statusLabel);

        Controls.Add(_list);
        Controls.Add(buttonPanel);
        Controls.Add(_status);
        Controls.Add(menu);

        FormClosing += OnFormClosing;
    }

    public void SetConnections(IReadOnlyList<ConnectionSession> connections)
    {
        _accountsMenu.DropDownItems.Clear();

        foreach (var conn in connections)
        {
            var id = conn.Config.Id;
            var label = conn.IsSignedIn ? $"{conn.Name} — {conn.Session!.Username}" : $"{conn.Name} — not signed in";
            var item = new ToolStripMenuItem(label);

            var signIn = new ToolStripMenuItem("Sign in", null, (_, _) => AccountSignInRequested?.Invoke(id))
            { Enabled = !conn.IsSignedIn };
            var signOut = new ToolStripMenuItem("Sign out", null, (_, _) => AccountSignOutRequested?.Invoke(id))
            { Enabled = conn.IsSignedIn };
            item.DropDownItems.Add(signIn);
            item.DropDownItems.Add(signOut);

            _accountsMenu.DropDownItems.Add(item);
        }

        _accountsMenu.DropDownItems.Add(new ToolStripSeparator());
        _accountsMenu.DropDownItems.Add(new ToolStripMenuItem("&Manage accounts...", null,
            (_, _) => ManageAccountsRequested?.Invoke()));

        var signedInCount = connections.Count(c => c.IsSignedIn);
        var refreshEnabled = signedInCount > 0;
        _refreshMenu.Enabled = refreshEnabled;
        _refreshBtn.Enabled = refreshEnabled;

        _statusLabel.Text = connections.Count == 0
            ? "No accounts configured - use Accounts > Manage accounts..."
            : signedInCount == 0
                ? "Not signed in to any account"
                : $"Signed in: {string.Join(", ", connections.Where(c => c.IsSignedIn).Select(c => $"{c.Name} ({c.Session!.Username})"))}";

        if (signedInCount == 0) SetRoles(Array.Empty<EligibleRole>());
    }

    public void SetRoles(IReadOnlyList<EligibleRole> roles)
    {
        _roles = roles;

        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var r in roles)
        {
            var item = new ListViewItem(r.RoleDisplayName) { Tag = r };
            item.SubItems.Add(r.ConnectionName);
            item.SubItems.Add(r.ScopeDescription);
            _list.Items.Add(item);
        }
        _list.EndUpdate();

        // Rebuild dynamic Roles menu entries (keep first 3 fixed items + separator + 2 select items + separator = 6).
        const int fixedItems = 6;
        while (_rolesMenu.DropDownItems.Count > fixedItems)
            _rolesMenu.DropDownItems.RemoveAt(fixedItems);

        if (roles.Count == 0)
        {
            _rolesMenu.DropDownItems.Add(new ToolStripMenuItem("(no eligible roles)") { Enabled = false });
        }
        else
        {
            foreach (var entry in RoleGrouping.BuildEntries(roles))
            {
                var captured = entry.Roles;
                var label = entry.IsCrossTenant ? $"&{entry.Label}" : entry.Label;
                _rolesMenu.DropDownItems.Add(new ToolStripMenuItem(label, null,
                    (_, _) => ActivateRolesRequested?.Invoke(captured)));
            }
        }

        UpdateActivateEnabled();
    }

    private void SetAllChecked(bool value)
    {
        _list.BeginUpdate();
        foreach (ListViewItem item in _list.Items) item.Checked = value;
        _list.EndUpdate();
    }

    public void ShowAndFocus()
    {
        if (!Visible) Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
    }

    public void RequestRealExit()
    {
        _reallyExit = true;
        Close();
    }

    private void UpdateActivateEnabled()
    {
        var count = GetTargetItems().Count;
        _activateBtn.Enabled = count > 0;
        _activateBtn.Text = count > 1 ? $"Activate ({count})..." : "Activate...";
        _activateSelectedMenu.Enabled = count > 0;
        _activateSelectedMenu.Text = count > 1 ? $"&Activate selected ({count})..." : "&Activate selected...";
    }

    private void ActivateSelected()
    {
        var roles = GetTargetItems()
            .Select(i => i.Tag)
            .OfType<EligibleRole>()
            .ToList();
        if (roles.Count == 0) return;
        ActivateRolesRequested?.Invoke(roles);
    }

    private IReadOnlyList<ListViewItem> GetTargetItems()
    {
        // Prefer checked items; fall back to highlighted selection if nothing is checked.
        var checkedItems = _list.CheckedItems.Cast<ListViewItem>().ToList();
        if (checkedItems.Count > 0) return checkedItems;
        return _list.SelectedItems.Cast<ListViewItem>().ToList();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_reallyExit) return;
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }
    }

    private void RequestExit()
    {
        // Tray context owns process lifetime - just signal it through closure.
        _reallyExit = true;
        Application.Exit();
    }
}
