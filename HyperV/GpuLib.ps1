# VM GPU Share - Hyper-V and GPU commands. Embedded in the app and run by
# PowerShellWorker in a background powershell.exe (see Worker.ps1).
$ErrorActionPreference = 'Stop'

function Get-HostNvidia {
    $nv = Get-CimInstance Win32_VideoController | Where-Object Name -like '*NVIDIA*' | Select-Object -First 1
    if (-not $nv) { return $null }
    $dir = Split-Path (($nv.InstalledDisplayDrivers -split ',')[0])   # ...\DriverStore\FileRepository\<folder>
    [pscustomobject]@{ Name = $nv.Name; Version = $nv.DriverVersion; DriverDir = $dir; Folder = Split-Path $dir -Leaf }
}

# The driver folder last copied into a VM is remembered in the VM's Notes
function Get-VMDriverFolder($vm) {
    if ($vm.Notes -match '\[GPU-DRIVER:([^\]]+)\]') { $Matches[1] }
}

function Set-VMDriverFolder($vm, $folder) {
    $notes = ($vm.Notes -replace '\[GPU-DRIVER:[^\]]+\]', '').Trim()
    Set-VM -VM $vm -Notes ("$notes`r`n[GPU-DRIVER:$folder]").Trim()
}

function Get-VMList {
    foreach ($vm in Get-VM | Sort-Object Name) {
        $gpu = Get-VMGpuPartitionAdapter -VM $vm -ErrorAction SilentlyContinue | Select-Object -First 1
        $nic = Get-VMNetworkAdapter -VM $vm | Select-Object -First 1
        $ip = @($nic.IPAddresses) -match '^\d+\.\d+\.\d+\.\d+$' | Select-Object -First 1
        $mem = if ($vm.MemoryAssigned -gt 0) { $vm.MemoryAssigned } else { $vm.MemoryStartup }
        [pscustomobject]@{
            Name = $vm.Name
            State = "$($vm.State)"
            GpuPercent = if ($gpu) { [int][math]::Round($gpu.MaxPartitionVRAM / 10000000) } else { -1 }
            DriverFolder = "$(Get-VMDriverFolder $vm)"
            Cpus = [int]$vm.ProcessorCount
            MemoryGB = [double]($mem / 1GB)
            Ip = "$ip"
            CpuUsage = [int]$vm.CPUUsage
            MemoryDemandGB = [double]($vm.MemoryDemand / 1GB)
            UptimeSeconds = [long]$vm.Uptime.TotalSeconds
            Generation = [int]$vm.Generation
            Switch = "$($nic.SwitchName)"
        }
    }
}

function Stop-VMForChange($vm) {
    if ($vm.State -eq 'Off') { return $false }
    Log "Shutting down $($vm.Name)..."
    Stop-VM -VM $vm -Force
    return $true
}

# Copy-VMFile can't write into the guest's System32, so mount the VHD offline instead
function Copy-GpuDriver($vm) {
    $nv = Get-HostNvidia
    if (-not $nv) { throw 'No NVIDIA GPU found on this PC.' }
    $vhd = (Get-VMHardDiskDrive -VM $vm | Select-Object -First 1).Path
    Log "Mounting $vhd..."
    $disk = Mount-VHD -Path $vhd -Passthru | Get-Disk
    try {
        $root = $null
        foreach ($p in Get-Partition -DiskNumber $disk.Number | Where-Object Size -gt 10GB) {
            if (-not $p.DriveLetter -or $p.DriveLetter -eq [char]0) {
                $p | Add-PartitionAccessPath -AssignDriveLetter
                $p = Get-Partition -DiskNumber $disk.Number -PartitionNumber $p.PartitionNumber
            }
            if (Test-Path "$($p.DriveLetter):\Windows\System32") { $root = "$($p.DriveLetter):"; break }
        }
        if (-not $root) { throw 'Could not find Windows inside the VM disk.' }

        $dest = "$root\Windows\System32\HostDriverStore\FileRepository"
        New-Item -ItemType Directory -Force $dest | Out-Null
        Log "Copying driver $($nv.Folder) ($($nv.Version))..."
        Copy-Item $nv.DriverDir $dest -Recurse -Force
        Log 'Copying nv* files from System32...'
        Get-ChildItem C:\Windows\System32 -Filter 'nv*' -File | ForEach-Object {
            Copy-Item $_.FullName "$root\Windows\System32\" -Force -ErrorAction SilentlyContinue
        }
    }
    finally { Dismount-VHD -Path $vhd }
    Set-VMDriverFolder (Get-VM -Id $vm.Id) $nv.Folder
    Log 'Driver copied.'
}

function Get-NvidiaPartitionGpu {
    $partGpus = @(if (Get-Command Get-VMHostPartitionableGpu -ErrorAction SilentlyContinue) { Get-VMHostPartitionableGpu } else { Get-VMPartitionableGpu })
    $nvGpu = $partGpus | Where-Object Name -like '*VEN_10DE*' | Select-Object -First 1   # 10DE = NVIDIA
    if (-not $nvGpu) { throw 'The NVIDIA GPU is not partitionable. Update the NVIDIA driver.' }
    [pscustomobject]@{ All = $partGpus; Nvidia = $nvGpu }
}

# Adds the GPU partition to a VM that is off. The driver is copied separately (Copy-GpuDriver).
function Add-GpuPartition($vm, [int]$percent) {
    $gpus = Get-NvidiaPartitionGpu
    $partGpus = $gpus.All; $nvGpu = $gpus.Nvidia

    Log 'Configuring GPU partition...'
    Set-VM -VM $vm -CheckpointType Disabled -AutomaticStopAction TurnOff -StaticMemory
    Get-VMGpuPartitionAdapter -VM $vm -ErrorAction SilentlyContinue | Remove-VMGpuPartitionAdapter
    if ((Get-Command Add-VMGpuPartitionAdapter).Parameters.ContainsKey('InstancePath')) {
        Add-VMGpuPartitionAdapter -VM $vm -InstancePath $nvGpu.Name
    } else {
        # Windows 10 has no -InstancePath; Hyper-V uses the first partitionable GPU
        if ($partGpus[0].Name -ne $nvGpu.Name) { Log 'Warning: the first partitionable GPU is not NVIDIA; the VM may get the other GPU.' }
        Add-VMGpuPartitionAdapter -VM $vm
    }
    $val = [uint64](1000000000 * $percent / 100)
    Set-VMGpuPartitionAdapter -VM $vm `
        -MinPartitionVRAM 80000000 -MaxPartitionVRAM $val -OptimalPartitionVRAM $val `
        -MinPartitionEncode 80000000 -MaxPartitionEncode $val -OptimalPartitionEncode $val `
        -MinPartitionDecode 80000000 -MaxPartitionDecode $val -OptimalPartitionDecode $val `
        -MinPartitionCompute 80000000 -MaxPartitionCompute $val -OptimalPartitionCompute $val
    Set-VM -VM $vm -GuestControlledCacheTypes $true -LowMemoryMappedIoSpace 1GB -HighMemoryMappedIoSpace 32GB
    Enable-VMIntegrationService -VM $vm -Name 'Guest Service Interface'
    Log "GPU shared at $percent%."
}

function Enable-Gpu([string]$vmName, [int]$percent) {
    $vm = Get-VM -Name $vmName
    $null = Get-NvidiaPartitionGpu   # fail before shutting the VM down
    $wasOn = Stop-VMForChange $vm
    Add-GpuPartition $vm $percent
    Copy-GpuDriver $vm
    if ($wasOn) { Log 'Starting VM...'; Start-VM -VM $vm }
}

function Update-GpuDriver([string]$vmName) {
    $vm = Get-VM -Name $vmName
    $wasOn = Stop-VMForChange $vm
    Copy-GpuDriver $vm
    if ($wasOn) { Log 'Starting VM...'; Start-VM -VM $vm }
}

function Remove-Gpu([string]$vmName) {
    $vm = Get-VM -Name $vmName
    $wasOn = Stop-VMForChange $vm
    Get-VMGpuPartitionAdapter -VM $vm -ErrorAction SilentlyContinue | Remove-VMGpuPartitionAdapter
    Log 'GPU removed from VM.'
    if ($wasOn) { Log 'Starting VM...'; Start-VM -VM $vm }
}

# ---- New VM ------------------------------------------------------------------

# Values the New VM form starts with
function Get-NewVmDefaults {
    $cs = Get-CimInstance Win32_ComputerSystem
    $used = @(Get-VM | Get-VMNetworkAdapter | ForEach-Object { $_.IPAddresses }) -match '^192\.168\.100\.\d+$'
    $next = 2..254 | Where-Object { "192.168.100.$_" -notin $used } | Select-Object -First 1
    [pscustomobject]@{
        LogicalCpus = [int]$cs.NumberOfLogicalProcessors
        MemoryGB = [int][math]::Floor($cs.TotalPhysicalMemory / 1GB)
        Switches = @(Get-VMSwitch | ForEach-Object Name)
        DiskPath = (Get-VMHost).VirtualHardDiskPath
        NextPrivateIp = "192.168.100.$next"
        Names = @(Get-VM | ForEach-Object Name)
    }
}

# Lets the VM talk only to this PC (192.168.100.1 on VM-Private), enforced by Hyper-V outside the VM
function Set-HostOnlyNetwork($vm) {
    Log 'Limiting the network to this PC...'
    Get-VMNetworkAdapterAcl -VMName $vm.Name | Remove-VMNetworkAdapterAcl
    Add-VMNetworkAdapterAcl -VMName $vm.Name -RemoteIPAddress '0.0.0.0/0' -Direction Both -Action Deny
    Add-VMNetworkAdapterAcl -VMName $vm.Name -RemoteIPAddress '::/0' -Direction Both -Action Deny
    Add-VMNetworkAdapterAcl -VMName $vm.Name -RemoteIPAddress '192.168.100.1' -Direction Both -Action Allow
    Set-VMNetworkAdapter -VMName $vm.Name -MacAddressSpoofing Off -DhcpGuard On -RouterGuard On
}

# Creates a Generation 2 VM ready for GPU sharing (fixed memory, no checkpoints)
function New-GpuVM([string]$name, [int]$cpus, [int]$memoryGB, [int]$diskGB, [string]$iso, [string]$switch,
                   [bool]$tpm, [int]$gpuPercent, [bool]$hostOnly, [bool]$start) {
    if (Get-VM -Name $name -ErrorAction SilentlyContinue) { throw "A VM named '$name' already exists." }
    if ($iso -and -not (Test-Path $iso)) { throw "The ISO file was not found: $iso" }
    if ($gpuPercent -gt 0) { $null = Get-NvidiaPartitionGpu }
    $vhd = Join-Path (Get-VMHost).VirtualHardDiskPath "$name.vhdx"
    if (Test-Path $vhd) { throw "A disk already exists at $vhd" }

    Log "Creating $name ($cpus vCPU, $memoryGB GB, $diskGB GB disk)..."
    $params = @{ Name = $name; Generation = 2; MemoryStartupBytes = [int64]$memoryGB * 1GB
                 NewVHDPath = $vhd; NewVHDSizeBytes = [uint64]$diskGB * 1GB }
    if ($switch) { $params.SwitchName = $switch }
    $vm = New-VM @params
    Set-VMProcessor -VM $vm -Count $cpus
    Set-VM -VM $vm -StaticMemory -CheckpointType Disabled -AutomaticCheckpointsEnabled $false -AutomaticStopAction TurnOff
    Enable-VMIntegrationService -VM $vm -Name 'Guest Service Interface'

    if ($iso) {
        Log 'Adding the Windows ISO...'
        $dvd = Add-VMDvdDrive -VM $vm -Path $iso -Passthru
        Set-VMFirmware -VM $vm -FirstBootDevice $dvd
    }
    if ($tpm) {
        Log 'Enabling TPM for Windows 11...'
        try { Set-VMKeyProtector -VM $vm -NewLocalKeyProtector; Enable-VMTPM -VM $vm }
        catch { Log "Warning: could not enable the TPM ($($_.Exception.Message)). Windows 10 still installs fine." }
    }
    if ($gpuPercent -gt 0) { Add-GpuPartition $vm $gpuPercent }
    if ($hostOnly) { Set-HostOnlyNetwork $vm }

    Log "$name is ready."
    if ($start) { Log 'Starting VM...'; Start-VM -VM $vm }
}


# ---- Copy files --------------------------------------------------------------

function Format-Size([double]$bytes) {
    if ($bytes -ge 1GB) { '{0:0.0} GB' -f ($bytes / 1GB) }
    elseif ($bytes -ge 1MB) { '{0:0.0} MB' -f ($bytes / 1MB) }
    elseif ($bytes -ge 1KB) { '{0:0} KB' -f ($bytes / 1KB) }
    else { "$bytes bytes" }
}

# Copies files and folders from this PC into a running VM with Copy-VMFile (Hyper-V's
# Guest Service Interface): no network and no VM password needed. Folders are copied
# file by file into <destination>\<folder name>, keeping their layout.
function Copy-FilesToVM([string]$vmName, [string]$destination, [string[]]$paths) {
    $vm = Get-VM -Name $vmName
    if ($vm.State -ne 'Running') { throw "$vmName is not running. Start it first." }
    $destination = $destination.Trim().Trim('"')
    if ($destination -notmatch '^[A-Za-z]:\\') { throw "The folder in the VM must be a full path, like C:\Users\you\Desktop\Share." }
    $destination = $destination.TrimEnd('\')

    # Guest Service Interface, found by its ID so it also works on non-English Windows
    $gsi = Get-VMIntegrationService -VM $vm | Where-Object { $_.Id -like '*6C09BB55-D683-4DA0-8931-C9BF705F6480*' }
    if ($gsi -and -not $gsi.Enabled) {
        Log 'Turning on Guest Service Interface...'
        $gsi | Enable-VMIntegrationService
        Start-Sleep -Seconds 5
    }

    $files = @(foreach ($path in $paths) {
        $path = $path.Trim('"')
        if (Test-Path -LiteralPath $path -PathType Container) {
            $root = (Get-Item -LiteralPath $path -Force).FullName.TrimEnd('\')
            $folder = Split-Path $root -Leaf
            if (-not $folder -or $folder -match ':$') { $folder = 'Drive ' + $root[0] }
            Get-ChildItem -LiteralPath $root -Recurse -File -Force | ForEach-Object {
                $relative = Join-Path $folder $_.FullName.Substring($root.Length + 1)
                [pscustomobject]@{ Source = $_.FullName; Relative = $relative; Length = $_.Length }
            }
        }
        elseif (Test-Path -LiteralPath $path -PathType Leaf) {
            $file = Get-Item -LiteralPath $path -Force
            [pscustomobject]@{ Source = $file.FullName; Relative = $file.Name; Length = $file.Length }
        }
        else { throw "Not found on this PC: $path" }
    })
    if ($files.Count -eq 0) { throw 'There is nothing to copy: the folders are empty.' }

    $bytes = ($files | Measure-Object -Property Length -Sum).Sum
    Log "Copying $($files.Count) file(s), $(Format-Size $bytes), to $destination in $vmName..."
    $i = 0
    foreach ($file in $files) {
        $i++
        Log "Copying $i of $($files.Count): $($file.Relative)"
        $target = Join-Path $destination $file.Relative
        # The guest service can need a few seconds after the VM starts or the service is turned on
        for ($try = 1; ; $try++) {
            try {
                Copy-VMFile -VM $vm -SourcePath $file.Source -DestinationPath $target -FileSource Host -CreateFullPath -Force
                break
            }
            catch {
                if ($try -ge 4) { throw "Could not copy $($file.Relative): $($_.Exception.Message)" }
                Log "Not ready yet, trying again in 5 seconds..."
                Start-Sleep -Seconds 5
            }
        }
    }
    Log "Copied $($files.Count) file(s), $(Format-Size $bytes), to $destination."
    [pscustomobject]@{ Files = $files.Count; Bytes = [long]$bytes }
}
