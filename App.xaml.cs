using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace VM_Manager
{
    public partial class App : Application
    {
        public static AppSettings Settings { get; } = AppSettings.Load();

        public App()
        {
            // Show and record unexpected errors instead of closing silently
            DispatcherUnhandledException += OnUnhandledException;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            // After App.xaml is loaded (it sets ThemeMode="System"), before MainWindow opens
            ApplyTheme(Settings.Theme);
            base.OnStartup(e);
        }

        /// <summary>System, Light or Dark (Fluent theme); applies immediately to all windows.</summary>
        public static void ApplyTheme(string theme)
        {
            Current.ThemeMode = theme switch
            {
                "Light" => ThemeMode.Light,
                "Dark" => ThemeMode.Dark,
                _ => ThemeMode.System,
            };
        }

        void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            string log = Path.Combine(AppContext.BaseDirectory, "error.log");
            try { File.AppendAllText(log, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {e.Exception}{Environment.NewLine}"); } catch (IOException) { }
            MessageBox.Show(e.Exception.Message, "VM Manager error", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }
    }
}
