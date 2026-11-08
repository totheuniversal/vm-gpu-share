using System.IO;
using System.Text.Json;

namespace VM_Manager
{
    /// <summary>User choices kept between runs, in %APPDATA%\VM Manager\settings.json.</summary>
    public sealed class AppSettings
    {
        static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VM Manager", "settings.json");

        /// <summary>System, Light or Dark.</summary>
        public string Theme { get; set; } = "System";
        public int GpuShare { get; set; } = 80;
        public string? LastVm { get; set; }
        public double? Left { get; set; }
        public double? Top { get; set; }
        public double Width { get; set; } = 1180;
        public double Height { get; set; } = 760;
        public bool Maximized { get; set; }

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
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
