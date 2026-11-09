using System.IO;

namespace VM_Manager
{
    /// <summary>A file or folder on this PC waiting to be copied into a VM (Copy files tab).</summary>
    public sealed class CopyItem
    {
        CopyItem(string path, bool isFolder, int files, long bytes)
        {
            Path = path;
            IsFolder = isFolder;
            Files = files;
            Bytes = bytes;
        }

        public string Path { get; }
        public bool IsFolder { get; }
        public int Files { get; }
        public long Bytes { get; }

        public string Name => System.IO.Path.GetFileName(Path.TrimEnd('\\')) is { Length: > 0 } name ? name : Path;
        public string Glyph => IsFolder ? "" : "";   // FolderHorizontal / Page
        public string Detail => IsFolder ? $"Folder · {Plural(Files, "file")} · {FormatSize(Bytes)}" : FormatSize(Bytes);

        /// <summary>Reads a path's size; folders are counted with everything in them, hidden files included
        /// (the copy includes them too). Returns null if the path doesn't exist.</summary>
        public static CopyItem? FromPath(string path)
        {
            path = System.IO.Path.GetFullPath(path.Trim().Trim('"'));
            if (File.Exists(path)) return new CopyItem(path, false, 1, new FileInfo(path).Length);
            if (!Directory.Exists(path)) return null;

            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 };
            int files = 0;
            long bytes = 0;
            foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", options))
            {
                files++;
                bytes += file.Length;
            }
            return new CopyItem(path, true, files, bytes);
        }

        public static string FormatSize(long bytes) =>
            bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.0} GB" :
            bytes >= 1L << 20 ? $"{bytes / (double)(1L << 20):0.0} MB" :
            bytes >= 1L << 10 ? $"{bytes / 1024.0:0} KB" :
            Plural(bytes, "byte");

        public static string Plural(long count, string word) => count == 1 ? $"1 {word}" : $"{count:N0} {word}s";
    }
}
