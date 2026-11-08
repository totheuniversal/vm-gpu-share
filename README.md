# VM Manager

A Windows desktop app for running Hyper-V virtual machines with a **shared NVIDIA GPU** (GPU partitioning, GPU-P).
Built with WPF on .NET 10 and its Fluent 2 theme: light and dark mode follow Windows.

![VM Manager showing a running VM with live stats and GPU sharing](docs/screenshot.png)

## Features

- **Virtual machines:** every Hyper-V VM in one list, with a status dot and summary. The page for the selected VM shows:
  - **Live stats:** CPU usage, memory in use, uptime, IP address, network, GPU share. They refresh every 3 seconds.
  - **Power:** Start, Shut down, Restart, Turn off.
  - **Connect:** Stream, the Hyper-V console, Copy IP.
- **GPU sharing:** give a VM 10–100% of the host NVIDIA GPU. The app:
  - adds the GPU partition with the needed VM settings (fixed memory, no checkpoints, MMIO space)
  - copies the host's NVIDIA driver into the VM by mounting its virtual disk, with no files to move by hand
  - warns when the VM's driver is older than the host's, with an **Update now** button
- **New VM tab:** create a Generation 2 VM that's ready for GPU sharing in one step:
  - name, Windows ISO, processors, memory, disk size, network
  - optional TPM for Windows 11
  - optional "only allow this PC" network filter (Hyper-V port ACLs)
- **Fluent design:**
  - in-window confirmation dialogs and InfoBars for task progress and results
  - theme picker (System / Light / Dark)
  - remembers window size, theme, GPU % and the last selected VM

## In use

SolidWorks 2025 running inside a VM that shares the host's RTX 4080, opened with Remote Desktop:

![SolidWorks 2025 with a 3D part open inside the VM](docs/solidworks-in-vm.png)

Inside the VM, Device Manager lists the shared GPU next to Hyper-V's own display adapters:

![Device Manager in the VM showing the NVIDIA GeForce RTX 4080 Laptop GPU](docs/vm-device-manager.png)

## Requirements

- Windows 10 (21H2 or later) or Windows 11, **Pro / Enterprise / Education**, with **Hyper-V** turned on
- An **NVIDIA GPU** that Hyper-V can partition (`Get-VMHostPartitionableGpu`, or `Get-VMPartitionableGpu` on Windows 10)
- **Administrator rights.** The app asks for them when it starts.
- To build: the [.NET 10 SDK](https://dotnet.microsoft.com/download), or Visual Studio with the ".NET desktop development" workload
- **The VM must run the same Windows version as the host PC:**

  | Host PC | Virtual machine |
  |---|---|
  | Windows 10 | Windows 10 |
  | Windows 11 | Windows 11 |

> [!IMPORTANT]
> **Use the same Windows version on the host and in the VM.** The VM runs the host's NVIDIA driver files, which the app copies in. If the host and VM run different Windows versions, the GPU may not work in the VM: it can show an error in Device Manager (often Code 43), or no GPU acceleration at all. A Windows 10 host needs Windows 10 VMs, and a Windows 11 host needs Windows 11 VMs.

## Build and run

```bash
dotnet build "VM Manager.csproj" -c Release
```

Then run `bin\Release\net10.0-windows\VM Manager.exe` and accept the administrator prompt.
To debug in Visual Studio, start Visual Studio **as administrator**.

**Streaming:** the Stream button opens `VMStreamViewer.exe` if it sits next to the app. Otherwise it uses Moonlight if it's installed, and Remote Desktop to the VM's IP if not.

## How it works

The Hyper-V PowerShell module only runs in **Windows PowerShell 5.1**, which .NET 10 can't host inside the app. So the app:

- starts two hidden `powershell.exe` workers (`HyperV/PowerShellWorker.cs`): one refreshes the list, one runs longer jobs, so the window never freezes
- sends each worker the embedded scripts `HyperV/GpuLib.ps1` (the Hyper-V and GPU commands) and `HyperV/Worker.ps1` (a request loop)
- talks to them over stdin/stdout, one JSON request at a time; progress lines come back as `LOG`, results as `RESULT` or `ERROR`

| File | Purpose |
|---|---|
| `MainWindow.xaml` / `.cs` | The window: VM list, details page, New VM tab, dialogs, InfoBars |
| `App.xaml` / `.cs` | Fluent theme, shared styles, theme switching, error logging |
| `VmRow.cs` | One VM as shown in the list and on the details page |
| `AppSettings.cs` | Saved settings (`%APPDATA%\VM Manager\settings.json`) |
| `HyperV/GpuLib.ps1` | Hyper-V and GPU commands: list VMs, share or remove the GPU, copy the driver, create VMs |
| `HyperV/PowerShellWorker.cs`, `Worker.ps1` | The background PowerShell worker and its protocol |
| `app.manifest` | Requests administrator rights |

## Good to know

- **The VM is shut down** while the GPU is added or removed or the driver is copied, then started again. The app asks first.
- **After every NVIDIA driver update on the host,** use **Update driver** for each VM. The list marks VMs whose driver is out of date.
- **Each VM's share is a maximum, not a reservation.** The host and running VMs share the GPU.
- **GPU partitioning on client Windows** works with recent NVIDIA drivers, but Microsoft documents it mainly for Windows Server.
- **If something goes wrong at runtime,** the app shows the error and writes it to `error.log` next to the `.exe`.
