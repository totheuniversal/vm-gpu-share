using System.IO;
using System.Text.Json;

namespace VM_Manager
{
    /// <summary>User choices kept between runs, in %APPDATA%\VM GPU Share\settings.json.</summary>
    public sealed class AppSettings
    {
        static readonly string AppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        static readonly string FilePath = Path.Combine(AppData, "VM GPU Share", "settings.json");
        // Where versions before 1.1.0 (named "VM Manager") kept them
        static readonly string OldFilePath = Path.Combine(AppData, "VM Manager", "settings.json");

        /// <summary>System, Light or Dark.</summary>
        public string Theme { get; set; } = "System";
        public int GpuShare { get; set; } = 80;
        public string? LastVm { get; set; }
        /// <summary>Folder inside the VM that the Copy files tab copies into.</summary>
        public string CopyDestination { get; set; } = @"C:\Users\SLW\Desktop\Share";
        /// <summary>Monitors (\\.\DISPLAYn) Remote Desktop uses; null means all of them.</summary>
        public List<string>? RdpScreens { get; set; }
        public double? Left { get; set; }
        public double? Top { get; set; }
        public double Width { get; set; } = 1180;
        public double Height { get; set; } = 760;
        public bool Maximized { get; set; }

        public static AppSettings Load()
        {
            try
            {
                string path = File.Exists(FilePath) ? FilePath : OldFilePath;
                if (File.Exists(path))
                    return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
