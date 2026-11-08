using System.ComponentModel;
using System.Runtime.CompilerServices;
using VM_Manager.HyperV;

namespace VM_Manager
{
    /// <summary>
    /// One VM in the list and in the details page. Updated in place on every
    /// refresh, so the selection survives the auto-refresh.
    /// </summary>
    public sealed class VmRow : INotifyPropertyChanged
    {
        public VmRow(string name) => Name = name;

        public string Name { get; }
        public VmInfo Info { get; private set; } = new();

        string state = "", stateKind = "", summary = "", gpu = "", driver = "", driverKind = "";
        string memory = "", memoryDetail = "", uptime = "", ip = "", network = "", generation = "";
        int cpuUsage;
        double memoryUsage;

        public string State { get => state; private set => Set(ref state, value); }
        /// <summary>Running, Off or Busy (starting, stopping, saved, paused...): picks the badge color.</summary>
        public string StateKind { get => stateKind; private set => Set(ref stateKind, value); }
        public bool IsRunning => State == "Running";
        /// <summary>One-line summary for the VM list.</summary>
        public string Summary { get => summary; private set => Set(ref summary, value); }
        public string Gpu { get => gpu; private set => Set(ref gpu, value); }
        public string Driver { get => driver; private set => Set(ref driver, value); }
        /// <summary>Ok, Update, Unknown or None: picks the driver color.</summary>
        public string DriverKind { get => driverKind; private set => Set(ref driverKind, value); }
        public int CpuUsage { get => cpuUsage; private set => Set(ref cpuUsage, value); }
        public string Memory { get => memory; private set => Set(ref memory, value); }
        public string MemoryDetail { get => memoryDetail; private set => Set(ref memoryDetail, value); }
        /// <summary>Memory in use as a percentage of assigned memory.</summary>
        public double MemoryUsage { get => memoryUsage; private set => Set(ref memoryUsage, value); }
        public string Uptime { get => uptime; private set => Set(ref uptime, value); }
        public string Ip { get => ip; private set => Set(ref ip, value); }
        public string Network { get => network; private set => Set(ref network, value); }
        public string Generation { get => generation; private set => Set(ref generation, value); }

        public void Update(VmInfo vm, HostGpu? host)
        {
            Info = vm;
            OnPropertyChanged(nameof(Info));
            State = vm.State;
            StateKind = vm.State switch { "Running" => "Running", "Off" => "Off", _ => "Busy" };
            OnPropertyChanged(nameof(IsRunning));

            bool hasGpu = vm.GpuPercent >= 0;
            Gpu = hasGpu ? vm.GpuPercent + "%" : "Off";
            (Driver, DriverKind) = !hasGpu ? ("No GPU", "None")
                : vm.DriverFolder.Length == 0 ? ("Unknown", "Unknown")
                : host != null && vm.DriverFolder == host.Folder ? ("Up to date", "Ok")
                : ("Needs update", "Update");
            Summary = $"{vm.Cpus} vCPU · {vm.MemoryGB:0.#} GB" + (hasGpu ? $" · GPU {vm.GpuPercent}%" : "");

            CpuUsage = IsRunning ? vm.CpuUsage : 0;
            Memory = $"{vm.MemoryGB:0.#} GB";
            MemoryUsage = IsRunning && vm.MemoryGB > 0 ? Math.Min(100, vm.MemoryDemandGB / vm.MemoryGB * 100) : 0;
            MemoryDetail = IsRunning ? $"{vm.MemoryDemandGB:0.0} GB in use" : "Not running";
            Uptime = IsRunning ? FormatUptime(vm.UptimeSeconds) : "-";
            Ip = vm.Ip.Length > 0 ? vm.Ip : "-";
            Network = vm.Switch.Length > 0 ? vm.Switch : "Not connected";
            Generation = $"Generation {vm.Generation}";
        }

        static string FormatUptime(long seconds)
        {
            var t = TimeSpan.FromSeconds(seconds);
            if (t.TotalDays >= 1) return $"{(int)t.TotalDays} d {t.Hours} h";
            if (t.TotalHours >= 1) return $"{t.Hours} h {t.Minutes} min";
            return $"{Math.Max(1, t.Minutes)} min";
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        void Set<T>(ref T field, T value, [CallerMemberName] string name = "")
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            OnPropertyChanged(name);
        }
    }
}
