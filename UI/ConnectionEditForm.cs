namespace PIMTray.UI;

// Add/edit dialog for a single tenant connection (e.g. "Prod" or "Sandbox").
public sealed class ConnectionEditForm : Form
{
    private readonly TextBox _nameBox;
    private readonly TextBox _tenantBox;
    private readonly TextBox _clientBox;
    private readonly TextBox _redirectBox;
    private readonly Button _okButton;
    private readonly Button _cancelButton;

    public string ConnectionName => _nameBox.Text.Trim();
    public string TenantId => _tenantBox.Text.Trim();
    public string ClientId => _clientBox.Text.Trim();
    public string RedirectUri => _redirectBox.Text.Trim();

    public ConnectionEditForm(string title, ConnectionConfig? existing)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        Padding = new Padding(12);
        Font = new Font("Segoe UI", 9F);
        ClientSize = new Size(420, 240);

        var nameLabel = new Label { Text = "Display name:", Location = new Point(12, 16), AutoSize = true };
        _nameBox = new TextBox { Location = new Point(140, 12), Size = new Size(268, 24), Text = existing?.Name ?? "" };

        var tenantLabel = new Label { Text = "Tenant ID:", Location = new Point(12, 52), AutoSize = true };
        _tenantBox = new TextBox { Location = new Point(140, 48), Size = new Size(268, 24), Text = existing?.TenantId ?? "common" };

        var clientLabel = new Label { Text = "Client (app) ID:", Location = new Point(12, 88), AutoSize = true };
        _clientBox = new TextBox { Location = new Point(140, 84), Size = new Size(268, 24), Text = existing?.ClientId ?? "" };

        var redirectLabel = new Label { Text = "Redirect URI:", Location = new Point(12, 124), AutoSize = true };
        _redirectBox = new TextBox { Location = new Point(140, 120), Size = new Size(268, 24), Text = existing?.RedirectUri ?? "http://localhost" };

        var hintLabel = new Label
        {
            Text = "Tip: use your tenant's directory (tenant) ID for single-tenant\napp registrations, or \"common\" for multi-tenant.",
            Location = new Point(12, 156),
            Size = new Size(396, 36),
            ForeColor = SystemColors.GrayText
        };

        _okButton = new Button { Text = "Save", Location = new Point(254, 200), Size = new Size(78, 28), DialogResult = DialogResult.None };
        _okButton.Click += OnSaveClick;

        _cancelButton = new Button { Text = "Cancel", Location = new Point(338, 200), Size = new Size(70, 28), DialogResult = DialogResult.Cancel };

        AcceptButton = _okButton;
        CancelButton = _cancelButton;

        Controls.AddRange(new Control[]
        {
            nameLabel, _nameBox, tenantLabel, _tenantBox, clientLabel, _clientBox,
            redirectLabel, _redirectBox, hintLabel, _okButton, _cancelButton
        });

        Load += (_, _) => _nameBox.Focus();
    }

    private void OnSaveClick(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ConnectionName))
        {
            MessageBox.Show(this, "Please enter a display name (e.g. \"Prod\" or \"Sandbox\").",
                "Name required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _nameBox.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(ClientId))
        {
            MessageBox.Show(this, "Please enter the app registration's Client (application) ID.",
                "Client ID required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _clientBox.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(TenantId))
        {
            MessageBox.Show(this, "Please enter a tenant ID, or \"common\" for multi-tenant.",
                "Tenant ID required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _tenantBox.Focus();
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}
