using DevExpress.LookAndFeel;
using GeePakEditor.Controllers;
using GeePakEditor.Services;
using GeePakEditor.Views;

namespace GeePakEditor;

/// <summary>
/// 程序入口，仅负责基础环境与依赖装配。
/// </summary>
internal static class Program
{
    /// <summary>
    /// 启动 GEE PAK 编辑器主窗口。
    /// </summary>
    [STAThread]
    private static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        UserLookAndFeel.Default.SetSkinStyle("Office 2019 Colorful");

        var keyProvider = new PakKeyProvider();
        var imageCodec = new PakImageCodec();
        var archiveService = new GeePakArchiveService(keyProvider, imageCodec);
        var passwordService = new PakPasswordService();
        using var mainForm = new MainForm();
        _ = new MainController(mainForm, archiveService, passwordService);
        Application.Run(mainForm);
    }
}
