using DevExpress.XtraEditors;
using DevExpress.XtraLayout;

namespace GeePakEditor.Views;

/// <summary>
/// 输入 PAK 密码并选择是否写回 FilePassword.txt 的对话框。
/// </summary>
internal sealed class PasswordDialog : XtraForm
{
    private readonly TextEdit _passwordEdit = new();
    private readonly CheckEdit _rememberCheck = new();

    /// <summary>
    /// 创建紧凑且支持 DPI 的密码输入窗口。
    /// </summary>
    public PasswordDialog(string fileName, string? initialPassword)
    {
        Text = "输入 PAK 密码";
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ClientSize = new Size(430, 154);
        Font = new Font("Microsoft YaHei UI", 9F);

        _passwordEdit.Text = initialPassword ?? string.Empty;
        _passwordEdit.Properties.UseSystemPasswordChar = true;
        _rememberCheck.Text = "写入 FilePassword.txt";
        _rememberCheck.Checked = true;

        var okButton = new SimpleButton { Text = "确定", DialogResult = DialogResult.OK };
        var cancelButton = new SimpleButton { Text = "取消", DialogResult = DialogResult.Cancel };
        AcceptButton = okButton;
        CancelButton = cancelButton;

        var layout = new LayoutControl { Dock = DockStyle.Fill };
        Controls.Add(layout);
        layout.Controls.Add(_passwordEdit);
        layout.Controls.Add(_rememberCheck);
        layout.Controls.Add(okButton);
        layout.Controls.Add(cancelButton);
        var root = new LayoutControlGroup { EnableIndentsWithoutBorders = DevExpress.Utils.DefaultBoolean.True };
        layout.Root = root;
        root.AddItem($"文件：{fileName}", _passwordEdit);
        root.AddItem(string.Empty, _rememberCheck).TextVisible = false;
        var buttons = root.AddItem(string.Empty, okButton);
        buttons.TextVisible = false;
        buttons.SizeConstraintsType = SizeConstraintsType.Custom;
        buttons.MinSize = new Size(90, 32);
        var cancelItem = root.AddItem(string.Empty, cancelButton);
        cancelItem.TextVisible = false;
        cancelItem.SizeConstraintsType = SizeConstraintsType.Custom;
        cancelItem.MinSize = new Size(90, 32);
    }

    /// <summary>用户输入的密码。</summary>
    public string Password => _passwordEdit.Text;

    /// <summary>是否把成功密码写回 PAK 同目录。</summary>
    public bool RememberPassword => _rememberCheck.Checked;
}
