using System.Text;

namespace ShinEraTenseiP同期アプリ;

/// <summary>
/// 前回の同期でゲームへ配ったファイルの一覧（同期先からの相対パス。直下のファイルと触らない場所は除く）。
/// ゲーム側にだけあるファイルが「配布元で消えた・改名された古いもの」（一覧にある）か、
/// 「ゲームフォルダに直接置いたもの」（一覧に無い）かを見分けるのに使う
/// </summary>
static class DistributedList
{
    public static HashSet<string>? Load(string path)
    {
        if (!File.Exists(path)) return null;
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
        {
            if (line.Length == 0 || line.StartsWith('#')) continue;
            set.Add(line.Replace('/', Path.DirectorySeparatorChar));
        }
        return set;
    }

    public static void Save(string path, IEnumerable<string> relPaths)
    {
        var sb = new StringBuilder();
        sb.Append("# 同期アプリが前回ゲームへ配ったファイル（自動で書き換わります。消すと次の同期で今の本体・口上・追加ファイルから作り直します）\r\n");
        foreach (var p in relPaths.Order(StringComparer.Ordinal)) sb.Append(p.Replace(Path.DirectorySeparatorChar, '/')).Append("\r\n");
        string temp = path + ".tmp";
        File.WriteAllText(temp, sb.ToString(), new UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);
    }
}

/// <summary>
/// ゲーム側にだけあるファイルを、「追加ファイル」に取り込んで残すか、同期で消すか選ぶ画面
/// </summary>
class GameOnlyDialog : Form
{
    private readonly CheckedListBox _list = new() { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false, HorizontalScrollbar = true };
    private readonly List<string> _files;

    /// <summary>残すと選んだもの（OK のとき）</summary>
    public List<string> Kept { get; } = [];

    public GameOnlyDialog(IReadOnlyList<(string Rel, bool WasDistributed)> files, string targetRoot)
    {
        var sorted = files.OrderBy(f => f.Rel, StringComparer.OrdinalIgnoreCase).ToList();
        _files = sorted.Select(f => f.Rel).ToList();
        Text = "ゲーム側にだけあるファイル";
        Size = new Size(780, 580);
        MinimumSize = new Size(520, 400);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;

        long bytes = 0;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(10) };
        var message = new Label { AutoSize = true, MaximumSize = new Size(740, 0) };
        layout.Controls.Add(message);
        if (sorted.Any(f => f.WasDistributed))
        {
            layout.Controls.Add(Note("「配布元で消えたファイル」は、前回の同期まで配っていたもの（配布元で削除・改名された古い公式画像など）です。", SystemColors.GrayText));
        }
        if (_files.Any(IsScript))
        {
            layout.Controls.Add(Note("ERB・ERH・CSV を残すと、配布元で消えた古いファイルだった場合に、二重定義などでゲームがエラーになることがあります。"
                + "そのため最初はチェックを外してあります。自分で入れた改造ファイルなら、チェックしてください。", Color.Firebrick));
        }

        _list.BeginUpdate();
        foreach (var (rel, was) in sorted)
        {
            long size = 0;
            try { size = new FileInfo(Path.Combine(targetRoot, rel)).Length; } catch (Exception) { }
            bytes += size;
            _list.Items.Add($"{rel}（{PathUtil.FormatBytes(size)}）" + (was ? "（配布元で消えたファイル）" : ""), !IsScript(rel));
        }
        _list.EndUpdate();
        layout.Controls.Add(_list);

        message.Text = $"本体・口上・追加ファイルのどれにも無いファイルが、ゲームのフォルダに {_files.Count:#,0} 件（{PathUtil.FormatBytes(bytes)}）あります。"
            + "昔ゲームフォルダに直接入れた画像や、自分で置いたファイルなどです。\n\n"
            + "残すものにチェックを入れてください。\n"
            + "・チェックしたもの：「追加ファイル」に同じ並びで取り込んで残します（次からは追加ファイルとして同期するので、もう聞きません）\n"
            + "・チェックしないもの：同期で消します（work\\削除済み に退避します）";

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        var ok = new Button { Text = "チェックしたものを残して同期", AutoSize = true, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "キャンセル（同期をやめる）", AutoSize = true, DialogResult = DialogResult.Cancel };
        var none = new Button { Text = "すべて外す", AutoSize = true };
        var all = new Button { Text = "すべて選ぶ", AutoSize = true };
        all.Click += (_, _) => SetAll(true);
        none.Click += (_, _) => SetAll(false);
        bottom.Controls.AddRange([ok, cancel, none, all]);
        layout.Controls.Add(bottom);

        // 1列なので、足した順がそのまま行になる。一覧だけ伸ばす
        foreach (Control c in layout.Controls)
            layout.RowStyles.Add(c == _list ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.AutoSize));
        Controls.Add(layout);
        AcceptButton = ok;
        CancelButton = cancel;

        FormClosing += (_, _) =>
        {
            if (DialogResult != DialogResult.OK) return;
            foreach (int i in _list.CheckedIndices) Kept.Add(_files[i]);
        };
    }

    private static bool IsScript(string rel) => Path.GetExtension(rel).ToLowerInvariant() is ".erb" or ".erh" or ".csv";

    private void SetAll(bool value)
    {
        _list.BeginUpdate();
        for (int i = 0; i < _list.Items.Count; i++) _list.SetItemChecked(i, value);
        _list.EndUpdate();
    }

    private static Label Note(string text, Color color) =>
        new() { Text = text, AutoSize = true, ForeColor = color, MaximumSize = new Size(740, 0), Margin = new Padding(3, 8, 3, 3) };
}
