using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ShinEraTenseiP同期アプリ;

/// <summary>git.exe を呼ぶ（差分だけ取れるので gitgud の遅い DL でも速い）</summary>
class GitClient
{
    public string Exe { get; }
    public GitClient(string exe) { Exe = exe; }

    private static readonly string[] CommonConfig =
        ["-c", "core.autocrlf=false", "-c", "core.longpaths=true", "-c", "core.quotepath=false", "-c", "safe.directory=*"];

    public static string? Find(string configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            string p = Environment.ExpandEnvironmentVariables(configured.Trim().Trim('"'));
            return File.Exists(p) ? Path.GetFullPath(p) : null;
        }
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                string p = Path.Combine(dir.Trim().Trim('"'), "git.exe");
                if (File.Exists(p)) return p;
            }
            catch (ArgumentException) { }
        }
        string[] candidates =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "cmd", "git.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Git", "cmd", "git.exe"),
        ];
        return candidates.FirstOrDefault(File.Exists);
    }

    public async Task<(int Code, string Out, string Err)> Run(string? workDir, IEnumerable<string> args, Action<string>? onErrLine = null)
    {
        var psi = new ProcessStartInfo(Exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };
        if (workDir != null) psi.WorkingDirectory = workDir;
        foreach (var a in CommonConfig) psi.ArgumentList.Add(a);
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        psi.Environment["LC_ALL"] = "C";

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("git を起動できませんでした: " + Exe);
        var outTask = p.StandardOutput.ReadToEndAsync();
        var err = new StringBuilder();
        var errTask = Task.Run(async () =>
        {
            // 進捗は \r で上書きされてくるので \r でも区切る
            var buf = new char[4096];
            var line = new StringBuilder();
            int n;
            void Flush()
            {
                if (line.Length == 0) return;
                string s = line.ToString();
                line.Clear();
                onErrLine?.Invoke(s);
                if (!Regex.IsMatch(s, @"\d+% \(")) err.AppendLine(s);
            }
            while ((n = await p.StandardError.ReadAsync(buf, 0, buf.Length)) > 0)
            {
                for (int i = 0; i < n; i++)
                {
                    if (buf[i] == '\r' || buf[i] == '\n') Flush();
                    else line.Append(buf[i]);
                }
            }
            Flush();
        });
        await p.WaitForExitAsync();
        string output = await outTask;
        await errTask;
        return (p.ExitCode, output, err.ToString());
    }

    public async Task<string> Check(string? workDir, IEnumerable<string> args, Action<string>? onErrLine = null)
    {
        var list = args.ToList();
        var (code, output, err) = await Run(workDir, list, onErrLine);
        if (code != 0) throw new InvalidOperationException($"git {string.Join(' ', list)} が失敗しました（{code}）\n{err.Trim()}");
        return output;
    }

    public async Task<string?> RevParse(string dir, string rev)
    {
        var (code, output, _) = await Run(dir, ["rev-parse", "--verify", "--quiet", rev + "^{commit}"]);
        return code == 0 ? output.Trim() : null;
    }
}

record RepoState(string Sha, string Date, string Title);

static class RepoUpdater
{
    /// <summary>今の版（git でなければ null）</summary>
    public static async Task<RepoState?> GetState(GitClient? git, string dir)
    {
        if (git == null || !Directory.Exists(Path.Combine(dir, ".git"))) return null;
        var (code, output, _) = await git.Run(dir, ["log", "-1", "--format=%h%n%cd%n%s", "--date=format:%Y-%m-%d %H:%M"]);
        if (code != 0) return null;
        var lines = output.Split('\n');
        return lines.Length >= 3 ? new RepoState(lines[0].Trim(), lines[1].Trim(), lines[2].Trim()) : null;
    }

    /// <summary>
    /// dir を取得元の最新版と同じにする（浅い取得で差分だけ落とす）。手元の変更や余計なファイルは消える
    /// </summary>
    public static async Task UpdateWithGit(GitClient git, string name, string url, string branch, string dir, Action<string> log, IProgress<(long done, long total)> progress)
    {
        Directory.CreateDirectory(dir);
        string gitUrl = ToGitUrl(url);
        if (!Directory.Exists(Path.Combine(dir, ".git")))
        {
            log($"{name}: 初回なので取得用のリポジトリを作ります（{dir}）");
            await git.Check(dir, ["init", "-q"]);
        }
        var (code, current, _) = await git.Run(dir, ["remote", "get-url", "origin"]);
        if (code != 0) await git.Check(dir, ["remote", "add", "origin", gitUrl]);
        else if (current.Trim() != gitUrl) await git.Check(dir, ["remote", "set-url", "origin", gitUrl]);
        foreach (var (k, v) in new[] { ("core.autocrlf", "false"), ("core.longpaths", "true"), ("core.quotepath", "false") })
            await git.Check(dir, ["config", k, v]);

        string? oldSha = await git.RevParse(dir, "HEAD");
        string remoteRef = "refs/remotes/origin/" + branch;
        log($"{name}: 最新版を確認中（{gitUrl} / {branch}）");
        var sw = Stopwatch.StartNew();
        string received = "";
        await git.Check(dir, ["fetch", "--depth", "1", "--no-tags", "--progress", "origin", $"+refs/heads/{branch}:{remoteRef}"], line =>
        {
            var m = Regex.Match(line, @"^(Receiving objects|Resolving deltas):\s+(\d+)%");
            if (!m.Success) return;
            int pct = int.Parse(m.Groups[2].Value);
            // 受信を 0〜90%、差分の解決を 90〜100% として見せる
            progress.Report((m.Groups[1].Value.StartsWith("Receiving") ? pct * 9 : 900 + pct, 1000));
            if (m.Groups[1].Value.StartsWith("Receiving")) received = line.Trim();
        });
        string newSha = await git.RevParse(dir, remoteRef) ?? throw new InvalidOperationException($"{name}: ブランチ {branch} が見つかりません");
        log($"  取得 {sw.Elapsed.TotalSeconds:0.0} 秒" + (received.Length > 0 ? "（" + Regex.Replace(received, @"^Receiving objects:\s+", "") + "）" : "（新しいデータなし）"));

        // 毎回チェックアウトし直す（手で触ったファイルや前回の残りも元に戻る）
        await git.Check(dir, ["checkout", "-q", "-f", "-B", branch, newSha]);
        await git.Check(dir, ["clean", "-q", "-ffdx"]);
        progress.Report((1000, 1000));

        var state = await GetState(git, dir);
        if (oldSha == newSha)
        {
            log($"{name}: 最新です（{state?.Sha} {state?.Date}）");
            return;
        }
        if (oldSha == null)
        {
            log($"{name}: 取得しました（{state?.Sha} {state?.Date} {state?.Title}）");
            return;
        }

        var (dcode, diff, _) = await git.Run(dir, ["diff", "--name-status", "-M", oldSha, newSha]);
        if (dcode == 0)
        {
            var kinds = diff.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l[0]).ToList();
            log($"{name}: 更新しました → {state?.Sha} {state?.Date}（追加 {kinds.Count(k => k == 'A')} / 変更 {kinds.Count(k => k == 'M')} / 削除 {kinds.Count(k => k == 'D')} / 名前変更 {kinds.Count(k => k == 'R')} ファイル）");
        }
        else log($"{name}: 更新しました → {state?.Sha} {state?.Date}");
        await LogCommitTitles(git, dir, url, branch, oldSha, log);
    }

    // gitgud（GitLab）の API で、前回から増えた更新の題名を出す。取れなくても更新自体には関係ない
    private static async Task LogCommitTitles(GitClient git, string dir, string url, string branch, string oldSha, Action<string> log)
    {
        try
        {
            var (code, since, _) = await git.Run(dir, ["log", "-1", "--format=%cI", oldSha]);
            if (code != 0) return;
            var u = new Uri(ToWebUrl(url));
            string project = Uri.EscapeDataString(u.AbsolutePath.Trim('/'));
            string api = $"{u.Scheme}://{u.Host}/api/v4/projects/{project}/repository/commits?ref_name={Uri.EscapeDataString(branch)}&since={Uri.EscapeDataString(since.Trim())}&per_page=100";
            using var doc = JsonDocument.Parse(await Http.Client.GetStringAsync(api));
            var titles = doc.RootElement.EnumerateArray()
                .Where(c => !c.GetProperty("id").GetString()!.StartsWith(oldSha, StringComparison.OrdinalIgnoreCase))
                .Select(c => $"    {DateTime.Parse(c.GetProperty("committed_date").GetString()!):MM/dd HH:mm} {c.GetProperty("title").GetString()}")
                .ToList();
            if (titles.Count == 0) return;
            const int Max = 15;
            log($"  今回入った更新 {titles.Count}{(titles.Count >= 100 ? "+" : "")} 件:");
            foreach (var t in titles.Take(Max)) log(t);
            if (titles.Count > Max) log($"    …ほか {titles.Count - Max} 件");
        }
        catch (Exception ex)
        {
            log("  （更新内容の一覧は取得できませんでした: " + ex.Message + "）");
        }
    }

    /// <summary>git が無いとき用。毎回 zip を丸ごと落として dir を同じにする</summary>
    public static async Task UpdateWithZip(string name, string url, string branch, string dir, string workDir, Action<string> log, IProgress<(long done, long total)> progress)
    {
        string web = ToWebUrl(url);
        string repo = web.TrimEnd('/').Split('/').Last();
        string zipUrl = $"{web}/-/archive/{branch}/{repo}-{branch}.zip";
        Directory.CreateDirectory(workDir);
        string zipPath = Path.Combine(workDir, $"{repo}-{branch}.zip");
        log($"{name}: zip を丸ごとダウンロード中（git が無いため。時間がかかります）: {zipUrl}");
        var sw = Stopwatch.StartNew();
        await Http.DownloadFile(zipUrl, zipPath, progress);
        log($"  {PathUtil.FormatBytes(new FileInfo(zipPath).Length)} / {sw.Elapsed.TotalSeconds:0} 秒");
        try
        {
            var (updated, deleted) = await Task.Run(() => SyncWithZip(zipPath, dir));
            log($"{name}: 更新しました（書き換え {updated} / 削除 {deleted} ファイル）");
        }
        finally
        {
            File.Delete(zipPath);
        }
    }

    private static (int updated, int deleted) SyncWithZip(string zipPath, string dir)
    {
        Directory.CreateDirectory(dir);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int updated = 0, deleted = 0;
        using (var archive = ZipFile.OpenRead(zipPath))
        {
            foreach (var entry in archive.Entries)
            {
                if (entry.FullName.EndsWith('/')) continue;
                // 先頭の「リポジトリ名-ブランチ名/」を除く
                string rel = entry.FullName[(entry.FullName.IndexOf('/') + 1)..].Replace('/', '\\');
                if (rel.Split('\\').Contains("..")) continue;
                files.Add(rel);
                string local = Path.Combine(dir, rel);
                if (File.Exists(local) && new FileInfo(local).Length == entry.Length)
                {
                    using var a = entry.Open();
                    using var b = File.OpenRead(local);
                    if (PathUtil.StreamsEqual(a, b)) continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(local)!);
                entry.ExtractToFile(local, true);
                updated++;
            }
        }
        foreach (string local in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).ToList())
        {
            string rel = Path.GetRelativePath(dir, local);
            if (files.Contains(rel) || rel.StartsWith(".git\\", StringComparison.OrdinalIgnoreCase)) continue;
            File.SetAttributes(local, FileAttributes.Normal);
            File.Delete(local);
            deleted++;
        }
        foreach (string d in Directory.EnumerateDirectories(dir, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length).ToList())
        {
            if (!Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d);
        }
        return (updated, deleted);
    }

    public static string ToWebUrl(string url) => Regex.Replace(url.Trim(), @"(\.git)?/*$", "");
    public static string ToGitUrl(string url) => ToWebUrl(url) + ".git";
}
