using System.Diagnostics;

namespace ShinEraTenseiP同期アプリ;

/// <summary>取得元・保存先・触らない場所など、普段は変えない設定</summary>
class ConfigDialog : Form
{
    public AppConfig Result { get; private set; }

    private readonly string _appDir;
    private readonly TextBox _bodyUrl = Box(), _bodyBranch = Box(), _kojoUrl = Box(), _kojoBranch = Box();
    private readonly TextBox _bodyDir = Box(), _kojoDir = Box(), _extraDir = Box(), _workDir = Box(), _gitPath = Box(), _extraUrl = Box();
    private readonly TextBox _noAction = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, Height = 110, AcceptsReturn = true };

    private static TextBox Box() => new() { Dock = DockStyle.Fill };

    public ConfigDialog(AppConfig config, string appDir, string configPath)
    {
        _appDir = appDir;
        Result = config.Clone();
        Text = "詳細設定";
        Size = new Size(900, 760);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(10), AutoScroll = true };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        int row = 0;

        void Add(string label, Control box, string help, Button? extra = null)
        {
            layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Top, Margin = new Padding(3, 7, 3, 0) }, 0, row);
            layout.Controls.Add(box, 1, row);
            if (extra != null) layout.Controls.Add(extra, 2, row);
            row++;
            layout.Controls.Add(new Label { Text = help, AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(3, 0, 3, 8), MaximumSize = new Size(700, 0) }, 1, row);
            layout.SetColumnSpan(layout.GetControlFromPosition(1, row)!, 2);
            row++;
        }

        Button Browse(TextBox target, bool file = false)
        {
            var b = new Button { Text = "参照...", AutoSize = true };
            b.Click += (_, _) =>
            {
                if (file)
                {
                    using var d = new OpenFileDialog { Filter = "git.exe|git.exe|exe|*.exe" };
                    if (d.ShowDialog(this) == DialogResult.OK) target.Text = d.FileName;
                }
                else
                {
                    using var d = new FolderBrowserDialog { SelectedPath = Resolve(target.Text) };
                    if (d.ShowDialog(this) == DialogResult.OK) target.Text = d.SelectedPath;
                }
            };
            return b;
        }

        Add("本体の取得元", _bodyUrl, "gitgud のリポジトリ URL。普段は変えません（作者がリポジトリを移したときだけ）。");
        Add("本体のブランチ", _bodyBranch, "取得するブランチ。通常は master。");
        Add("口上の取得元", _kojoUrl, "口上まとめのリポジトリ URL。中の ERB はゲームの Data\\ERB に、その他（KOJO_README.md など）はゲームフォルダ直下に入ります。");
        Add("口上のブランチ", _kojoBranch, "通常は master。");
        Add("本体の保存先", _bodyDir, "本体の最新版を置く場所（空なら「本体」）。手で編集しないでください。更新のたびに配布元と同じに戻ります。", Browse(_bodyDir));
        Add("口上の保存先", _kojoDir, "口上の最新版を置く場所（空なら「口上」）。こちらも更新のたびに配布元と同じに戻ります。", Browse(_kojoDir));
        Add("追加ファイルの保存先", _extraDir, "ゲームに上乗せするファイルの置き場（空なら「追加ファイル」）。中はゲームフォルダと同じ並び（Data\\resources\\… など）。本体・口上より優先されます。", Browse(_extraDir));
        Add("作業フォルダ", _workDir, "ログ（同期アプリ_ログ.txt）、同期で消したファイルの退避先（削除済み）、zip で取得するときの一時置き場（空なら「work」）。", Browse(_workDir));
        Add("git.exe の場所", _gitPath, "空なら自動で探します。git があると差分だけ取得（数秒〜）、無いと毎回 zip を丸ごと取得します。", Browse(_gitPath, file: true));
        Add("追加画像の配布元", _extraUrl, "メイン画面の「追加画像の配布元」リンクで開くページ。今の配布元（getuploader）は 2026/11/25 にサービス終了予定なので、移転したらここを新しい URL に変えてください。空ならリンクを出しません。");
        Add("触らない場所", _noAction, "同期でコピーも削除もしない場所（1行に1つ、同期先からの相対パス）。/ を含まない名前はどの階層でも一致し、* は任意の文字列。ゲームフォルダ直下のファイルは書かなくても消しません。");

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        var ok = new Button { Text = "OK", AutoSize = true };
        var cancel = new Button { Text = "キャンセル", AutoSize = true, DialogResult = DialogResult.Cancel };
        var reset = new Button { Text = "初期値に戻す", AutoSize = true };
        var openConfig = new Button { Text = "config.txt を開く", AutoSize = true };
        var openApp = new Button { Text = "アプリのフォルダを開く", AutoSize = true };
        buttons.Controls.AddRange([cancel, ok, reset, openConfig, openApp]);
        Controls.Add(layout);
        Controls.Add(buttons);
        AcceptButton = null; // 「触らない場所」で Enter を改行に使う
        CancelButton = cancel;

        Fill(config);
        ok.Click += (_, _) =>
        {
            var c = Read();
            string? error = c.Validate() ?? c.ValidateDirs(_appDir, c.TargetDir);
            if (error != null)
            {
                MessageBox.Show(this, error, "設定を確認してください", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Result = c;
            DialogResult = DialogResult.OK;
        };
        reset.Click += (_, _) =>
        {
            var d = new AppConfig { TargetDir = config.TargetDir };
            Fill(d);
        };
        openConfig.Click += (_, _) => Process.Start(new ProcessStartInfo(configPath) { UseShellExecute = true });
        openApp.Click += (_, _) => Process.Start(new ProcessStartInfo(_appDir) { UseShellExecute = true });
    }

    private string Resolve(string value)
    {
        try { return Path.GetFullPath(Path.Combine(_appDir, value.Trim())); } catch (Exception) { return _appDir; }
    }

    private void Fill(AppConfig c)
    {
        _bodyUrl.Text = c.BodyUrl;
        _bodyBranch.Text = c.BodyBranch;
        _kojoUrl.Text = c.KojoUrl;
        _kojoBranch.Text = c.KojoBranch;
        _bodyDir.Text = c.BodyDir;
        _kojoDir.Text = c.KojoDir;
        _extraDir.Text = c.ExtraDir;
        _workDir.Text = c.WorkDir;
        _gitPath.Text = c.GitPath;
        _extraUrl.Text = c.ExtraUrl;
        _noAction.Text = string.Join(Environment.NewLine, c.NoActionFolders);
    }

    private AppConfig Read()
    {
        var c = Result.Clone();
        c.BodyUrl = _bodyUrl.Text.Trim();
        c.BodyBranch = _bodyBranch.Text.Trim();
        c.KojoUrl = _kojoUrl.Text.Trim();
        c.KojoBranch = _kojoBranch.Text.Trim();
        c.BodyDir = _bodyDir.Text.Trim();
        c.KojoDir = _kojoDir.Text.Trim();
        c.ExtraDir = _extraDir.Text.Trim();
        c.WorkDir = _workDir.Text.Trim();
        c.GitPath = _gitPath.Text.Trim();
        c.ExtraUrl = _extraUrl.Text.Trim();
        c.NoActionFolders = _noAction.Lines.Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')).ToList();
        return c;
    }
}
