namespace VM_Manager.HyperV
{
    /// <summary>One row of Get-VMList (GpuLib.ps1).</summary>
    public sealed class VmInfo
    {
        public string Name { get; set; } = "";
        public string State { get; set; } = "";
        /// <summary>GPU share in percent, or -1 when no GPU is assigned.</summary>
        public int GpuPercent { get; set; }
        /// <summary>Host driver folder last copied into the VM (from the VM's Notes).</summary>
        public string DriverFolder { get; set; } = "";
        public int Cpus { get; set; }
        public double MemoryGB { get; set; }
        public string Ip { get; set; } = "";
        /// <summary>CPU load of the VM, 0-100.</summary>
        public int CpuUsage { get; set; }
        /// <summary>Memory the guest is actually using (0 when the VM is off).</summary>
        public double MemoryDemandGB { get; set; }
        public long UptimeSeconds { get; set; }
        public int Generation { get; set; }
        /// <summary>Virtual switch of the first network adapter.</summary>
        public string Switch { get; set; } = "";
    }

    /// <summary>Result of Get-NewVmDefaults (GpuLib.ps1): starting values for the New VM form.</summary>
    public sealed class NewVmDefaults
    {
        public int LogicalCpus { get; set; }
        public int MemoryGB { get; set; }
        public List<string> Switches { get; set; } = new();
        public string DiskPath { get; set; } = "";
        public string NextPrivateIp { get; set; } = "";
        public List<string> Names { get; set; } = new();
    }

    /// <summary>Result of Get-HostNvidia (GpuLib.ps1).</summary>
    public sealed class HostGpu
    {
        public string Name { get; set; } = "";
        public string Version { get; set; } = "";
        public string Folder { get; set; } = "";
    }
}
