using DevExpress.XtraEditors;

namespace GeePakEditor.Views;

/// <summary>
/// 提供现代化密码输入对话框，带有简洁清晰的视觉层次。
/// </summary>
internal sealed class PasswordDialog : XtraForm
{
    private readonly TextEdit _passwordEdit = new();

    /// <summary>
    /// 创建只要求用户输入或确认密码的现代化窗口。
    /// </summary>
    public PasswordDialog(string? initialPassword)
    {
        Text = "输入密码";
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ClientSize = new Size(320, 180);
        Font = new Font("Microsoft YaHei UI", 9F);
        Appearance.BackColor = Color.FromArgb(250, 250, 250);

        // TXT 中已记录的密码只作为初始值，用户仍可直接修改后确认。
        _passwordEdit.Text = initialPassword ?? string.Empty;
        _passwordEdit.Properties.UseSystemPasswordChar = true;
        _passwordEdit.Properties.Appearance.Font = new Font("Microsoft YaHei UI", 9F);
        _passwordEdit.Dock = DockStyle.Fill;

        var titleLabel = new LabelControl
        {
            Text = "请输入归档密码",
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
            Dock = DockStyle.Fill,
            Appearance = { ForeColor = Color.FromArgb(40, 40, 40) }
        };

        var inputLabel = new LabelControl
        {
            Text = "密码",
            Dock = DockStyle.Fill,
            Appearance = { ForeColor = Color.FromArgb(100, 100, 100) }
        };

        var okButton = new SimpleButton
        {
            Text = "确定",
            DialogResult = DialogResult.OK,
            Width = 90,
            Height = 30,
            Font = new Font("Microsoft YaHei UI", 9F)
        };
        var cancelButton = new SimpleButton
        {
            Text = "取消",
            DialogResult = DialogResult.Cancel,
            Width = 90,
            Height = 30,
            Font = new Font("Microsoft YaHei UI", 9F)
        };
        AcceptButton = okButton;
        CancelButton = cancelButton;

        var buttonPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 4, 0, 0),
            WrapContents = false
        };
        buttonPanel.Controls.Add(cancelButton);
        buttonPanel.Controls.Add(okButton);

        // 使用单列表格布局，使标题、标签、输入框和按钮有清晰的纵向层次。
        var layout = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Padding = new Padding(20, 20, 20, 16),
            RowCount = 4
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.Controls.Add(titleLabel, 0, 0);
        layout.Controls.Add(inputLabel, 0, 1);
        layout.Controls.Add(_passwordEdit, 0, 2);
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