using System.Text;

namespace ShinEraTenseiP同期アプリ;

/// <summary>
/// 設定はすべて config.txt（exe の隣）に入れる。知らないキーも消さずに残す
/// </summary>
class AppConfig
{
    public const string DefaultBodyUrl = "https://gitgud.io/bazzile/eramegaten_p";
    public const string DefaultKojoUrl = "https://gitgud.io/bazzile/eramegatenpKOJO";
    public const string DefaultBranch = "master";
    public const string DefaultLaunchExe = "Emuera.exe";
    // 追加画像の配布元（getuploader は 2026/11/25 にサービス終了予定。移転したら詳細設定で変える）
    public const string DefaultExtraUrl = "https://uu.getuploader.com/eraMegaten_P/";

    // 本体の .gitignore で「配布物ではない」とされている物（セーブ・設定・ログ）と git の管理フォルダ
    public static readonly string[] DefaultNoAction =
    [
        ".git",
        "Data/sav",
        "Data/debug",
        "Data/*.config",
        "Data/*.profile",
        "Data/profile",
        "Data/setting_user.json",
        "Data/resources/画像_自家製",
        "*.log",
        "*.dat",
        "console_history.json",
    ];

    public string TargetDir { get; set; } = "";
    public string BodyUrl { get; set; } = DefaultBodyUrl;
    public string BodyBranch { get; set; } = DefaultBranch;
    public bool UseKojo { get; set; } = true;
    public string KojoUrl { get; set; } = DefaultKojoUrl;
    public string KojoBranch { get; set; } = DefaultBranch;
    public string BodyDir { get; set; } = "";
    public string KojoDir { get; set; } = "";
    public string ExtraDir { get; set; } = "";
    public string WorkDir { get; set; } = "";
    public string GitPath { get; set; } = "";
    public string LaunchExe { get; set; } = DefaultLaunchExe;
    public bool LaunchAfterUpdate { get; set; } = false;
    public string ExtraUrl { get; set; } = DefaultExtraUrl;
    public List<string> NoActionFolders { get; set; } = [.. DefaultNoAction];

    private List<KeyValuePair<string, string>> _unknown = [];

    // 書き出す順番とコメント
    private static readonly (string Key, string? Comment)[] Layout =
    [
        ("TargetDir", "同期先（ゲームの ShinEraTenseiP フォルダ）"),
        ("BodyUrl", "本体の取得元（gitgud のリポジトリ URL）とブランチ"),
        ("BodyBranch", null),
        ("UseKojo", "口上も入れるか（true/false）と、口上の取得元・ブランチ"),
        ("KojoUrl", null),
        ("KojoBranch", null),
        ("BodyDir", "保存先（空なら既定: 本体 / 口上 / 追加ファイル / work。相対パスはこのフォルダ基準）"),
        ("KojoDir", null),
        ("ExtraDir", null),
        ("WorkDir", null),
        ("GitPath", "git.exe の場所（空なら自動で探す。見つからなければ zip で丸ごと取得）"),
        ("LaunchExe", "「ゲームを起動」で起動する exe（同期先からの相対パス）と、まとめて更新のあとに起動するか"),
        ("LaunchAfterUpdate", null),
        ("ExtraUrl", "追加画像の配布元のページ（メイン画面のリンクで開く。空ならリンクを出さない）"),
    ];

    private static bool IsKnownKey(string key) => Layout.Any(l => l.Key == key);

    public string BodyPath(string appDir) => Resolve(BodyDir, appDir, "本体");
    public string KojoPath(string appDir) => Resolve(KojoDir, appDir, "口上");
    public string ExtraPath(string appDir) => Resolve(ExtraDir, appDir, "追加ファイル");
    public string WorkPath(string appDir) => Resolve(WorkDir, appDir, "work");

    private static string Resolve(string value, string appDir, string defaultName) =>
        Path.GetFullPath(Path.Combine(appDir, string.IsNullOrWhiteSpace(value) ? defaultName : Environment.ExpandEnvironmentVariables(value.Trim())));

    public AppConfig Clone()
    {
        var c = (AppConfig)MemberwiseClone();
        c.NoActionFolders = [.. NoActionFolders];
        c._unknown = [.. _unknown];
        return c;
    }

    public static AppConfig Load(string path)
    {
        var c = new AppConfig();
        if (!File.Exists(path)) return c;
        List<string>? list = null;
        foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
        {
            var t = raw.Trim();
            if (t.Length == 0 || t.StartsWith('#')) continue;
            if (t == "NoActionFolders=")
            {
                c.NoActionFolders = [];
                list = c.NoActionFolders;
                continue;
            }
            int eq = t.IndexOf('=');
            string key = eq > 0 ? t[..eq].Trim() : "";
            // 一覧の途中でも、知っているキーの行が来たら一覧は終わり
            if (eq > 0 && (list == null || IsKnownKey(key)))
            {
                list = null;
                c.Set(key, t[(eq + 1)..].Trim());
                continue;
            }
            list?.Add(t);
        }
        return c;
    }

    private void Set(string key, string value)
    {
        switch (key)
        {
            case "TargetDir": TargetDir = value; break;
            case "BodyUrl": if (value.Length > 0) BodyUrl = value; break;
            case "BodyBranch": if (value.Length > 0) BodyBranch = value; break;
            case "UseKojo": UseKojo = !value.Equals("false", StringComparison.OrdinalIgnoreCase); break;
            case "KojoUrl": if (value.Length > 0) KojoUrl = value; break;
            case "KojoBranch": if (value.Length > 0) KojoBranch = value; break;
            case "BodyDir": BodyDir = value; break;
            case "KojoDir": KojoDir = value; break;
            case "ExtraDir": ExtraDir = value; break;
            case "WorkDir": WorkDir = value; break;
            case "GitPath": GitPath = value; break;
            case "LaunchExe": LaunchExe = value; break;
            case "LaunchAfterUpdate": LaunchAfterUpdate = value.Equals("true", StringComparison.OrdinalIgnoreCase); break;
            case "ExtraUrl": ExtraUrl = value; break;
            default: _unknown.Add(new(key, value)); break;
        }
    }

    private string Get(string key) => key switch
    {
        "TargetDir" => TargetDir,
        "BodyUrl" => BodyUrl,
        "BodyBranch" => BodyBranch,
        "UseKojo" => UseKojo ? "true" : "false",
        "KojoUrl" => KojoUrl,
        "KojoBranch" => KojoBranch,
        "BodyDir" => BodyDir,
        "KojoDir" => KojoDir,
        "ExtraDir" => ExtraDir,
        "WorkDir" => WorkDir,
        "GitPath" => GitPath,
        "LaunchExe" => LaunchExe,
        "LaunchAfterUpdate" => LaunchAfterUpdate ? "true" : "false",
        "ExtraUrl" => ExtraUrl,
        _ => "",
    };

    public void Save(string path)
    {
        var sb = new StringBuilder();
        sb.Append("# ShinEraTenseiP 同期アプリの設定（アプリの「詳細設定」からも変えられます）\r\n\r\n");
        foreach (var (key, comment) in Layout)
        {
            if (comment != null) sb.Append("# ").Append(comment).Append("\r\n");
            sb.Append(key).Append('=').Append(Get(key)).Append("\r\n");
        }
        foreach (var kv in _unknown) sb.Append(kv.Key).Append('=').Append(kv.Value).Append("\r\n");
        sb.Append("\r\n# 同期で触らない場所（1行に1つ。同期先からの相対パス。/ を含まない名前はどの階層でも一致、* は任意の文字列）\r\n");
        sb.Append("# ゲームフォルダ直下のファイル（EmueraEX.exe など）は書かなくても消しません\r\n");
        sb.Append("NoActionFolders=\r\n");
        foreach (var p in NoActionFolders) sb.Append(p).Append("\r\n");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    /// <summary>入力が使える形か。問題があればその説明</summary>
    public string? Validate()
    {
        foreach (var (name, url, branch) in new[] { ("本体", BodyUrl, BodyBranch), ("口上", KojoUrl, KojoBranch) })
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != "https" || u.AbsolutePath.Trim('/').Split('/').Length < 2)
                return $"{name}の取得元は「https://gitgud.io/作者名/リポジトリ名」の形で指定してください。";
            if (branch.Length == 0 || branch.Any(char.IsWhiteSpace) || branch.StartsWith('-'))
                return $"{name}のブランチ名が正しくありません。";
        }
        if (ExtraUrl.Length > 0 && !IsWebUrl(ExtraUrl)) return "追加画像の配布元は「https://…」の URL で指定してください（使わないなら空に）。";
        return null;
    }

    public static bool IsWebUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == "https" || u.Scheme == "http");

    /// <summary>保存先どうし・同期先と重なっていないか（重なると更新や同期で消し合う）</summary>
    public string? ValidateDirs(string appDir, string? targetRoot)
    {
        (string Name, string Path)[] dirs;
        try
        {
            dirs = [("本体の保存先", BodyPath(appDir)), ("口上の保存先", KojoPath(appDir)), ("追加ファイルの保存先", ExtraPath(appDir)), ("作業フォルダ", WorkPath(appDir))];
        }
        catch (Exception ex)
        {
            return "保存先のパスが正しくありません: " + ex.Message;
        }
        for (int i = 0; i < dirs.Length; i++)
        {
            for (int j = i + 1; j < dirs.Length; j++)
            {
                if (PathUtil.IsSameOrUnder(dirs[i].Path, dirs[j].Path) || PathUtil.IsSameOrUnder(dirs[j].Path, dirs[i].Path))
                    return $"{dirs[i].Name}と{dirs[j].Name}が重なっています。別々のフォルダにしてください。";
            }
        }
        if (!string.IsNullOrWhiteSpace(targetRoot))
        {
            string target = Path.GetFullPath(targetRoot);
            foreach (var (name, path) in dirs)
            {
                if (PathUtil.IsSameOrUnder(path, target) || PathUtil.IsSameOrUnder(target, path)) return $"{name}が同期先（ゲームのフォルダ）と重なっています。";
            }
            if (PathUtil.IsSameOrUnder(appDir, target)) return "このアプリのフォルダが同期先の中にあります（同期で消されます）。";
        }
        return null;
    }
}
