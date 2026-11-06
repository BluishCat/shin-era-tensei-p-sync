using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace ShinEraTenseiP同期アプリ;

/// <summary>
/// 追加画像などの zip を「追加ファイル」フォルダへ、ゲームフォルダと同じ並び（Data\resources\…）で取り込む
/// </summary>
static class ExtraImport
{
    public record Item(int EntryIndex, string Rel, long Length, DateTimeOffset Time);
    public record Plan(string ZipPath, List<Item> Items, List<string> Skipped);

    private static readonly string[] SkipNames = ["thumbs.db", "desktop.ini", ".ds_store"];
    private static readonly string[] TextExt = [".txt", ".md"];

    public static Plan Prepare(string zipPath)
    {
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Read, Encoding.Latin1);
        var files = new List<(int Index, string[] Parts, ZipArchiveEntry E)>();
        var skipped = new List<string>();
        for (int i = 0; i < zip.Entries.Count; i++)
        {
            var e = zip.Entries[i];
            string name = DecodeName(e).Replace('\\', '/');
            if (name.EndsWith('/')) continue;
            var parts = name.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || parts[0] == "__MACOSX" || SkipNames.Contains(parts[^1].ToLowerInvariant())) continue;
            if (parts.Any(p => p == ".." || p == ".")) { skipped.Add(name + "（不正なパス）"); continue; }
            files.Add((i, parts, e));
        }

        // 全体を包むだけのフォルダ（「[0.4.11]ShinEratenseiP_追加画像」など）は外す。
        // Data / resources と、番号で始まる分類フォルダ（50_依頼画像 など）は中身の一部なので外さない
        int strip = 0;
        while (files.Count > 0 && files.All(f => f.Parts.Length > strip + 1))
        {
            string top = files[0].Parts[strip];
            if (!files.All(f => f.Parts[strip].Equals(top, StringComparison.OrdinalIgnoreCase))) break;
            if (IsContentFolder(top)) break;
            strip++;
        }

        var items = new List<Item>();
        foreach (var (index, parts, e) in files)
        {
            var rest = parts.Skip(strip).ToArray();
            if (rest.Length == 1 && TextExt.Contains(Path.GetExtension(rest[0]).ToLowerInvariant()))
            {
                skipped.Add(string.Join('/', rest) + "（説明ファイル）");
                continue;
            }
            string rel = rest[0].Equals("Data", StringComparison.OrdinalIgnoreCase) ? Path.Combine(rest)
                : rest[0].Equals("resources", StringComparison.OrdinalIgnoreCase) ? Path.Combine(["Data", .. rest])
                : Path.Combine(["Data", "resources", .. rest]);
            items.Add(new Item(index, rel, e.Length, e.LastWriteTime));
        }
        return new Plan(zipPath, items, skipped);
    }

    private static bool IsContentFolder(string name) =>
        name.Equals("Data", StringComparison.OrdinalIgnoreCase) || name.Equals("resources", StringComparison.OrdinalIgnoreCase) || Regex.IsMatch(name, @"^\d");

    /// <summary>取り込む。手元の方が新しい同名ファイルは上書きしない。(書き込み, そのまま) を返す</summary>
    public static (int written, int kept) Apply(Plan plan, string extraRoot)
    {
        using var zip = ZipFile.Open(plan.ZipPath, ZipArchiveMode.Read, Encoding.Latin1);
        int written = 0, kept = 0;
        string root = Path.GetFullPath(extraRoot);
        foreach (var item in plan.Items)
        {
            string dest = Path.GetFullPath(Path.Combine(root, item.Rel));
            if (!PathUtil.IsSameOrUnder(dest, root)) continue;
            var local = new FileInfo(dest);
            if (local.Exists && (local.Length == item.Length || local.LastWriteTimeUtc > item.Time.UtcDateTime)) { kept++; continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            if (local.Exists) File.SetAttributes(dest, FileAttributes.Normal);
            zip.Entries[item.EntryIndex].ExtractToFile(dest, true);
            written++;
        }
        return (written, kept);
    }

    // UTF-8 と Shift_JIS が混ざった zip でも 1 件ずつ正しく読む
    private static string DecodeName(ZipArchiveEntry e)
    {
        string n = e.FullName;
        if (n.Any(c => c > 0xFF)) return n; // UTF-8 フラグ付きは既に読めている
        byte[] bytes = Encoding.Latin1.GetBytes(n);
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(932).GetString(bytes);
        }
    }
}
