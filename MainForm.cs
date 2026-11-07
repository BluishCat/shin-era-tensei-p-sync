using System.Diagnostics;
using System.Text;

namespace ShinEraTenseiP同期アプリ;

class MainForm : Form
{
    private static readonly string AppDirectory = AppContext.BaseDirectory;
    private static readonly string ConfigPath = Path.Combine(AppDirectory, "config.txt");
    private const long MaxLogBytes = 2_000_000;

    private AppConfig _config = new();
    private string BodyDir => _config.BodyPath(AppDirectory);
    private string KojoDir => _config.KojoPath(AppDirectory);
    private string ExtraDir => _config.ExtraPath(AppDirectory);
    private string WorkDir => _config.WorkPath(AppDirectory);
    private string TrashRoot => Path.Combine(WorkDir, "削除済み");
    private string LogPath => Path.Combine(WorkDir, "同期アプリ_ログ.txt");
    private bool _logStarted;

    private readonly TextBox _targetBox = new() { Dock = DockStyle.Fill };
    private readonly Button _browseButton = new() { Text = "参照...", AutoSize = true };
    private readonly Label _stateLabel = new() { AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly CheckBox _kojoCheck = new() { Text = "口上も入れる", AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Button _configButton = new() { Text = "詳細設定...", AutoSize = true };
    private readonly Button _allButton = new() { Text = "まとめて更新（本体＋口上＋同期）", AutoSize = true, Font = new Font(Control.DefaultFont, FontStyle.Bold) };
    private readonly Button _updateButton = new() { Text = "本体・口上を更新", AutoSize = true };
    private readonly Button _syncButton = new() { Text = "同期", AutoSize = true };
    private readonly Button _importButton = new() { Text = "追加画像 zip を取り込む...", AutoSize = true };
    private readonly Button _openExtraButton = new() { Text = "追加ファイルを開く", AutoSize = true };
    private readonly ComboBox _launchBox = new() { Width = 150, DropDownStyle = ComboBoxStyle.DropDown };
    private readonly Button _launchButton = new() { Text = "ゲームを起動", AutoSize = true };
    private readonly CheckBox _launchAfterCheck = new() { Text = "まとめて更新のあと起動", AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly LinkLabel _trashLink = new() { Text = "同期で消したファイル（退避先）", AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly LinkLabel _logLink = new() { Text = "ログ", AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly LinkLabel _uploaderLink = new() { Text = "追加画像の配布元（アップローダー）", AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly ProgressBar _progressBar = new() { Dock = DockStyle.Fill };
    private readonly TextBox _logBox = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false };
    private readonly ToolTip _toolTip = new() { AutoPopDelay = 20000 };

    public MainForm()
    {
        Text = "ShinEraTenseiP 同期アプリ";
        Size = new Size(1000, 640);
        StartPosition = FormStartPosition.CenterScreen;
        AllowDrop = true;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(8) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        layout.Controls.Add(new Label { Text = "同期先", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        layout.Controls.Add(_targetBox, 1, 0);
        layout.Controls.Add(_browseButton, 2, 0);

        layout.Controls.Add(new Label { Text = "同期元", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        layout.Controls.Add(_stateLabel, 1, 1);
        var right = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        right.Controls.Add(_kojoCheck);
        right.Controls.Add(_configButton);
        layout.Controls.Add(right, 2, 1);

        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        buttons.Controls.AddRange([_allButton, _updateButton, _syncButton, _importButton, _openExtraButton]);
        layout.Controls.Add(buttons, 0, 2);
        layout.SetColumnSpan(buttons, 3);

        var launch = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        launch.Controls.AddRange([_launchBox, _launchButton, _launchAfterCheck, _trashLink, _logLink, _uploaderLink]);
        layout.Controls.Add(launch, 0, 3);
        layout.SetColumnSpan(launch, 3);

        layout.Controls.Add(_progressBar, 0, 4);
        layout.SetColumnSpan(_progressBar, 3);
        layout.Controls.Add(_logBox, 0, 5);
        layout.SetColumnSpan(_logBox, 3);
        for (int i = 0; i < 4; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(layout);

        // exe 単体で置かれても動くよう、設定が無ければ初期値で作る（隣に ShinEraTenseiP があればそれを同期先に）
        if (!File.Exists(ConfigPath))
        {
            var c = new AppConfig();
            string guess = Path.GetFullPath(Path.Combine(AppDirectory, "..", "ShinEraTenseiP"));
            if (LooksLikeGame(guess)) c.TargetDir = guess;
            c.Save(ConfigPath);
        }
        LoadConfigToUi();
        RotateLog();

        _toolTip.SetToolTip(_targetBox, "ゲームの ShinEraTenseiP フォルダ。同期で本体＋口上＋追加ファイルと同じ中身になります（セーブや設定、直下のファイルはそのまま）");
        _toolTip.SetToolTip(_allButton, "本体と口上を gitgud から取得（差分だけ）→ ゲームフォルダへ同期、を一度に行います");
        _toolTip.SetToolTip(_updateButton, "本体と口上をこのアプリのフォルダに最新版で用意します（ゲームフォルダはまだ触りません）");
        _toolTip.SetToolTip(_syncButton, "ゲームフォルダを 本体 → 口上 → 追加ファイル の重ね合わせと同じにします。消すものがあるときは確認します");
        _toolTip.SetToolTip(_importButton, "追加画像などの zip を「追加ファイル」に取り込みます（ウィンドウに zip をドロップしても可）。次の同期でゲームに入ります");
        _toolTip.SetToolTip(_openExtraButton, "ゲームに上乗せするファイルを置く場所。中はゲームフォルダと同じ並び（Data\\resources\\… など）");
        _toolTip.SetToolTip(_kojoCheck, "口上（eramegatenpKOJO）を本体と一緒に入れます。外すと次の同期で口上のファイルは消えます");
        _toolTip.SetToolTip(_launchBox, "起動する exe（同期先の直下）");
        _toolTip.SetToolTip(_trashLink, $"同期でゲームフォルダから消したファイルは、作業フォルダの「削除済み\\日時」に元の並びで退避します（直近 {FolderSync.TrashGenerations} 回分）");
        _toolTip.SetToolTip(_logLink, "このウィンドウのログと同じ内容を残したファイル（作業フォルダの 同期アプリ_ログ.txt）を開きます");

        _kojoCheck.CheckedChanged += (_, _) => TrySaveSettings();
        _launchAfterCheck.CheckedChanged += (_, _) => TrySaveSettings();
        _targetBox.Leave += (_, _) => { TrySaveSettings(); RefreshLaunchList(); };
        _launchBox.Leave += (_, _) => TrySaveSettings();
        _launchBox.SelectedIndexChanged += (_, _) => TrySaveSettings();
        _browseButton.Click += (_, _) => BrowseTarget();
        _configButton.Click += async (_, _) => await OpenConfigDialog();
        _allButton.Click += async (_, _) => await RunWithLock(RunAll);
        _updateButton.Click += async (_, _) => await RunWithLock(UpdateRepos);
        _syncButton.Click += async (_, _) => await RunWithLock(async () => await Sync());
        _importButton.Click += async (_, _) => await ChooseAndImport();
        _openExtraButton.Click += (_, _) => { Directory.CreateDirectory(ExtraDir); OpenShell(ExtraDir); };
        _launchButton.Click += (_, _) => Launch();
        _trashLink.LinkClicked += (_, _) => { Directory.CreateDirectory(TrashRoot); OpenShell(TrashRoot); };
        _logLink.LinkClicked += (_, _) => { if (File.Exists(LogPath)) OpenShell(LogPath); else OpenShell(WorkDir); };
        _uploaderLink.LinkClicked += (_, _) => { if (AppConfig.IsWebUrl(_config.ExtraUrl)) OpenShell(_config.ExtraUrl); };
        DragEnter += (_, e) => { if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy; };
        DragDrop += async (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths)
            {
                var zips = paths.Where(p => p.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && File.Exists(p)).ToArray();
                if (zips.Length > 0) await RunWithLock(() => ImportZips(zips));
            }
        };
        Shown += async (_, _) => await RefreshState();
    }

    private void LoadConfigToUi()
    {
        _config = AppConfig.Load(ConfigPath);
        _targetBox.Text = _config.TargetDir;
        _kojoCheck.Checked = _config.UseKojo;
        _launchAfterCheck.Checked = _config.LaunchAfterUpdate;
        RefreshLaunchList();
        RefreshUploaderLink();
    }

    private void RefreshUploaderLink()
    {
        _uploaderLink.Visible = AppConfig.IsWebUrl(_config.ExtraUrl);
        _toolTip.SetToolTip(_uploaderLink, _config.ExtraUrl + "\n（配布元が変わったら 詳細設定 → 追加画像の配布元 で変えられます）");
    }

    private void RefreshLaunchList()
    {
        string current = _launchBox.Text.Trim().Length > 0 ? _launchBox.Text.Trim() : _config.LaunchExe;
        _launchBox.Items.Clear();
        try
        {
            string t = _targetBox.Text.Trim();
            if (t.Length > 0 && Directory.Exists(t)) _launchBox.Items.AddRange(Directory.GetFiles(t, "*.exe").Select(Path.GetFileName).ToArray()!);
        }
        catch (Exception) { }
        _launchBox.Text = current;
    }

    private async Task RefreshState()
    {
        string? gitExe = GitClient.Find(_config.GitPath);
        var git = gitExe == null ? null : new GitClient(gitExe);
        var body = await RepoUpdater.GetState(git, BodyDir);
        var kojo = await RepoUpdater.GetState(git, KojoDir);
        string Describe(RepoState? s, string dir) => s != null ? $"{s.Sha}（{s.Date}）" : Directory.Exists(dir) ? "取得済み（版不明）" : "未取得";
        _stateLabel.Text = $"本体: {Describe(body, BodyDir)}　口上: {(_config.UseKojo ? Describe(kojo, KojoDir) : "入れない")}　"
            + (gitExe != null ? "git: あり（差分だけ取得）" : "git: なし（毎回 zip で丸ごと取得）");
        _toolTip.SetToolTip(_stateLabel, $"本体: {BodyDir}\n口上: {KojoDir}\n追加ファイル: {ExtraDir}\n作業フォルダ: {WorkDir}\ngit: {gitExe ?? "見つかりません"}"
            + (body != null ? $"\n本体の最新の更新: {body.Title}" : "") + (kojo != null ? $"\n口上の最新の更新: {kojo.Title}" : ""));
    }

    // 画面の入力を設定に反映して保存する。おかしな入力なら理由を返す
    private string? TrySaveSettings()
    {
        var c = _config.Clone();
        c.TargetDir = _targetBox.Text.Trim();
        c.UseKojo = _kojoCheck.Checked;
        c.LaunchAfterUpdate = _launchAfterCheck.Checked;
        c.LaunchExe = _launchBox.Text.Trim();
        string? error = c.Validate() ?? c.ValidateDirs(AppDirectory, null);
        if (error != null) return error;
        _config = c;
        _config.Save(ConfigPath);
        return null;
    }

    private bool CheckSettings()
    {
        string? error = TrySaveSettings();
        if (error == null) return true;
        MessageBox.Show(this, error, "設定を確認してください", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return false;
    }

    private async Task OpenConfigDialog()
    {
        TrySaveSettings();
        var old = _config.Clone();
        using var dialog = new ConfigDialog(_config, AppDirectory, ConfigPath);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var next = dialog.Result;
        next.TargetDir = _targetBox.Text.Trim();
        next.UseKojo = _kojoCheck.Checked;
        next.LaunchAfterUpdate = _launchAfterCheck.Checked;
        next.LaunchExe = _launchBox.Text.Trim();

        // 保存先を変えたら中身を移すか聞く（移さなければ新しい場所は空から）
        var moves = new List<(string Name, string From, string To)>();
        foreach (var (name, from, to) in new[]
        {
            ("本体", old.BodyPath(AppDirectory), next.BodyPath(AppDirectory)),
            ("口上", old.KojoPath(AppDirectory), next.KojoPath(AppDirectory)),
            ("追加ファイル", old.ExtraPath(AppDirectory), next.ExtraPath(AppDirectory)),
        })
        {
            if (PathUtil.SamePath(from, to) || PathUtil.IsEmptyOrMissing(from)) continue;
            if (!PathUtil.IsEmptyOrMissing(to)) { Log($"{name}の新しい保存先には既にファイルがあるので、移さずにそのまま使います: {to}"); continue; }
            if (PathUtil.IsSameOrUnder(to, from) || PathUtil.IsSameOrUnder(from, to)) continue;
            var answer = MessageBox.Show(this, $"{name}の保存先を変えます。\n\n{from}\n→ {to}\n\n今の中身を新しい場所へ移しますか？\n（いいえ: 移さず、新しい場所は空から始めます）",
                "保存先の変更", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel) { Log("保存先の変更をやめました"); return; }
            if (answer == DialogResult.Yes) moves.Add((name, from, to));
        }

        _config = next;
        _config.Save(ConfigPath);
        RefreshUploaderLink();
        foreach (var (name, from, to) in moves)
        {
            await RunWithLock(async () =>
            {
                Log($"{name}を移動中: {from} → {to}");
                await Task.Run(() => PathUtil.MoveDirectory(from, to, Log));
                Log($"{name}の移動が終わりました");
            });
        }
        Log("設定を保存しました");
        await RefreshState();
    }

    private void BrowseTarget()
    {
        using var dialog = new FolderBrowserDialog { Description = "同期先の ShinEraTenseiP フォルダを選んでください", SelectedPath = _targetBox.Text };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _targetBox.Text = dialog.SelectedPath;
        TrySaveSettings();
        RefreshLaunchList();
    }

    // 処理中はボタンを押せないようにし、例外はダイアログで知らせる
    private async Task RunWithLock(Func<Task> action)
    {
        SetBusy(true);
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Log("エラー: " + ex.Message);
            MessageBox.Show(this, ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
            await RefreshState();
        }
    }

    private void SetBusy(bool busy)
    {
        foreach (Control c in new Control[] { _browseButton, _configButton, _allButton, _updateButton, _syncButton, _importButton, _kojoCheck, _launchButton, _launchBox, _launchAfterCheck })
            c.Enabled = !busy;
        _targetBox.ReadOnly = busy;
        UseWaitCursor = busy;
    }

    private async Task RunAll()
    {
        if (!CheckSettings() || !CheckTarget(out _)) return;
        var sw = Stopwatch.StartNew();
        Log($"===== まとめて更新を開始（{DateTime.Now:yyyy/MM/dd HH:mm}）=====");
        await UpdateRepos();
        if (!await Sync()) return;
        Log($"===== まとめて更新が終わりました（{sw.Elapsed.TotalSeconds:0} 秒）=====");
        if (_config.LaunchAfterUpdate) Launch();
    }

    private async Task UpdateRepos()
    {
        if (!CheckSettings()) return;
        string? gitExe = GitClient.Find(_config.GitPath);
        if (gitExe == null && !string.IsNullOrWhiteSpace(_config.GitPath)) Log("指定された git.exe が見つかりません: " + _config.GitPath);
        if (gitExe == null) Log("git が見つからないので zip で丸ごと取得します（Git for Windows を入れると差分だけになり速くなります）");
        var targets = new List<(string Name, string Url, string Branch, string Dir)> { ("本体", _config.BodyUrl, _config.BodyBranch, BodyDir) };
        if (_config.UseKojo) targets.Add(("口上", _config.KojoUrl, _config.KojoBranch, KojoDir));
        foreach (var (name, url, branch, dir) in targets)
        {
            var progress = DownloadProgress();
            if (gitExe != null) await RepoUpdater.UpdateWithGit(new GitClient(gitExe), name, url, branch, dir, Log, progress);
            else await RepoUpdater.UpdateWithZip(name, url, branch, dir, WorkDir, Log, progress);
        }
    }

    private IProgress<(long done, long total)> DownloadProgress()
    {
        _progressBar.Maximum = 1000;
        _progressBar.Value = 0;
        return new Progress<(long done, long total)>(p =>
            _progressBar.Value = p.total > 0 ? (int)Math.Clamp(p.done * 1000 / p.total, 0, 1000) : (int)(p.done / 1_000_000 % 1000));
    }

    private static bool LooksLikeGame(string dir) =>
        Directory.Exists(Path.Combine(dir, "Data", "ERB")) && Directory.Exists(Path.Combine(dir, "Data", "CSV"));

    private bool CheckTarget(out string targetRoot)
    {
        targetRoot = _targetBox.Text.Trim();
        string? error;
        if (targetRoot.Length == 0) error = "同期先が空です。ゲームの ShinEraTenseiP フォルダを指定してください。";
        else
        {
            targetRoot = Path.GetFullPath(targetRoot);
            error = _config.ValidateDirs(AppDirectory, targetRoot);
            bool isEmpty = !Directory.Exists(targetRoot) || !Directory.EnumerateFileSystemEntries(targetRoot).Any();
            if (error == null && !LooksLikeGame(targetRoot) && !isEmpty)
                error = "同期先が ShinEraTenseiP のフォルダではないようです（Data\\ERB と Data\\CSV がありません）。";
        }
        if (error == null) return true;
        MessageBox.Show(this, error, "同期先を確認してください", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return false;
    }

    // 同期を最後まで行った（または差分なし）なら true
    private async Task<bool> Sync()
    {
        if (!CheckSettings() || !CheckTarget(out string targetRoot)) return false;
        if (!Directory.Exists(Path.Combine(BodyDir, "Data")))
        {
            MessageBox.Show(this, $"本体がまだありません。先に「本体・口上を更新」を実行してください。\n{BodyDir}", "同期できません", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        if (_config.UseKojo && !Directory.Exists(Path.Combine(KojoDir, "ERB")))
        {
            MessageBox.Show(this, $"口上がまだありません。先に「本体・口上を更新」を実行するか、「口上も入れる」を外してください。\n{KojoDir}", "同期できません", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        var running = RunningGames(targetRoot);
        if (running.Count > 0 && MessageBox.Show(this, $"ゲームが起動中です（{string.Join(", ", running)}）。\n起動中は書き換えに失敗することがあります。続けますか？",
                "ゲーム起動中", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return false;

        var layers = new List<SourceLayer> { FolderSync.BodyLayer(BodyDir) };
        if (_config.UseKojo) layers.Add(FolderSync.KojoLayer(KojoDir));
        Directory.CreateDirectory(ExtraDir);
        layers.Add(FolderSync.ExtraLayer(ExtraDir));
        var protect = new PathMatcher(_config.NoActionFolders);

        Log("差分を確認中...");
        var sw = Stopwatch.StartNew();
        var plan = await Task.Run(() => FolderSync.CreatePlan(layers, targetRoot, protect));

        // ゲームの Data\resources にだけある画像などは、消す前に「追加ファイル」へ取り込むか聞く
        var gameOnly = FolderSync.GameOnlyResources(plan);
        if (gameOnly.Count > 0)
        {
            bool? adopted = await AskAdoptGameOnly(gameOnly, targetRoot);
            if (adopted == null) return false;
            if (adopted == true)
            {
                sw.Restart();
                plan = await Task.Run(() => FolderSync.CreatePlan(layers, targetRoot, protect));
            }
        }

        const int MaxLines = 300;
        var report = new StringBuilder();
        foreach (var (_, rel) in plan.Copies.Take(MaxLines)) report.AppendLine("コピー: " + rel);
        if (plan.Copies.Count > MaxLines) report.AppendLine($"…ほか {plan.Copies.Count - MaxLines} 件のコピー");
        foreach (string rel in plan.DeleteFiles.Take(MaxLines)) report.AppendLine("削除: " + rel);
        if (plan.DeleteFiles.Count > MaxLines) report.AppendLine($"…ほか {plan.DeleteFiles.Count - MaxLines} 件の削除");
        foreach (string rel in plan.DeleteDirs.Take(MaxLines)) report.AppendLine("フォルダ削除: " + rel);
        if (plan.Overrides.Count > 0)
        {
            report.AppendLine($"※追加ファイルが本体・口上の同名ファイルより優先されています（{plan.Overrides.Count} 件）:");
            foreach (string o in plan.Overrides.Take(20)) report.AppendLine("  " + o);
        }
        long copyBytes = plan.Copies.Sum(c => new FileInfo(c.Src).Length);
        report.Append($"コピー {plan.Copies.Count} 件（{PathUtil.FormatBytes(copyBytes)}）/ 削除 {plan.DeleteFiles.Count} 件 / フォルダ削除 {plan.DeleteDirs.Count} 件"
            + (plan.Touches.Count > 0 ? $" / 日時合わせ {plan.Touches.Count} 件" : "") + $"（確認 {sw.Elapsed.TotalSeconds:0.0} 秒）");
        Log(report.ToString());

        int total = plan.Copies.Count + plan.Touches.Count + plan.DeleteFiles.Count + plan.DeleteDirs.Count;
        if (total == 0)
        {
            Log("ゲームフォルダは最新です");
            return true;
        }

        // ボタン一つで済ませたいので、確認は消すものがあるときだけ
        if (plan.DeleteFiles.Count + plan.DeleteDirs.Count > 0)
        {
            string sample = string.Join("\n", plan.DeleteFiles.Take(8).Select(r => "・" + r)) + (plan.DeleteFiles.Count > 8 ? $"\n…ほか {plan.DeleteFiles.Count - 8} 件（ログ参照）" : "");
            string question = $"{targetRoot}\n\nコピー {plan.Copies.Count} 件 / 削除 {plan.DeleteFiles.Count} 件 / フォルダ削除 {plan.DeleteDirs.Count} 件\n\n{sample}\n\n"
                + "配布元から消えたファイルや、本体・口上・追加ファイルのどれにもないファイルです。\n残したいものは先に「追加ファイル」へ移すか、詳細設定の「触らない場所」に入れてください。\n\n"
                + $"消すファイルは次の場所に退避します（直近 {FolderSync.TrashGenerations} 回分）:\n{TrashRoot}\n\n実行しますか？";
            if (MessageBox.Show(this, question, "同期の確認", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            {
                Log("同期をやめました");
                return false;
            }
        }

        _progressBar.Maximum = Math.Max(1, total);
        _progressBar.Value = 0;
        var progress = new Progress<int>(done => _progressBar.Value = Math.Min(done, _progressBar.Maximum));
        sw.Restart();
        string trashRoot = TrashRoot;
        string? trashDir = await Task.Run(() => FolderSync.Execute(plan, targetRoot, trashRoot, progress, Log));
        // 同期で Emuera.exe などが入った・名前が変わったときに、起動する exe の一覧を合わせる
        RefreshLaunchList();
        Log($"同期が終わりました（{sw.Elapsed.TotalSeconds:0.0} 秒）");
        if (trashDir != null) Log($"  消した {plan.DeleteFiles.Count} 件は退避しました: {trashDir}");
        return true;
    }

    /// <summary>
    /// ゲーム側にだけある Data\resources のファイルを、取り込んで残すか・同期で消すか・同期をやめるか聞く。
    /// 取り込んだら true、消すなら false、やめる（または取り込みに失敗した）なら null
    /// </summary>
    private async Task<bool?> AskAdoptGameOnly(List<string> gameOnly, string targetRoot)
    {
        long bytes = gameOnly.Sum(rel => { try { return new FileInfo(Path.Combine(targetRoot, rel)).Length; } catch (Exception) { return 0L; } });
        var folders = gameOnly.GroupBy(rel => Path.GetDirectoryName(rel) ?? "", StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase).ToList();
        string sample = string.Join("\n", folders.Take(10).Select(g => $"・{g.Key}（{g.Count():#,0} 件）"))
            + (folders.Count > 10 ? $"\n…ほか {folders.Count - 10} フォルダ" : "");
        Log($"ゲームの Data\\resources にだけあるファイル {gameOnly.Count:#,0} 件（{PathUtil.FormatBytes(bytes)}）:\n"
            + string.Join("\n", gameOnly.Take(300).Select(r => "  " + r)) + (gameOnly.Count > 300 ? $"\n  …ほか {gameOnly.Count - 300} 件" : ""));
        var answer = MessageBox.Show(this,
            $"ゲームの Data\\resources に、本体・口上・追加ファイルのどれにも無いファイルが {gameOnly.Count:#,0} 件（{PathUtil.FormatBytes(bytes)}）あります。\n\n{sample}\n\n"
            + "昔ゲームフォルダに直接入れた追加画像や自作の画像のほか、配布元で削除・改名された古い公式画像のこともあります（一覧はログにあります）。\n\n"
            + "「追加ファイル」に取り込んで残しますか？\n\n"
            + "はい：取り込んで残す（次からは追加ファイルとして同期します）\n"
            + "いいえ：同期で消す（work\\削除済み に退避します）\n"
            + "キャンセル：同期をやめる",
            "ゲーム側にだけあるファイル", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1);
        if (answer == DialogResult.Cancel)
        {
            Log("同期をやめました");
            return null;
        }
        if (answer == DialogResult.No)
        {
            Log($"ゲーム側にだけある {gameOnly.Count:#,0} 件は取り込まず、同期で消します（退避します）");
            return false;
        }

        Directory.CreateDirectory(ExtraDir);
        string extra = ExtraDir;
        var sw = Stopwatch.StartNew();
        var (adopted, errors) = await Task.Run(() => FolderSync.AdoptToExtra(gameOnly, targetRoot, extra));
        Log($"ゲーム側にだけあった {adopted:#,0} 件を「追加ファイル」に取り込みました（{sw.Elapsed.TotalSeconds:0.0} 秒）");
        if (errors.Count == 0) return true;

        // 取り込めなかったものを同期で消してしまわないよう、ここで止める
        Log($"取り込めなかったもの {errors.Count} 件:\n" + string.Join("\n", errors.Take(30).Select(e => "  " + e)));
        MessageBox.Show(this, $"ゲーム側にだけあるファイルのうち {errors.Count} 件を取り込めなかったため、同期をやめました。\n詳しくはログを見てください。",
            "取り込めませんでした", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return null;
    }

    private static List<string> RunningGames(string targetRoot)
    {
        var list = new List<string>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (!p.ProcessName.StartsWith("Emuera", StringComparison.OrdinalIgnoreCase)) continue;
                string? file = p.MainModule?.FileName;
                if (file != null && PathUtil.IsSameOrUnder(file, targetRoot)) list.Add(Path.GetFileName(file));
            }
            catch (Exception) { }
            finally { p.Dispose(); }
        }
        return list;
    }

    private void Launch()
    {
        string target = _targetBox.Text.Trim();
        string exe = Path.Combine(target, _launchBox.Text.Trim());
        if (_launchBox.Text.Trim().Length == 0 || !File.Exists(exe))
        {
            MessageBox.Show(this, "起動する exe が見つかりません: " + exe, "起動できません", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        TrySaveSettings();
        Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = target, UseShellExecute = true });
        Log("起動しました: " + exe);
    }

    private async Task ChooseAndImport()
    {
        using var dialog = new OpenFileDialog { Filter = "zip ファイル|*.zip", Multiselect = true, Title = "取り込む追加画像などの zip を選んでください" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        await RunWithLock(() => ImportZips(dialog.FileNames));
    }

    private async Task ImportZips(string[] zips)
    {
        foreach (string zip in zips)
        {
            var plan = await Task.Run(() => ExtraImport.Prepare(zip));
            if (plan.Items.Count == 0)
            {
                Log($"{Path.GetFileName(zip)}: 取り込めるファイルがありません");
                continue;
            }
            var folders = plan.Items.Select(i => Path.GetDirectoryName(i.Rel) ?? "").GroupBy(d => d).OrderBy(g => g.Key).ToList();
            string sample = string.Join("\n", folders.Take(10).Select(g => $"・{g.Key}（{g.Count()} 件）")) + (folders.Count > 10 ? $"\n…ほか {folders.Count - 10} フォルダ" : "");
            string skipped = plan.Skipped.Count > 0 ? $"\n\n取り込まないもの: {string.Join(", ", plan.Skipped.Take(5))}" : "";
            if (MessageBox.Show(this, $"{Path.GetFileName(zip)}\n\n{plan.Items.Count} ファイルを「追加ファイル」に取り込みます。ゲームフォルダでの置き場所:\n\n{sample}{skipped}\n\nよろしいですか？",
                    "追加画像の取り込み", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            {
                Log($"{Path.GetFileName(zip)}: 取り込みをやめました");
                continue;
            }
            var (written, kept) = await Task.Run(() => ExtraImport.Apply(plan, ExtraDir));
            Log($"{Path.GetFileName(zip)}: 取り込みました（新規・更新 {written} 件 / 手元と同じか新しいので据え置き {kept} 件）。次の同期でゲームに入ります");
        }
    }

    private static void OpenShell(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

    // 起動時、ログが大きくなっていたら .old.txt に回す（前の .old.txt は消える）
    private void RotateLog()
    {
        try
        {
            var info = new FileInfo(LogPath);
            if (info.Exists && info.Length > MaxLogBytes) File.Move(LogPath, Path.ChangeExtension(LogPath, ".old.txt"), true);
        }
        catch (Exception)
        {
            // ログが回せなくても動作には関係ない
        }
    }

    // バックグラウンド処理からも呼ばれるので UI スレッドに回す。ログファイルにも残す
    private void Log(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => Log(message));
            return;
        }
        _logBox.AppendText(message.Replace("\r\n", "\n").Replace("\n", Environment.NewLine) + Environment.NewLine);
        try
        {
            Directory.CreateDirectory(WorkDir);
            var sb = new StringBuilder();
            if (!_logStarted)
            {
                sb.Append($"\r\n===== {DateTime.Now:yyyy/MM/dd HH:mm:ss} 起動 =====\r\n");
                _logStarted = true;
            }
            sb.Append($"{DateTime.Now:HH:mm:ss} ").Append(message.Replace("\r\n", "\n").TrimEnd('\n').Replace("\n", "\r\n         ")).Append("\r\n");
            File.AppendAllText(LogPath, sb.ToString(), new UTF8Encoding(false));
        }
        catch (Exception)
        {
            // ログが書けなくても動作には関係ない
        }
    }
}
