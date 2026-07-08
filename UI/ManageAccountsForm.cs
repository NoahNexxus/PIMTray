namespace PIMTray.UI;

// Lets the user add, rename/reconfigure, or remove tenant connections (e.g. "Prod" / "Sandbox")
// without hand-editing appsettings.json. Operates on a working copy; the caller only applies
// the result if the dialog closes with OK.
public sealed class ManageAccountsForm : Form
{
    private readonly ListView _list;
    private readonly Button _addButton;
    private readonly Button _editButton;
    private readonly Button _removeButton;
    private readonly Button _closeButton;

    public List<ConnectionConfig> Connections { get; }
    public bool ChangesMade { get; private set; }

    public ManageAccountsForm(IEnumerable<ConnectionConfig> connections)
    {
        Connections = connections.Select(Clone).ToList();

        Text = "Manage accounts";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        Padding = new Padding(12);
        Font = new Font("Segoe UI", 9F);
        ClientSize = new Size(520, 320);

        var hintLabel = new Label
        {
            Text = "Each account below is a separate Entra ID tenant connection (e.g. Prod / Sandbox).",
            Location = new Point(12, 12),
            AutoSize = true
        };

        _list = new ListView
        {
            Location = new Point(12, 36),
            Size = new Size(496, 200),
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            GridLines = true
        };
        _list.Columns.Add("Name", 130);
        _list.Columns.Add("Tenant ID", 220);
        _list.Columns.Add("Client ID", 140);
        _list.SelectedIndexChanged += (_, _) => UpdateButtons();
        _list.DoubleClick += (_, _) => EditSelected();

        _addButton = new Button { Text = "Add...", Location = new Point(12, 244), Size = new Size(90, 28) };
        _addButton.Click += (_, _) => AddNew();

        _editButton = new Button { Text = "Edit...", Location = new Point(108, 244), Size = new Size(90, 28) };
        _editButton.Click += (_, _) => EditSelected();

        _removeButton = new Button { Text = "Remove", Location = new Point(204, 244), Size = new Size(90, 28) };
        _removeButton.Click += (_, _) => RemoveSelected();

        _closeButton = new Button
        {
            Text = "Close",
            Location = new Point(418, 244),
            Size = new Size(90, 28),
            DialogResult = DialogResult.OK
        };
        AcceptButton = _closeButton;
        CancelButton = _closeButton;

        Controls.AddRange(new Control[]
        {
            hintLabel, _list, _addButton, _editButton, _removeButton, _closeButton
        });

        Load += (_, _) => RefreshList();
    }

    private static ConnectionConfig Clone(ConnectionConfig c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        TenantId = c.TenantId,
        ClientId = c.ClientId,
        RedirectUri = c.RedirectUri
    };

    private void RefreshList()
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var c in Connections)
        {
            var item = new ListViewItem(c.Name) { Tag = c };
            item.SubItems.Add(c.TenantId);
            item.SubItems.Add(c.ClientId);
            _list.Items.Add(item);
        }
        _list.EndUpdate();
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var hasSelection = _list.SelectedItems.Count > 0;
        _editButton.Enabled = hasSelection;
        _removeButton.Enabled = hasSelection;
    }

    private ConnectionConfig? SelectedConnection =>
        _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as ConnectionConfig : null;

    private void AddNew()
    {
        using var form = new ConnectionEditForm("Add account", null);
        if (form.ShowDialog(this) != DialogResult.OK) return;

        if (Connections.Any(c => c.Name.Equals(form.ConnectionName, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, $"An account named \"{form.ConnectionName}\" already exists.",
                "Duplicate name", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Connections.Add(new ConnectionConfig
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = form.ConnectionName,
            TenantId = form.TenantId,
            ClientId = form.ClientId,
            RedirectUri = string.IsNullOrWhiteSpace(form.RedirectUri) ? "http://localhost" : form.RedirectUri
        });
        ChangesMade = true;
        RefreshList();
    }

    private void EditSelected()
    {
        var existing = SelectedConnection;
        if (existing is null) return;

        using var form = new ConnectionEditForm($"Edit account - {existing.Name}", existing);
        if (form.ShowDialog(this) != DialogResult.OK) return;

        if (Connections.Any(c => c != existing && c.Name.Equals(form.ConnectionName, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, $"An account named \"{form.ConnectionName}\" already exists.",
                "Duplicate name", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        existing.Name = form.ConnectionName;
        existing.TenantId = form.TenantId;
        existing.ClientId = form.ClientId;
        existing.RedirectUri = string.IsNullOrWhiteSpace(form.RedirectUri) ? "http://localhost" : form.RedirectUri;
        ChangesMade = true;
        RefreshList();
    }

    private void RemoveSelected()
    {
        var existing = SelectedConnection;
        if (existing is null) return;

        var confirm = MessageBox.Show(this,
            $"Remove the \"{existing.Name}\" account? This signs it out and clears its cached sign-in.",
            "Remove account", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes) return;

        Connections.Remove(existing);
        ChangesMade = true;
        RefreshList();
    }
}
