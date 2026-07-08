using PIMTray.Pim;

namespace PIMTray.UI;

public sealed class ActivateRoleForm : Form
{
    private readonly TextBox _reasonBox;
    private readonly ComboBox _durationBox;
    private readonly Button _okButton;
    private readonly Button _cancelButton;

    public string Justification => _reasonBox.Text.Trim();
    public TimeSpan Duration => TimeSpan.FromHours((int)(_durationBox.SelectedItem ?? 1));

    public ActivateRoleForm(IReadOnlyList<EligibleRole> roles, int[] durationOptions, int defaultHours)
    {
        if (roles is null || roles.Count == 0)
            throw new ArgumentException("At least one role is required.", nameof(roles));

        var multi = roles.Count > 1;
        Text = multi
            ? $"Activate {roles.Count} roles"
            : $"Activate: {roles[0].RoleDisplayName}";

        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = true;
        Padding = new Padding(12);
        Font = new Font("Segoe UI", 9F);

        var headerLabel = new Label
        {
            Text = multi
                ? $"Activating {roles.Count} eligible roles with the same reason and duration:"
                : "Activating role:",
            Location = new Point(12, 12),
            AutoSize = true
        };

        var rolesList = new ListBox
        {
            Location = new Point(12, 34),
            Size = new Size(416, multi ? 96 : 36),
            SelectionMode = SelectionMode.None,
            IntegralHeight = false,
            BackColor = SystemColors.Control,
            BorderStyle = BorderStyle.FixedSingle
        };
        var multiTenant = roles.Select(r => r.ConnectionName).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
        foreach (var r in roles)
        {
            var label = r.ScopeDescription == "Directory"
                ? r.RoleDisplayName
                : $"{r.RoleDisplayName}  ({r.ScopeDescription})";
            rolesList.Items.Add(multiTenant ? $"{label}  [{r.ConnectionName}]" : label);
        }

        var listBottom = rolesList.Bottom + 10;

        var reasonLabel = new Label
        {
            Text = "Reason / justification (required):",
            Location = new Point(12, listBottom),
            AutoSize = true
        };

        _reasonBox = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Location = new Point(12, listBottom + 20),
            Size = new Size(416, 80),
            MaxLength = 500
        };

        var reasonBottom = _reasonBox.Bottom + 12;

        var durationLabel = new Label
        {
            Text = "Duration:",
            Location = new Point(12, reasonBottom + 4),
            AutoSize = true
        };

        _durationBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(80, reasonBottom),
            Size = new Size(120, 24)
        };
        foreach (var h in durationOptions) _durationBox.Items.Add(h);
        _durationBox.SelectedItem = durationOptions.Contains(defaultHours) ? defaultHours : durationOptions.First();
        _durationBox.Format += (_, e) =>
        {
            if (e.ListItem is int hours) e.Value = hours == 1 ? "1 hour" : $"{hours} hours";
        };

        var buttonsTop = _durationBox.Bottom + 16;

        _okButton = new Button
        {
            Text = multi ? $"Activate all ({roles.Count})" : "Activate",
            Location = new Point(254, buttonsTop),
            Size = new Size(126, 28),
            DialogResult = DialogResult.None
        };
        _okButton.Click += OnActivateClick;

        _cancelButton = new Button
        {
            Text = "Cancel",
            Location = new Point(386, buttonsTop),
            Size = new Size(84, 28),
            DialogResult = DialogResult.Cancel
        };

        AcceptButton = _okButton;
        CancelButton = _cancelButton;

        ClientSize = new Size(484, buttonsTop + 28 + 12);

        Controls.AddRange(new Control[]
        {
            headerLabel, rolesList, reasonLabel, _reasonBox,
            durationLabel, _durationBox, _okButton, _cancelButton
        });

        Load += (_, _) => _reasonBox.Focus();
    }

    private void OnActivateClick(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_reasonBox.Text))
        {
            MessageBox.Show(this, "Please enter a reason for the activation.", "Reason required",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _reasonBox.Focus();
            return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }
}
