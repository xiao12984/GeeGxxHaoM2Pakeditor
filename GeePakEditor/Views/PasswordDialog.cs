using DevExpress.XtraEditors;

namespace GeePakEditor.Views;

/// <summary>
/// 提供与原编辑器一致的紧凑密码输入对话框。
/// </summary>
internal sealed class PasswordDialog : XtraForm
{
    private readonly TextEdit _passwordEdit = new();

    /// <summary>
    /// 创建只要求用户输入或确认密码的紧凑窗口。
    /// </summary>
    public PasswordDialog(string? initialPassword)
    {
        Text = "输入密码";
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ClientSize = new Size(282, 162);
        Font = new Font("Microsoft YaHei UI", 9F);

        // TXT 中已记录的密码只作为初始值，用户仍可直接修改后确认。
        _passwordEdit.Text = initialPassword ?? string.Empty;
        _passwordEdit.Properties.UseSystemPasswordChar = true;
        _passwordEdit.Dock = DockStyle.Fill;

        var inputLabel = new LabelControl { Text = "输入密码：", Dock = DockStyle.Fill };
        var okButton = new SimpleButton { Text = "OK", DialogResult = DialogResult.OK, Width = 78 };
        var cancelButton = new SimpleButton { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 78 };
        AcceptButton = okButton;
        CancelButton = cancelButton;

        var buttonPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(61, 0, 0, 0),
            WrapContents = false
        };
        buttonPanel.Controls.Add(okButton);
        buttonPanel.Controls.Add(cancelButton);

        // 使用单列表格布局，使标签、输入框和按钮与原编辑器的纵向顺序一致。
        var layout = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Padding = new Padding(14, 38, 14, 14),
            RowCount = 4
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 27F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        layout.Controls.Add(inputLabel, 0, 0);
        layout.Controls.Add(_passwordEdit, 0, 1);
        layout.Controls.Add(buttonPanel, 0, 3);
        Controls.Add(layout);

        // 打开窗口后立即定位到密码框；预填密码会被选中，方便直接覆盖输入。
        Shown += (_, _) =>
        {
            _passwordEdit.Focus();
            _passwordEdit.SelectAll();
        };
    }

    /// <summary>
    /// 返回用户确认后用于打开归档的密码。
    /// </summary>
    public string Password => _passwordEdit.Text;
}
