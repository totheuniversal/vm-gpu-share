using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace VM_Manager
{
    /// <summary>A monitor of this PC, in physical pixels.</summary>
    /// <param name="RdpId">The ID Remote Desktop uses for it (what "mstsc /l" lists): the display's
    /// position in EnumDisplayDevices, which is also the number in \\.\DISPLAYn minus one.</param>
    public sealed record ScreenInfo(string Device, int RdpId, int X, int Y, int Width, int Height, bool Primary)
    {
        public int Right => X + Width;
        public int Bottom => Y + Height;

        /// <summary>Shares an edge with <paramref name="other"/>.</summary>
        public bool Touches(ScreenInfo other) =>
            ((Right == other.X || other.Right == X) && Y < other.Bottom && other.Y < Bottom) ||
            ((Bottom == other.Y || other.Bottom == Y) && X < other.Right && other.X < Right);
    }

    /// <summary>The monitors of this PC, read with EnumDisplayDevices/EnumDisplaySettings so the sizes
    /// and positions are real pixels whatever the display scaling.</summary>
    public static class Screens
    {
        public static List<ScreenInfo> GetAll()
        {
            var list = new List<ScreenInfo>();
            var device = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            for (int index = 0; EnumDisplayDevices(null, (uint)index, ref device, 0); index++)
            {
                if ((device.StateFlags & AttachedToDesktop) != 0 && (device.StateFlags & MirroringDriver) == 0)
                {
                    var mode = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
                    if (EnumDisplaySettings(device.DeviceName, EnumCurrentSettings, ref mode))
                        list.Add(new ScreenInfo(device.DeviceName, index, mode.dmPositionX, mode.dmPositionY,
                            mode.dmPelsWidth, mode.dmPelsHeight, (device.StateFlags & PrimaryDevice) != 0));
                }
                device = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            }
            return list.OrderBy(s => s.X).ThenBy(s => s.Y).ToList();
        }

        /// <summary>Remote Desktop can only span monitors that form one connected block.</summary>
        public static bool AreContiguous(IReadOnlyList<ScreenInfo> screens)
        {
            if (screens.Count <= 1) return true;
            var reached = new HashSet<ScreenInfo> { screens[0] };
            var queue = new Queue<ScreenInfo>(reached);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var next in screens)
                    if (!reached.Contains(next) && current.Touches(next)) { reached.Add(next); queue.Enqueue(next); }
            }
            return reached.Count == screens.Count;
        }

        const int AttachedToDesktop = 0x1, PrimaryDevice = 0x4, MirroringDriver = 0x8;
        const int EnumCurrentSettings = -1;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }

        // The display variant of DEVMODEW
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public int dmFields;
            public int dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);
    }

    /// <summary>A monitor toggle in the "Remote Desktop screens" row.</summary>
    public sealed class ScreenChoice : INotifyPropertyChanged
    {
        public ScreenChoice(ScreenInfo info, int number, bool selected)
        {
            Info = info;
            Number = number;
            this.selected = selected;
        }

        public ScreenInfo Info { get; }
        public int Number { get; }   // 1, 2, 3... from left to right
        public string Title => $"Screen {Number}";
        public string Detail => $"{Info.Width} × {Info.Height}" + (Info.Primary ? " · main" : "");
        public string ToolTip => Info.Primary ? "This PC's main screen" : $"Screen {Number} from the left";

        bool selected;
        public bool Selected
        {
            get => selected;
            set { if (selected != value) { selected = value; OnChanged(); } }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
