namespace ShinEraTenseiP同期アプリ;

static class Program
{
    [STAThread]
    static void Main()
    {
        // zip 内の Shift_JIS ファイル名を読むため
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
