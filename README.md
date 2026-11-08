# VM GPU Share

**Share your NVIDIA graphics card with Hyper-V virtual machines.**
Run 3D apps, CAD and GPU software inside a VM, at near-native speed, with a few clicks.

![VM GPU Share showing a running VM with live stats and GPU sharing](docs/screenshot.png)

### [⬇ Download the latest version](../../releases/latest)

One file, nothing to install: download `VM-GPU-Share-…-win-x64.exe` and double-click it.

---

## What it does

- **Shares your NVIDIA GPU with a VM.** Pick how much (10–100%) and click one button.
- **Copies the graphics driver into the VM for you,** and tells you when it needs updating.
- **Creates new VMs** that are ready for GPU sharing.
- **Shows your VMs live:** state, CPU, memory, uptime and IP address, with Start, Shut down, Restart and Connect buttons.

## What you need

- [ ] **Windows 10 or 11 Pro, Enterprise or Education.** Home editions don't have Hyper-V.
- [ ] **An NVIDIA graphics card**
- [ ] **A Windows installation ISO of the same version as your PC** (see below)
- [ ] **Administrator rights** on your PC

> [!IMPORTANT]
> **The VM must run the same Windows version as your PC.**
>
> | Your PC | Install in the VM |
> |---|---|
> | Windows 10 | Windows 10 |
> | Windows 11 | Windows 11 |
>
> The VM uses your PC's NVIDIA driver. With different Windows versions, the GPU won't work in the VM.

---

## Setup: 4 steps

**Install Windows in the VM first, then share the GPU.** The app copies the driver into the VM's Windows, so Windows must already be installed there.

### Step 1: Turn on Hyper-V (only once)

1. Press **Start**, type **"Turn Windows features on or off"**, and open it.
2. Tick **Hyper-V** and click **OK**.
3. **Restart** your PC.

### Step 2: Create a VM and install Windows

1. Open **VM GPU Share** and click **Yes** at the administrator prompt.
2. Go to the **New VM** tab and fill it in:
   - **Name:** anything, e.g. *Work VM*
   - **Windows ISO:** click **Browse** and pick your Windows ISO, **the same version as your PC**. The app shows which one you need.
   - **Processors / Memory / Disk:** the defaults are fine. 8 CPUs, 8 GB and 128 GB suit most uses.
   - **Network:** **Default Switch** gives the VM internet.
3. Click **Create VM**. The VM starts and its window opens.
4. **Press any key** when the VM says *"Press any key to boot from CD or DVD"*.
5. **Install Windows** as usual and sign in.
6. **Set a password** for your Windows account in the VM. Remote Desktop needs one.

> Prefer Hyper-V Manager? Use **Action → New → Virtual Machine**, choose **Generation 2**, and **untick "Use Dynamic Memory"**.

### Step 3: Share the GPU

1. In VM GPU Share, select your VM on the left.
2. Under **GPU sharing**, move the **Maximum share** slider, e.g. to **80%**.
3. Click **Apply GPU share**, then **Apply**.
4. Wait about a minute. The VM shuts down, gets the GPU and the driver, and starts again.

When it's done, **Driver in VM** shows **Up to date**. ✅

### Step 4: Check it works

1. Connect to the VM with the **Remote Desktop** button.
2. In the VM, right-click **Start** and open **Device Manager**.
3. Open **Display adapters**. Your **NVIDIA** card should be there **without a yellow warning sign**.

![Device Manager in the VM showing the NVIDIA GeForce RTX 4080 Laptop GPU](docs/vm-device-manager.png)

🎉 **Done!** Your VM can now use the GPU.

---

## Everyday use

| I want to… | Do this |
|---|---|
| Start or stop the VM | Select it → **Start**, **Shut down** or **Restart** |
| Work in the VM with the GPU | **Remote Desktop** button. This is the fast way, and 3D apps use the GPU. |
| See the VM's basic window | **Console**. Good for setup, but it **doesn't show GPU acceleration**. |
| Give the VM more or less GPU | Move the slider → **Apply GPU share** |
| Update after a new NVIDIA driver on my PC | When it says **Needs update** → **Update now** |
| Take the GPU away from the VM | **Remove GPU** |
| Use dark or light mode | Theme picker, top right |

> [!TIP]
> **After every NVIDIA driver update on your PC,** open VM GPU Share and click **Update now** for each VM. Otherwise the GPU in the VM can stop working.

### Example: SolidWorks in a VM

SolidWorks 2025 running in a VM with a shared RTX 4080, opened with Remote Desktop:

![SolidWorks 2025 with a 3D part open inside the VM](docs/solidworks-in-vm.png)

---

## Troubleshooting

<details>
<summary><b>"Windows protected your PC" when I open the app</b></summary>

The app isn't code-signed yet. Click **More info → Run anyway**.
</details>

<details>
<summary><b>The NVIDIA GPU has a yellow warning sign in the VM (e.g. Code 43)</b></summary>

1. Make sure the VM runs the **same Windows version** as your PC (10 with 10, 11 with 11).
2. In VM GPU Share, click **Update driver** for the VM.
3. Restart the VM.
</details>

<details>
<summary><b>"Driver in VM" says Unknown or Needs update</b></summary>

- **Unknown:** the driver was never copied. Install Windows in the VM first, then click **Copy driver**.
- **Needs update:** your PC has a newer NVIDIA driver. Click **Update now**.
</details>

<details>
<summary><b>The VM's window (Console) shows no GPU acceleration</b></summary>

That's normal. Hyper-V's console window never uses the GPU. Connect with **Remote Desktop** instead.
</details>

<details>
<summary><b>Remote Desktop can't connect</b></summary>

- The VM must be **running** and show an **IP address** in the app.
- In the VM, turn on **Settings → System → Remote Desktop**. This needs Windows **Pro** in the VM.
- Your Windows account in the VM must have a **password**.
</details>

<details>
<summary><b>"The NVIDIA GPU is not partitionable"</b></summary>

Update your NVIDIA driver on the PC, and check that **Hyper-V** is turned on (step 1).
</details>

<details>
<summary><b>Something else went wrong</b></summary>

Open **Activity** at the bottom of the app to see what happened. Errors are also saved in `error.log` next to the app.
</details>

---

## Good to know

- **The VM shuts down for a moment** whenever the GPU is changed or the driver is copied. The app always asks first.
- **The GPU share is a limit, not a reservation:** your PC and running VMs share the GPU.
- **Your settings are remembered:** window size, theme, GPU % and the last VM.

## Credits

Made by [totheuniversal](https://github.com/totheuniversal), with support from **[Claude Code](https://claude.com/claude-code)**.
