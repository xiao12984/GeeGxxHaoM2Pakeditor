using DevExpress.XtraEditors;
using GeePakEditor.Models;

namespace GeePakEditor.Views;

/// <summary>
/// 新建 PAK/WZL 资源文件的设置对话框。
/// </summary>
internal sealed class NewArchiveDialog : XtraForm
{
    private const string DefaultPassword = "Mir";

    private readonly ComboBoxEdit _formatEdit = new();
    private readonly TextEdit _passwordEdit = new();
    private readonly LabelControl _passwordLabel = new();

    /// <summary>
    /// 创建与现有密码输入窗口一致风格的新建文件设置窗口。
    /// </summary>
    public NewArchiveDialog()
    {
        Text = "新建文件";
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ClientSize = new Size(340, 220);
        Font = new Font("Microsoft YaHei UI", 9F);
        Appearance.BackColor = Color.FromArgb(250, 250, 250);

        ConfigureFormatEdit();
        ConfigurePasswordEdit();

        var titleLabel = new LabelControl
        {
            Text = "资源设置",
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
            Dock = DockStyle.Fill,
            Appearance = { ForeColor = Color.FromArgb(40, 40, 40) }
        };

        var formatLabel = new LabelControl
        {
            Text = "文件类型",
            Dock = DockStyle.Fill,
            Appearance = { ForeColor = Color.FromArgb(100, 100, 100) }
        };

        _passwordLabel.Text = "文件密码";
        _passwordLabel.Dock = DockStyle.Fill;
        _passwordLabel.Appearance.ForeColor = Color.FromArgb(100, 100, 100);

        var okButton = new SimpleButton
        {
            Text = "确认(&N)",
            DialogResult = DialogResult.OK,
            Width = 92,
            Height = 30,
            Font = new Font("Microsoft YaHei UI", 9F)
        };
        var cancelButton = new SimpleButton
        {
            Text = "取消(&C)",
            DialogResult = DialogResult.Cancel,
            Width = 92,
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
            Padding = new Padding(0, 8, 0, 0),
            WrapContents = false
        };
        buttonPanel.Controls.Add(cancelButton);
        buttonPanel.Controls.Add(okButton);

        var layout = new TableLayoutPanel
        {
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Padding = new Padding(20, 18, 20, 16),
            RowCount = 4
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.Controls.Add(titleLabel, 0, 0);
        layout.SetColumnSpan(titleLabel, 2);
        layout.Controls.Add(formatLabel, 0, 1);
        layout.Controls.Add(_formatEdit, 1, 1);
        layout.Controls.Add(_passwordLabel, 0, 2);
        layout.Controls.Add(_passwordEdit, 1, 2);
        layout.Controls.Add(buttonPanel, 0, 3);
        layout.SetColumnSpan(buttonPanel, 2);
        Controls.Add(layout);

        Shown += (_, _) => _formatEdit.Focus();
    }

    /// <summary>
    /// 返回用户确认的新归档格式与密码。
    /// </summary>
    public NewArchiveSettings Settings => new()
    {
        Format = SelectedFormat,
        Password = SelectedFormat == PakArchiveFormat.GeePak3 ? _passwordEdit.Text.Trim() : string.Empty
    };

    /// <summary>
    /// 初始化格式下拉框并绑定格式切换事件。
    /// </summary>
    private void ConfigureFormatEdit()
    {
        _formatEdit.Dock = DockStyle.Fill;
        _formatEdit.Properties.TextEditStyle = DevExpress.XtraEditors.Controls.TextEditStyles.DisableTextEditor;
        _formatEdit.Properties.Items.AddRange(new object[] { "PAK", "WZL" });
        _formatEdit.SelectedIndex = 0;
        _formatEdit.SelectedIndexChanged += (_, _) => UpdatePasswordState();
    }

    /// <summary>
    /// 初始化 PAK 密码输入框，默认使用用户指定的 Mir。
    /// </summary>
    private void ConfigurePasswordEdit()
    {
        _passwordEdit.Text = DefaultPassword;
        _passwordEdit.Dock = DockStyle.Fill;
        _passwordEdit.Properties.UseSystemPasswordChar = false;
        _passwordEdit.Properties.Appearance.Font = new Font("Microsoft YaHei UI", 9F);
    }

    /// <summary>
    /// 按当前格式启用或禁用 PAK 密码输入。
    /// </summary>
    private void UpdatePasswordState()
    {
        var enabled = SelectedFormat == PakArchiveFormat.GeePak3;
        _passwordLabel.Enabled = enabled;
        _passwordEdit.Enabled = enabled;
    }

    /// <summary>
    /// 根据下拉框选择返回目标归档格式。
    /// </summary>
    private PakArchiveFormat SelectedFormat => _formatEdit.SelectedIndex == 1
        ? PakArchiveFormat.Wzl
        : PakArchiveFormat.GeePak3;
}
