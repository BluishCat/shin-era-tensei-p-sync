namespace ShinEraTenseiP同期アプリ;

/// <summary>同期元の一つ。Map はその中の相対パスを同期先の相対パスにする（null なら入れない）</summary>
record SourceLayer(string Name, string Root, Func<string, string?> Map);

record SyncPlan(
    List<(string Src, string Rel)> Copies,
    List<(string Src, string Rel)> Touches,   // 中身は同じで日時だけ違う → 日時だけ合わせる（次回からの比較が速くなる）
    List<string> DeleteFiles,
    List<string> DeleteDirs,
    List<string> Overrides);                  // 追加ファイルが本体・口上の同名ファイルを上書きしているもの

static class FolderSync
{
    public static SourceLayer BodyLayer(string root) => new("本体", root, rel => FirstPart(rel).Equals(".git", StringComparison.OrdinalIgnoreCase) ? null : rel);

    // 口上リポジトリの ERB\ はゲームの Data\ERB\ に、それ以外（KOJO_README.md など）はゲームフォルダ直下に入る
    public static SourceLayer KojoLayer(string root) => new("口上", root, rel =>
    {
        string first = FirstPart(rel);
        if (first.StartsWith(".git", StringComparison.OrdinalIgnoreCase)) return null;
        return first.Equals("ERB", StringComparison.OrdinalIgnoreCase) ? Path.Combine("Data", rel) : rel;
    });

    public static SourceLayer ExtraLayer(string root) => new("追加ファイル", root, rel => rel);

    private static string FirstPart(string rel) { int i = rel.IndexOf('\\'); return i < 0 ? rel : rel[..i]; }

    /// <summary>
    /// 同期先を「本体 → 口上 → 追加ファイル」の重ね合わせと同じにする操作を求める（後のものが優先）。
    /// 保護対象と同期先直下のファイルは消さない
    /// </summary>
    public static SyncPlan CreatePlan(IReadOnlyList<SourceLayer> layers, string targetRoot, PathMatcher protect)
    {
        var desired = new Dictionary<string, (string Src, string Layer)>(StringComparer.OrdinalIgnoreCase);
        var overrides = new List<string>();
        foreach (var layer in layers)
        {
            if (!Directory.Exists(layer.Root)) continue;
            foreach (string file in EnumerateFilesSkippingGit(layer.Root))
            {
                string? rel = layer.Map(Path.GetRelativePath(layer.Root, file));
                if (rel == null || protect.IsProtected(rel)) continue;
                if (desired.TryGetValue(rel, out var prev) && prev.Layer != layer.Name) overrides.Add($"{rel}（{prev.Layer}より{layer.Name}を優先）");
                desired[rel] = (file, layer.Name);
            }
        }

        var copies = new List<(string, string)>();
        var touches = new List<(string, string)>();
        foreach (var (rel, (src, _)) in desired)
        {
            switch (Compare(src, Path.Combine(targetRoot, rel)))
            {
                case FileDiff.Different: copies.Add((src, rel)); break;
                case FileDiff.TimeOnly: touches.Add((src, rel)); break;
            }
        }

        var deleteFiles = new List<string>();
        var deleteDirs = new List<string>();
        if (Directory.Exists(targetRoot))
        {
            // 残るファイルがあるフォルダは消さない
            var keepDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void KeepWithParents(string? dir)
            {
                for (; !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir)) if (!keepDirs.Add(dir)) break;
            }
            foreach (string rel in desired.Keys) KeepWithParents(Path.GetDirectoryName(rel));

            var walked = new List<string>();
            void Walk(string relDir)
            {
                string abs = relDir.Length == 0 ? targetRoot : Path.Combine(targetRoot, relDir);
                foreach (string file in Directory.EnumerateFiles(abs))
                {
                    if (relDir.Length == 0) continue; // 直下のファイルはユーザーが置いたもの（EmueraEX.exe など）
                    string rel = Path.GetRelativePath(targetRoot, file);
                    if (desired.ContainsKey(rel) || protect.IsProtected(rel)) KeepWithParents(relDir);
                    else deleteFiles.Add(rel);
                }
                foreach (string dir in Directory.EnumerateDirectories(abs))
                {
                    string rel = Path.GetRelativePath(targetRoot, dir);
                    if (protect.IsProtected(rel)) { KeepWithParents(rel); continue; }
                    walked.Add(rel);
                    Walk(rel);
                }
            }
            Walk("");
            deleteDirs = walked.Where(d => !keepDirs.Contains(d) && !protect.MayContainProtected(d)).OrderByDescending(d => d.Length).ToList();
        }
        return new SyncPlan(copies, touches, deleteFiles, deleteDirs, overrides);
    }

    private static IEnumerable<string> EnumerateFilesSkippingGit(string root)
    {
        foreach (string f in Directory.EnumerateFiles(root)) yield return f;
        foreach (string d in Directory.EnumerateDirectories(root))
        {
            if (Path.GetFileName(d).Equals(".git", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (string f in Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories)) yield return f;
        }
    }

    public static void Execute(SyncPlan plan, string targetRoot, IProgress<int> progress, Action<string> log)
    {
        int done = 0;
        foreach (var (src, rel) in plan.Copies)
        {
            string dest = Path.Combine(targetRoot, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            if (File.Exists(dest)) File.SetAttributes(dest, FileAttributes.Normal);
            File.Copy(src, dest, true);
            if (++done % 50 == 0) progress.Report(done);
        }
        foreach (var (src, rel) in plan.Touches)
        {
            string dest = Path.Combine(targetRoot, rel);
            File.SetAttributes(dest, FileAttributes.Normal);
            File.SetLastWriteTimeUtc(dest, File.GetLastWriteTimeUtc(src));
            if (++done % 200 == 0) progress.Report(done);
        }
        foreach (string rel in plan.DeleteFiles)
        {
            string path = Path.Combine(targetRoot, rel);
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
            if (++done % 50 == 0) progress.Report(done);
        }
        foreach (string rel in plan.DeleteDirs)
        {
            string path = Path.Combine(targetRoot, rel);
            if (Directory.EnumerateFileSystemEntries(path).Any()) log("  空でないので残しました: " + rel);
            else Directory.Delete(path);
            progress.Report(++done);
        }
        progress.Report(done);
    }

    private enum FileDiff { Same, TimeOnly, Different }

    // サイズと更新日時が同じなら同一、日時だけ違えば中身を比べる
    private static FileDiff Compare(string src, string dest)
    {
        var d = new FileInfo(dest);
        if (!d.Exists) return FileDiff.Different;
        var s = new FileInfo(src);
        if (s.Length != d.Length) return FileDiff.Different;
        if (s.LastWriteTimeUtc == d.LastWriteTimeUtc) return FileDiff.Same;
        using var a = s.OpenRead();
        using var b = d.OpenRead();
        return PathUtil.StreamsEqual(a, b) ? FileDiff.TimeOnly : FileDiff.Different;
    }
}
