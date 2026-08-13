using System.Drawing;
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
        UserLookAndFeel.Default.SetSkinStyle("WXI");
        UserLookAndFeel.Default.SkinMaskColor = Color.FromArgb(0, 122, 204);
        UserLookAndFeel.Default.SkinMaskColor2 = Color.FromArgb(0, 122, 204);

        // 密钥服务会在程序关闭时清理本次启动的本地派生引擎。
        using var keyProvider = new PakKeyProvider();
        var imageCodec = new PakImageCodec();
        var archiveService = new GeePakArchiveService(keyProvider, imageCodec);
        var passwordService = new PakPasswordService();
        // 分类服务只负责读取用户选择的资源根目录，不参与 PAK/WZL 解码。
        var resourceFolderCatalogService = new ResourceFolderCatalogService();
        using var mainForm = new MainForm();
        _ = new MainController(mainForm, archiveService, passwordService, resourceFolderCatalogService);
        Application.Run(mainForm);
    }
}
