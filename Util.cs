using System.Text.RegularExpressions;

namespace ShinEraTenseiP同期アプリ;

static class Http
{
    public static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(60) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) ShinEraTenseiP-sync/1.0");
        return client;
    }

    // total は分からなければ 0
    public static async Task DownloadFile(string url, string destPath, IProgress<(long done, long total)> progress)
    {
        using var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        long total = response.Content.Headers.ContentLength ?? 0;
        await using var source = await response.Content.ReadAsStreamAsync();
        await using var dest = File.Create(destPath);
        var buffer = new byte[1 << 20];
        long done = 0;
        int read;
        while ((read = await source.ReadAsync(buffer)) > 0)
        {
            await dest.WriteAsync(buffer.AsMemory(0, read));
            done += read;
            progress.Report((done, total));
        }
    }
}

static class PathUtil
{
    public static bool IsSameOrUnder(string path, string root)
    {
        string p = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        string r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        return p.Equals(r, StringComparison.OrdinalIgnoreCase)
            || p.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    public static bool SamePath(string a, string b) =>
        Path.GetFullPath(a).TrimEnd('\\').Equals(Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    public static bool IsEmptyOrMissing(string dir) => !Directory.Exists(dir) || !Directory.EnumerateFileSystemEntries(dir).Any();

    /// <summary>フォルダの中身を丸ごと移す。同じドライブなら一瞬、別ドライブならコピーしてから消す</summary>
    public static void MoveDirectory(string from, string to, Action<string> log)
    {
        from = Path.GetFullPath(from);
        to = Path.GetFullPath(to);
        if (Path.GetPathRoot(from)!.Equals(Path.GetPathRoot(to), StringComparison.OrdinalIgnoreCase))
        {
            if (Directory.Exists(to)) Directory.Delete(to); // 空であることは呼び出し側で確認済み
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            Directory.Move(from, to);
            return;
        }
        var files = Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories).ToList();
        int done = 0;
        foreach (string file in files)
        {
            string dest = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, true);
            if (++done % 2000 == 0) log($"  移動中 {done}/{files.Count}");
        }
        foreach (string dir in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
        }
        DeleteDirectoryForce(from);
    }

    /// <summary>読み取り専用（git のパックなど）が混ざっていても消す</summary>
    public static void DeleteDirectoryForce(string dir)
    {
        foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(dir, true);
    }

    public static bool StreamsEqual(Stream a, Stream b)
    {
        byte[] bufA = new byte[81920];
        byte[] bufB = new byte[81920];
        while (true)
        {
            int readA = a.ReadAtLeast(bufA, bufA.Length, throwOnEndOfStream: false);
            int readB = b.ReadAtLeast(bufB, bufB.Length, throwOnEndOfStream: false);
            if (readA != readB) return false;
            if (readA == 0) return true;
            if (!bufA.AsSpan(0, readA).SequenceEqual(bufB.AsSpan(0, readB))) return false;
        }
    }

    public static string FormatBytes(long bytes) =>
        bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.00} GB" : bytes >= 1L << 20 ? $"{bytes / (double)(1L << 20):0.0} MB" : $"{bytes / 1024.0:0.0} KB";
}

/// <summary>
/// 「同期で触らない場所」の判定。/ を含まない名前はどの階層の名前とも比べ、含むものは先頭から比べる。* と ? が使える
/// </summary>
class PathMatcher
{
    private readonly List<Regex> _anyLevel = [];
    private readonly List<Regex[]> _rooted = [];
    public IReadOnlyList<string> Patterns { get; }

    public PathMatcher(IEnumerable<string> patterns)
    {
        var list = new List<string>();
        foreach (var raw in patterns)
        {
            string p = raw.Trim().Replace('/', '\\').Trim('\\');
            if (p.Length == 0) continue;
            list.Add(p);
            if (p.Contains('\\')) _rooted.Add(p.Split('\\').Select(Glob).ToArray());
            else _anyLevel.Add(Glob(p));
        }
        Patterns = list;
    }

    private static Regex Glob(string s) =>
        new("^" + Regex.Escape(s).Replace(@"\*", ".*").Replace(@"\?", ".") + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>relPath（ファイルでもフォルダでも）が保護対象か、保護対象の中にあるか</summary>
    public bool IsProtected(string relPath)
    {
        var parts = relPath.Split('\\');
        foreach (var part in parts)
        {
            foreach (var g in _anyLevel) if (g.IsMatch(part)) return true;
        }
        foreach (var pat in _rooted)
        {
            if (parts.Length < pat.Length) continue;
            bool all = true;
            for (int i = 0; i < pat.Length && all; i++) all = pat[i].IsMatch(parts[i]);
            if (all) return true;
        }
        return false;
    }

    /// <summary>フォルダ relDir の下に保護対象のパターンがあるか（あれば空でもフォルダを消さない）</summary>
    public bool MayContainProtected(string relDir)
    {
        var parts = relDir.Split('\\');
        foreach (var pat in _rooted)
        {
            if (pat.Length <= parts.Length) continue;
            bool all = true;
            for (int i = 0; i < parts.Length && all; i++) all = pat[i].IsMatch(parts[i]);
            if (all) return true;
        }
        return false;
    }
}
