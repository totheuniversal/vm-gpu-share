using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;
using System.Windows.Threading;
using VM_Manager.HyperV;

namespace VM_Manager
{
    /// <summary>
    /// Start/stop Hyper-V VMs and share the host NVIDIA GPU with them (GPU-P).
    /// Hyper-V work runs in two background PowerShell workers: one for the
    /// list refresh, one for longer tasks, so neither blocks the other.
    /// </summary>
    public partial class MainWindow : Window
    {
        const string MoonlightPath = @"C:\Program Files\Moonlight Game Streaming\Moonlight.exe";

        readonly AppSettings settings = App.Settings;
        readonly ObservableCollection<VmRow> vms = new();
        readonly DispatcherTimer refreshTimer = new() { Interval = TimeSpan.FromSeconds(3) };
        readonly DispatcherTimer infoTimer = new() { Interval = TimeSpan.FromSeconds(8) };
        PowerShellWorker? refreshWorker;
        PowerShellWorker? taskWorker;
        HostGpu? hostGpu;
        NewVmDefaults? newDefaults;
        TaskCompletionSource<bool>? dialogResult;
        bool busy;
        bool refreshing;
        bool loaded;

        public MainWindow()
        {
            InitializeComponent();
            listVms.ItemsSource = vms;
            refreshTimer.Tick += async (_, _) => { if (!busy) await RefreshVmsAsync(); };
            infoTimer.Tick += (_, _) => { infoTimer.Stop(); infoBar.Visibility = Visibility.Collapsed; };
            RestoreSettings();
            ShowHostWindowsVersion();
            UpdateDetails();
        }

        /// <summary>
        /// New VM tab: the VM runs this PC's NVIDIA driver, so it must run the same Windows
        /// version. Windows 11 is build 22000 and later.
        /// </summary>
        void ShowHostWindowsVersion()
        {
            int build = Environment.OSVersion.Version.Build;
            bool windows11 = build >= 22000;
            string version = windows11 ? "Windows 11" : "Windows 10";
            runHostOsTitle.Text = $"This PC runs {version} (build {build}): use a {version} ISO.";
            runHostOsMessage.Text = "The VM runs this PC's NVIDIA driver, so the GPU only works when both run the same Windows: " +
                                    "Windows 10 with Windows 10, Windows 11 with Windows 11.";
            checkNewTpm.IsChecked = windows11;   // Windows 11 needs a TPM; Windows 10 doesn't
        }

        // ---- startup / shutdown ---------------------------------------------------

        void RestoreSettings()
        {
            Width = settings.Width;
            Height = settings.Height;
            if (settings.Left is double left && settings.Top is double top && IsOnScreen(left, top))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = left;
                Top = top;
            }
            if (settings.Maximized) WindowState = WindowState.Maximized;
            sliderGpuShare.Value = Math.Clamp(settings.GpuShare, 10, 100);
            foreach (ComboBoxItem item in comboTheme.Items)
                if ((string)item.Tag == settings.Theme) comboTheme.SelectedItem = item;
            if (comboTheme.SelectedItem == null) comboTheme.SelectedIndex = 0;
        }

        static bool IsOnScreen(double left, double top) =>
            left >= SystemParameters.VirtualScreenLeft && top >= SystemParameters.VirtualScreenTop &&
            left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100 &&
            top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 100;

        async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            loaded = true;
            WriteLog("Loading Hyper-V...");
            try
            {
                refreshWorker = new PowerShellWorker();
                taskWorker = new PowerShellWorker();
                taskWorker.Log += message => Dispatcher.BeginInvoke(() => WriteLog(message));

                hostGpu = (await refreshWorker.RunAsync<HostGpu>("Get-HostNvidia")).FirstOrDefault();
                textGpuName.Text = hostGpu?.Name ?? "No NVIDIA GPU found";
                textGpuDriver.Text = hostGpu != null ? "Driver " + hostGpu.Version : "GPU sharing is unavailable";
                textStream.Text = StreamTarget() switch
                {
                    StreamKind.VMStream => "Stream (VMStream)",
                    StreamKind.Moonlight => "Stream (Moonlight)",
                    _ => "Remote Desktop",
                };
                WriteLog("Ready.");
                await RefreshVmsAsync();
                listVms.SelectedItem = vms.FirstOrDefault(v => v.Name == settings.LastVm) ?? vms.FirstOrDefault();
                refreshTimer.Start();
                await LoadNewVmDefaultsAsync(firstTime: true);
            }
            catch (Exception ex)
            {
                ShowInfo(Severity.Error, "Could not load Hyper-V.", ex.Message);
                WriteLog("ERROR: " + ex.Message);
            }
        }

        void Window_Closing(object? sender, CancelEventArgs e)
        {
            if (busy && MessageBox.Show(this, "A task is still running. Close anyway? The task will be stopped.", Title,
                    MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
            {
                e.Cancel = true;
                return;
            }

            var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            settings.Left = bounds.Left;
            settings.Top = bounds.Top;
            settings.Width = bounds.Width;
            settings.Height = bounds.Height;
            settings.Maximized = WindowState == WindowState.Maximized;
            settings.GpuShare = (int)Math.Round(sliderGpuShare.Value);
            settings.Save();
        }

        void Window_Closed(object? sender, EventArgs e)
        {
            refreshTimer.Stop();
            refreshWorker?.Dispose();
            taskWorker?.Dispose();
        }

        void comboTheme_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (comboTheme.SelectedItem is not ComboBoxItem { Tag: string theme }) return;
            settings.Theme = theme;
            if (loaded) { App.ApplyTheme(theme); settings.Save(); }
        }

        // ---- VM list ----------------------------------------------------------------

        async Task RefreshVmsAsync()
        {
            if (refreshWorker == null || refreshing) return;
            refreshing = true;
            try
            {
                ShowVms(await refreshWorker.RunAsync<VmInfo>("Get-VMList"));
            }
            catch (Exception ex)
            {
                WriteLog("ERROR: " + ex.Message);
            }
            finally { refreshing = false; }
        }

        void ShowVms(List<VmInfo> list)
        {
            // Update rows in place; add/remove only VMs that appeared or disappeared
            foreach (var gone in vms.Where(r => list.All(v => v.Name != r.Name)).ToList()) vms.Remove(gone);
            for (int i = 0; i < list.Count; i++)
            {
                VmRow? row = vms.FirstOrDefault(r => r.Name == list[i].Name);
                if (row == null) { row = new VmRow(list[i].Name); vms.Insert(Math.Min(i, vms.Count), row); }
                row.Update(list[i], hostGpu);
            }
            textVmCount.Text = vms.Count == 1 ? "1 virtual machine" : $"{vms.Count} virtual machines";
            textNoVms.Visibility = vms.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateDetails();
        }

        VmRow? SelectedRow => listVms.SelectedItem as VmRow;
        VmInfo? SelectedVm => SelectedRow?.Info;

        void listVms_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SelectedRow != null) settings.LastVm = SelectedRow.Name;
            UpdateDetails();
        }

        /// <summary>Shows the selected VM: colors, highlighted action and which buttons can be used.</summary>
        void UpdateDetails()
        {
            VmRow? row = SelectedRow;
            textNoSelection.Visibility = row == null ? Visibility.Visible : Visibility.Collapsed;
            scrollDetails.Visibility = row == null ? Visibility.Collapsed : Visibility.Visible;

            VmInfo? vm = row?.Info;
            bool has = vm != null, free = !busy;
            bool running = vm?.State == "Running";
            bool off = vm?.State is "Off" or "Saved" or "Paused";
            bool hasIp = !string.IsNullOrEmpty(vm?.Ip);
            bool hasGpu = vm != null && vm.GpuPercent >= 0;

            buttonStart.IsEnabled = has && free && off;
            buttonShutDown.IsEnabled = buttonRestart.IsEnabled = buttonTurnOff.IsEnabled = has && free && running;
            buttonConsole.IsEnabled = has;
            buttonCopyIp.IsEnabled = hasIp;
            buttonStream.IsEnabled = running && (StreamTarget() == StreamKind.VMStream || hasIp);
            buttonShareGpu.IsEnabled = has && free && hostGpu != null;
            buttonUpdateDriver.IsEnabled = buttonUpdateNow.IsEnabled = buttonRemoveGpu.IsEnabled = hasGpu && free;
            buttonRefresh.IsEnabled = free;
            Cursor = busy ? Cursors.AppStarting : null;

            // The most useful next step gets the accent color
            SetAccent(buttonStart, off);
            SetAccent(buttonStream, running);

            if (row == null) return;
            (string pillBg, string dotFill) = row.StateKind switch
            {
                "Running" => ("SystemFillColorSuccessBackgroundBrush", "SystemFillColorSuccessBrush"),
                "Busy" => ("SystemFillColorCautionBackgroundBrush", "SystemFillColorCautionBrush"),
                _ => ("SystemFillColorNeutralBackgroundBrush", "ControlStrongFillColorDisabledBrush"),
            };
            pillState.SetResourceReference(Border.BackgroundProperty, pillBg);
            pillDot.SetResourceReference(Shape.FillProperty, dotFill);

            switch (row.DriverKind)
            {
                case "Ok": textDriver.SetResourceReference(ForegroundProperty, "SystemFillColorSuccessBrush"); break;
                case "Update": textDriver.SetResourceReference(ForegroundProperty, "SystemFillColorCautionBrush"); break;
                default: textDriver.SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush"); break;
            }
            // Reminder to copy the driver: out of date, or never copied (new VM)
            if (row.DriverKind == "Update")
            {
                runDriverTitle.Text = "Driver out of date";
                runDriverMessage.Text = "This PC has a newer NVIDIA driver than the VM. Copy it in so the GPU keeps working.";
                buttonUpdateNow.Content = "Update now";
                infoDriver.SetResourceReference(Border.BackgroundProperty, "SystemFillColorCautionBackgroundBrush");
            }
            else if (row.DriverKind == "Unknown")
            {
                runDriverTitle.Text = "GPU driver not copied yet";
                runDriverMessage.Text = "Once Windows is installed in the VM, copy this PC's NVIDIA driver in to use the GPU.";
                buttonUpdateNow.Content = "Copy driver";
                infoDriver.SetResourceReference(Border.BackgroundProperty, "SystemFillColorAttentionBackgroundBrush");
            }
            infoDriver.Visibility = row.DriverKind is "Update" or "Unknown" ? Visibility.Visible : Visibility.Collapsed;
        }

        static void SetAccent(Button button, bool accent)
        {
            if (accent) button.SetResourceReference(StyleProperty, "AccentButtonStyle");
            else button.SetResourceReference(StyleProperty, typeof(Button));
        }

        // ---- tasks --------------------------------------------------------------------

        /// <summary>Runs a Hyper-V job on the task worker; returns whether it succeeded.</summary>
        async Task<bool> RunTaskAsync(string title, string script, params object[] args)
        {
            if (busy || taskWorker == null) return false;
            busy = true;
            buttonCreateVm.IsEnabled = false;
            UpdateDetails();
            WriteLog(title + "...");
            ShowInfo(Severity.Busy, title + "...", "This can take a minute. Details are in Activity below.");
            bool ok = false;
            try
            {
                await taskWorker.RunAsync(script, args);
                ok = true;
                WriteLog(title + ": done.");
                ShowInfo(Severity.Success, "Done.", title + " finished.");
            }
            catch (Exception ex)
            {
                WriteLog("ERROR: " + ex.Message);
                WriteLog(title + ": failed.");
                ShowInfo(Severity.Error, title + " failed.", ex.Message);
            }
            finally
            {
                busy = false;
                buttonCreateVm.IsEnabled = true;
                UpdateDetails();
            }
            await RefreshVmsAsync();
            return ok;
        }

        async void buttonStart_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedVm is { } vm) await RunTaskAsync("Starting " + vm.Name, "Start-VM -Name $args[0]", vm.Name);
        }

        async void buttonShutDown_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedVm is { } vm && await ConfirmAsync($"Shut down {vm.Name}?",
                    "Windows inside the VM shuts down normally, like choosing Shut down from its Start menu.", "Shut down"))
                await RunTaskAsync("Shutting down " + vm.Name, "Stop-VM -Name $args[0] -Force", vm.Name);
        }

        async void buttonRestart_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedVm is { } vm && await ConfirmAsync($"Restart {vm.Name}?",
                    "Windows inside the VM restarts. Save your work in the VM first.", "Restart"))
                await RunTaskAsync("Restarting " + vm.Name, "Restart-VM -Name $args[0] -Force", vm.Name);
        }

        async void buttonTurnOff_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedVm is { } vm && await ConfirmAsync($"Turn off {vm.Name}?",
                    "This is like pulling the plug: the VM stops immediately and unsaved work in it is lost.", "Turn off"))
                await RunTaskAsync("Turning off " + vm.Name, "Stop-VM -Name $args[0] -TurnOff -Force", vm.Name);
        }

        async void buttonShareGpu_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedVm is not { } vm) return;
            int percent = (int)Math.Round(sliderGpuShare.Value);
            settings.GpuShare = percent;
            if (await ConfirmAsync($"Share {percent}% of the GPU with {vm.Name}?",
                    "The VM shuts down while the GPU is configured and the driver is copied in, then starts again. Checkpoints are turned off for this VM.",
                    "Apply"))
                await RunTaskAsync($"Sharing {percent}% of the GPU with {vm.Name}", "Enable-Gpu $args[0] $args[1]", vm.Name, percent);
        }

        async void buttonUpdateDriver_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedVm is { } vm && await ConfirmAsync($"Update the driver in {vm.Name}?",
                    "This copies this PC's current NVIDIA driver into the VM. The VM shuts down during the copy, then starts again.", "Update"))
                await RunTaskAsync("Updating the driver in " + vm.Name, "Update-GpuDriver $args[0]", vm.Name);
        }

        async void buttonRemoveGpu_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedVm is { } vm && await ConfirmAsync($"Remove the GPU from {vm.Name}?",
                    "The VM no longer gets GPU acceleration. It shuts down, then starts again.", "Remove"))
                await RunTaskAsync("Removing the GPU from " + vm.Name, "Remove-Gpu $args[0]", vm.Name);
        }

        async void buttonRefresh_Click(object sender, RoutedEventArgs e)
        {
            await RefreshVmsAsync();
            WriteLog("Refreshed.");
        }

        // ---- connect ------------------------------------------------------------------

        enum StreamKind { VMStream, Moonlight, RemoteDesktop }

        static string VMStreamViewerPath => System.IO.Path.Combine(AppContext.BaseDirectory, "VMStreamViewer.exe");

        // VMStream Viewer next to this app, else Moonlight, else Remote Desktop
        static StreamKind StreamTarget() =>
            File.Exists(VMStreamViewerPath) ? StreamKind.VMStream :
            File.Exists(MoonlightPath) ? StreamKind.Moonlight : StreamKind.RemoteDesktop;

        void buttonConsole_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedVm is { } vm) Process.Start("vmconnect.exe", $"localhost \"{vm.Name}\"");
        }

        void buttonStream_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedVm is not { } vm) return;
            switch (StreamTarget())
            {
                case StreamKind.VMStream:
                    Process.Start(new ProcessStartInfo(VMStreamViewerPath) { WorkingDirectory = AppContext.BaseDirectory });
                    break;
                case StreamKind.Moonlight:
                    Process.Start(MoonlightPath, $"stream {vm.Ip} Desktop");
                    break;
                default:
                    Process.Start("mstsc.exe", $"/v:{vm.Ip}");
                    break;
            }
        }

        void buttonCopyIp_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedVm is { Ip.Length: > 0 } vm)
            {
                Clipboard.SetText(vm.Ip);
                ShowInfo(Severity.Success, "Copied.", vm.Ip + " is on the clipboard.");
            }
        }

        // ---- New VM tab ---------------------------------------------------------------

        const string PrivateSwitch = "VM-Private";
        const string NoSwitch = "Not connected";

        async Task LoadNewVmDefaultsAsync(bool firstTime)
        {
            if (refreshWorker == null) return;
            try
            {
                newDefaults = (await refreshWorker.RunAsync<NewVmDefaults>("Get-NewVmDefaults")).FirstOrDefault();
            }
            catch (Exception ex)
            {
                WriteLog("ERROR: " + ex.Message);
            }
            if (newDefaults == null) return;

            sliderNewCpus.Maximum = Math.Max(1, newDefaults.LogicalCpus);
            sliderNewMemory.Maximum = Math.Max(2, newDefaults.MemoryGB - 4);   // leave some for this PC
            textNewMemoryNote.Text = $"Fixed memory: the VM always uses this much while it runs. This PC has {newDefaults.MemoryGB} GB.";
            textNewDiskPath.Text = $"Saved in {newDefaults.DiskPath}. The file grows as needed and only takes the space actually used.";
            runNewIp.Text = newDefaults.NextPrivateIp;

            if (firstTime)
            {
                sliderNewCpus.Value = Math.Min(8, Math.Max(1, newDefaults.LogicalCpus / 2));
                sliderNewMemory.Value = Math.Min(8, sliderNewMemory.Maximum);
                sliderNewGpu.Value = sliderGpuShare.Value;
            }

            string? selectedSwitch = comboNewSwitch.SelectedItem as string;
            comboNewSwitch.ItemsSource = newDefaults.Switches.Append(NoSwitch).ToList();
            comboNewSwitch.SelectedItem = selectedSwitch != null && newDefaults.Switches.Contains(selectedSwitch) ? selectedSwitch
                : newDefaults.Switches.Contains(PrivateSwitch) ? PrivateSwitch
                : newDefaults.Switches.FirstOrDefault() ?? NoSwitch;

            // Suggest a name that isn't taken yet
            if (firstTime || newDefaults.Names.Contains(textNewName.Text.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                string name = "Windows VM";
                for (int n = 2; newDefaults.Names.Contains(name, StringComparer.OrdinalIgnoreCase); n++) name = $"Windows VM {n}";
                textNewName.Text = name;
            }
            UpdateNewVmForm();
        }

        void NewVmInput_Changed(object sender, RoutedEventArgs e) => UpdateNewVmForm();

        void UpdateNewVmForm()
        {
            if (newDefaults == null) return;
            textNewError.Visibility = Visibility.Collapsed;
            string sw = comboNewSwitch.SelectedItem as string ?? NoSwitch;
            bool isPrivate = sw == PrivateSwitch;
            textNewSwitchNote.Text = isPrivate ? "Private network between this PC and its VMs, with no internet."
                : sw == "Default Switch" ? "Shares this PC's internet connection with the VM."
                : sw == NoSwitch ? "The VM has no network. You can connect it later in Hyper-V Manager."
                : "An existing Hyper-V switch on this PC.";
            checkNewHostOnly.IsEnabled = isPrivate;
            if (!isPrivate) checkNewHostOnly.IsChecked = false;
            panelNewIp.Visibility = isPrivate ? Visibility.Visible : Visibility.Collapsed;
        }

        void buttonBrowseIso_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Choose a Windows installation ISO",
                Filter = "Disk images (*.iso)|*.iso|All files (*.*)|*.*",
            };
            if (dialog.ShowDialog(this) == true) textNewIso.Text = dialog.FileName;
        }

        async void buttonCreateVm_Click(object sender, RoutedEventArgs e)
        {
            string name = textNewName.Text.Trim();
            string iso = textNewIso.Text.Trim().Trim('"');
            string sw = comboNewSwitch.SelectedItem as string ?? NoSwitch;
            int cpus = (int)sliderNewCpus.Value, memory = (int)sliderNewMemory.Value;
            int disk = int.Parse((string)((ComboBoxItem)comboNewDisk.SelectedItem).Tag);
            int gpu = checkNewGpu.IsChecked == true ? (int)sliderNewGpu.Value : 0;
            bool tpm = checkNewTpm.IsChecked == true, hostOnly = checkNewHostOnly.IsChecked == true, start = checkNewStart.IsChecked == true;

            string? error =
                name.Length == 0 ? "Enter a name for the VM." :
                name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0 ? "The name can't contain \\ / : * ? \" < > |" :
                vms.Any(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ? $"A VM named \"{name}\" already exists." :
                iso.Length > 0 && !File.Exists(iso) ? "The ISO file was not found." :
                gpu > 0 && hostGpu == null ? "No NVIDIA GPU was found, so it can't be shared. Untick the GPU option." :
                null;
            if (error != null)
            {
                textNewError.Text = error;
                textNewError.Visibility = Visibility.Visible;
                return;
            }

            string summary = $"{cpus} virtual CPUs, {memory} GB memory, {disk} GB disk, network: {sw}" +
                             (gpu > 0 ? $", {gpu}% of the GPU" : ", no GPU") +
                             (iso.Length > 0 ? $".\n\nIt boots from {System.IO.Path.GetFileName(iso)}" : ".\n\nNo ISO was chosen, so there is nothing to install from yet") +
                             (start ? " and starts right away. In the console, press a key when it says \"Press any key to boot from CD\"." : ".");
            if (!await ConfirmAsync($"Create {name}?", summary, "Create")) return;

            tabs.SelectedIndex = 0;   // progress shows on the VM page
            bool ok = await RunTaskAsync("Creating " + name,
                "New-GpuVM $args[0] $args[1] $args[2] $args[3] $args[4] $args[5] $args[6] $args[7] $args[8] $args[9]",
                name, cpus, memory, disk, iso, sw == NoSwitch ? "" : sw, tpm, gpu, hostOnly, start);
            if (!ok) return;

            listVms.SelectedItem = vms.FirstOrDefault(v => v.Name == name) ?? listVms.SelectedItem;
            if (start) Process.Start("vmconnect.exe", $"localhost \"{name}\"");
            await LoadNewVmDefaultsAsync(firstTime: false);
        }

        // ---- InfoBar ------------------------------------------------------------------

        enum Severity { Busy, Success, Error }

        void ShowInfo(Severity severity, string title, string message)
        {
            infoTimer.Stop();
            (string background, string iconBrush, string glyph) = severity switch
            {
                Severity.Busy => ("SystemFillColorAttentionBackgroundBrush", "AccentTextFillColorPrimaryBrush", "\uE946"),
                Severity.Success => ("SystemFillColorSuccessBackgroundBrush", "SystemFillColorSuccessBrush", "\uE73E"),
                _ => ("SystemFillColorCriticalBackgroundBrush", "SystemFillColorCriticalBrush", "\uEA39"),
            };
            infoBar.SetResourceReference(Border.BackgroundProperty, background);
            infoIcon.SetResourceReference(ForegroundProperty, iconBrush);
            infoIcon.Text = glyph;
            infoTitle.Text = title;
            infoMessage.Text = message;
            infoProgress.Visibility = severity == Severity.Busy ? Visibility.Visible : Visibility.Collapsed;
            infoClose.Visibility = severity == Severity.Busy ? Visibility.Collapsed : Visibility.Visible;
            infoBar.Visibility = Visibility.Visible;
            if (severity == Severity.Success) infoTimer.Start();   // errors stay until closed
        }

        void infoClose_Click(object sender, RoutedEventArgs e)
        {
            infoTimer.Stop();
            infoBar.Visibility = Visibility.Collapsed;
        }

        // ---- dialog -------------------------------------------------------------------

        /// <summary>In-window confirmation in the style of a Fluent ContentDialog.</summary>
        Task<bool> ConfirmAsync(string title, string message, string primary)
        {
            dialogResult?.TrySetResult(false);
            dialogResult = new TaskCompletionSource<bool>();
            dialogTitle.Text = title;
            dialogMessage.Text = message;
            dialogPrimary.Content = primary;
            dialogOverlay.Visibility = Visibility.Visible;
            dialogPrimary.Focus();
            return dialogResult.Task;
        }

        void CloseDialog(bool result)
        {
            dialogOverlay.Visibility = Visibility.Collapsed;
            dialogResult?.TrySetResult(result);
            dialogResult = null;
        }

        void dialogPrimary_Click(object sender, RoutedEventArgs e) => CloseDialog(true);

        void dialogCancel_Click(object sender, RoutedEventArgs e) => CloseDialog(false);

        void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (dialogOverlay.Visibility != Visibility.Visible) return;
            if (e.Key == Key.Escape) { CloseDialog(false); e.Handled = true; }
            else if (e.Key == Key.Enter) { CloseDialog(true); e.Handled = true; }
        }

        // ---- helpers ------------------------------------------------------------------

        void WriteLog(string message)
        {
            textLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
            textLog.ScrollToEnd();
        }
    }
}
