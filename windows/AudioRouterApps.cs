// Audio Router · Apps: pick the output device for each Windows app (game -> headphones, Chrome -> speakers).
// Uses the same per-app endpoint policy that Settings > Sound > Volume mixer uses.
// Build: build.cmd (built-in .NET Framework csc, C# 5, no dependencies).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AudioRouterApps
{
    // ---------- Core Audio COM interop ----------

    enum EDataFlow { eRender = 0, eCapture = 1, eAll = 2 }
    enum ERole { eConsole = 0, eMultimedia = 1, eCommunications = 2 }

    // Undocumented but stable since Vista: what the Sound control panel uses to set the default output.
    // Only SetDefaultEndpoint is called; the earlier slots just keep the vtable order.
    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat(); [PreserveSig] int GetDeviceFormat(); [PreserveSig] int ResetDeviceFormat();
        [PreserveSig] int SetDeviceFormat(); [PreserveSig] int GetProcessingPeriod(); [PreserveSig] int SetProcessingPeriod();
        [PreserveSig] int GetShareMode(); [PreserveSig] int SetShareMode(); [PreserveSig] int GetPropertyValue();
        [PreserveSig] int SetPropertyValue();
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
    }

    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    class PolicyConfigClient { }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumeratorCo { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow flow, int stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow flow, ERole role, out IMMDevice device);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int Item(int index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetAt(int index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PropertyKey
    {
        public Guid fmtid; public int pid;
        public PropertyKey(Guid f, int p) { fmtid = f; pid = p; }
    }

    // Padded to the full x64 PROPVARIANT size (24 bytes) so the out-param never overruns.
    [StructLayout(LayoutKind.Explicit)]
    struct PropVariant
    {
        [FieldOffset(0)] public short vt;
        [FieldOffset(8)] public IntPtr pointer;
        [FieldOffset(16)] public IntPtr pad;
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionManager2
    {
        [PreserveSig] int GetAudioSessionControl(IntPtr guid, int flags, out IntPtr control);
        [PreserveSig] int GetSimpleAudioVolume(IntPtr guid, int flags, out IntPtr volume);
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, out IAudioSessionControl2 session);
    }

    [ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionControl2
    {
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName(out IntPtr name);
        [PreserveSig] int SetDisplayName(IntPtr value, IntPtr ctx);
        [PreserveSig] int GetIconPath(out IntPtr path);
        [PreserveSig] int SetIconPath(IntPtr value, IntPtr ctx);
        [PreserveSig] int GetGroupingParam(out Guid param);
        [PreserveSig] int SetGroupingParam(IntPtr param, IntPtr ctx);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr client);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr client);
        [PreserveSig] int GetSessionIdentifier(out IntPtr id);
        [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr id);
        [PreserveSig] int GetProcessId(out uint pid);
        [PreserveSig] int IsSystemSoundsSession();
    }

    static class Native
    {
        [DllImport("combase.dll")] public static extern int RoGetActivationFactory(IntPtr classId, ref Guid iid, out IntPtr factory);
        [DllImport("combase.dll", CharSet = CharSet.Unicode)] public static extern int WindowsCreateString(string s, int length, out IntPtr hstring);
        [DllImport("combase.dll")] public static extern int WindowsDeleteString(IntPtr hstring);
        [DllImport("combase.dll")] public static extern IntPtr WindowsGetStringRawBuffer(IntPtr hstring, out int length);
        [DllImport("ole32.dll")] public static extern int PropVariantClear(ref PropVariant pv);
        [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(int access, bool inherit, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder buffer, ref int size);
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll")] public static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr min, IntPtr max);
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] public static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

        // Hand idle pages back to Windows (they come back on demand). Keeps Task Manager honest while we sit idle.
        public static void Trim()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, (IntPtr)(-1), (IntPtr)(-1));
        }

        public static void Release(object o) { if (o != null && Marshal.IsComObject(o)) Marshal.ReleaseComObject(o); }
    }

    static class HString
    {
        public static IntPtr Create(string s)
        {
            IntPtr h;
            Marshal.ThrowExceptionForHR(Native.WindowsCreateString(s, s.Length, out h));
            return h;
        }

        public static string Read(IntPtr h)
        {
            if (h == IntPtr.Zero) return "";
            int len;
            IntPtr p = Native.WindowsGetStringRawBuffer(h, out len);
            return Marshal.PtrToStringUni(p, len);
        }
    }

    // ---------- Per-app endpoint policy (undocumented; what Settings and EarTrumpet use) ----------

    class Policy
    {
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int SetFn(IntPtr self, uint pid, EDataFlow flow, ERole role, IntPtr deviceId);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetFn(IntPtr self, uint pid, EDataFlow flow, ERole role, out IntPtr deviceId);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int ClearFn(IntPtr self);

        // 21H2+ IID first, then the older one. Same vtable layout: IInspectable (6) + 19 methods, then Set/Get/Clear.
        static readonly string[] Iids = { "ab3d4648-e242-459f-b02f-541c70306324", "2a59116d-6c4f-45e0-a74f-707e3fef9258" };
        const int SetSlot = 25, GetSlot = 26, ClearSlot = 27;

        readonly IntPtr factory;
        readonly SetFn set; readonly GetFn get; readonly ClearFn clear;

        public static Policy TryCreate()
        {
            IntPtr cls = HString.Create("Windows.Media.Internal.AudioPolicyConfig");
            try
            {
                foreach (string s in Iids)
                {
                    Guid iid = new Guid(s);
                    IntPtr f;
                    if (Native.RoGetActivationFactory(cls, ref iid, out f) >= 0 && f != IntPtr.Zero) return new Policy(f);
                }
            }
            catch { }
            finally { Native.WindowsDeleteString(cls); }
            return null;
        }

        Policy(IntPtr f)
        {
            factory = f;
            IntPtr vt = Marshal.ReadIntPtr(f);
            set = (SetFn)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(vt, SetSlot * IntPtr.Size), typeof(SetFn));
            get = (GetFn)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(vt, GetSlot * IntPtr.Size), typeof(GetFn));
            clear = (ClearFn)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(vt, ClearSlot * IntPtr.Size), typeof(ClearFn));
        }

        // "" = follows the system default.
        public string Get(uint pid)
        {
            IntPtr h;
            if (get(factory, pid, EDataFlow.eRender, ERole.eMultimedia, out h) < 0) return "";
            try { return DeviceIds.FromPolicy(HString.Read(h)); }
            finally { if (h != IntPtr.Zero) Native.WindowsDeleteString(h); }
        }

        // deviceId "" = back to system default. Both roles, like the Settings app does.
        public void Set(uint pid, string deviceId)
        {
            IntPtr h = string.IsNullOrEmpty(deviceId) ? IntPtr.Zero : HString.Create(DeviceIds.ToPolicy(deviceId));
            try
            {
                Marshal.ThrowExceptionForHR(set(factory, pid, EDataFlow.eRender, ERole.eConsole, h));
                Marshal.ThrowExceptionForHR(set(factory, pid, EDataFlow.eRender, ERole.eMultimedia, h));
            }
            finally { if (h != IntPtr.Zero) Native.WindowsDeleteString(h); }
        }

        public void ClearAll() { Marshal.ThrowExceptionForHR(clear(factory)); }
    }

    static class DeviceIds
    {
        const string Prefix = @"\\?\SWD#MMDEVAPI#";
        const string RenderSuffix = "#{e6327cad-dcec-4949-ae8a-991e976a79d2}";

        public static string ToPolicy(string mmId) { return Prefix + mmId + RenderSuffix; }

        public static string FromPolicy(string p)
        {
            if (string.IsNullOrEmpty(p)) return "";
            int a = p.IndexOf("MMDEVAPI#", StringComparison.OrdinalIgnoreCase);
            if (a < 0) return p;
            a += "MMDEVAPI#".Length;
            int b = p.IndexOf("#{", a, StringComparison.Ordinal);
            return b < 0 ? p.Substring(a) : p.Substring(a, b - a);
        }
    }

    // ---------- Enumeration ----------

    class Device { public string Id; public string Name; }

    class AppInfo
    {
        public string Path; public string Name; public uint Pid; public bool Active; public string Current = "";
    }

    static class Audio
    {
        const int DEVICE_STATE_ACTIVE = 1, CLSCTX_ALL = 23;
        static readonly PropertyKey FriendlyName = new PropertyKey(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);
        static readonly Dictionary<string, string> nameCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // pid -> exe path; pruned every scan so a recycled PID can't keep a stale path.
        static Dictionary<uint, string> pathCache = new Dictionary<uint, string>();
        static IMMDeviceEnumerator enumerator;
        static IMMDeviceEnumerator Enumerator() { return enumerator ?? (enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCo()); }

        static string Name(IMMDevice d)
        {
            IPropertyStore store;
            if (d.OpenPropertyStore(0, out store) < 0) return "?";
            PropertyKey key = FriendlyName;
            PropVariant pv;
            try
            {
                if (store.GetValue(ref key, out pv) < 0) return "?";
                try { return pv.vt == 31 ? Marshal.PtrToStringUni(pv.pointer) : "?"; }
                finally { Native.PropVariantClear(ref pv); }
            }
            finally { Native.Release(store); }
        }

        static List<IMMDevice> Active()
        {
            var list = new List<IMMDevice>();
            IMMDeviceCollection col;
            Marshal.ThrowExceptionForHR(Enumerator().EnumAudioEndpoints(EDataFlow.eRender, DEVICE_STATE_ACTIVE, out col));
            int n; col.GetCount(out n);
            for (int i = 0; i < n; i++) { IMMDevice d; if (col.Item(i, out d) >= 0) list.Add(d); }
            Native.Release(col);
            return list;
        }

        public static List<Device> Outputs(out string defaultName)
        {
            IMMDevice def;
            defaultName = Enumerator().GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out def) >= 0 ? Name(def) : "";
            Native.Release(def);
            return Active().Select(d => { string id; d.GetId(out id); var dev = new Device { Id = id, Name = Name(d) }; Native.Release(d); return dev; })
                           .OrderBy(d => d.Name).ToList();
        }

        public static string ProcessPath(uint pid)
        {
            IntPtr h = Native.OpenProcess(0x1000 /* QUERY_LIMITED_INFORMATION */, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024); int size = sb.Capacity;
                return Native.QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
            }
            finally { Native.CloseHandle(h); }
        }

        public static string AppName(string path)
        {
            string name;
            if (nameCache.TryGetValue(path, out name)) return name;
            try { name = (FileVersionInfo.GetVersionInfo(path).FileDescription ?? "").Trim(); } catch { name = ""; }
            if (name.Length == 0) name = System.IO.Path.GetFileNameWithoutExtension(path);
            nameCache[path] = name;
            return name;
        }

        // One entry per executable: the policy is stored per app, so any of its PIDs works.
        public static List<AppInfo> Apps()
        {
            var byPath = new Dictionary<string, AppInfo>(StringComparer.OrdinalIgnoreCase);
            var seenPaths = new Dictionary<uint, string>();
            uint self = (uint)Process.GetCurrentProcess().Id;
            Guid iid = typeof(IAudioSessionManager2).GUID;
            foreach (IMMDevice d in Active())
            {
                object o = null; IAudioSessionEnumerator sessions = null;
                try
                {
                    if (d.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out o) < 0) continue;
                    if (((IAudioSessionManager2)o).GetSessionEnumerator(out sessions) < 0) continue;
                    int n; sessions.GetCount(out n);
                    for (int i = 0; i < n; i++)
                    {
                        IAudioSessionControl2 s = null;
                        try
                        {
                            if (sessions.GetSession(i, out s) < 0) continue;
                            int state; uint pid;
                            s.GetState(out state);
                            if (state == 2 || s.IsSystemSoundsSession() == 0) continue; // expired / system sounds
                            s.GetProcessId(out pid);
                            if (pid == 0 || pid == self) continue;
                            string path;
                            if (!pathCache.TryGetValue(pid, out path)) path = ProcessPath(pid);
                            if (path == null) continue; // process already gone
                            seenPaths[pid] = path;
                            AppInfo app;
                            if (!byPath.TryGetValue(path, out app))
                                byPath[path] = app = new AppInfo { Path = path, Name = AppName(path), Pid = pid };
                            if (state == 1) { app.Active = true; app.Pid = pid; }
                        }
                        catch { } // one broken session must not hide the rest
                        finally { Native.Release(s); }
                    }
                }
                catch { }
                finally { Native.Release(sessions); Native.Release(o); Native.Release(d); }
            }
            pathCache = seenPaths;
            return byPath.Values.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
    }

    // ---------- UI ----------

    class DeviceItem
    {
        public string Id; public string Text;
        public override string ToString() { return Text; }
    }

    class AppRow : Panel
    {
        public AppInfo App;
        public readonly ComboBox Combo = new ComboBox();
        public readonly Label Title = new Label();
        public readonly Label State = new Label();
        public readonly PictureBox Icon = new PictureBox();
    }

    class MainForm : Form
    {
        // Follows the Windows app theme (Settings > Personalization > Colors), read once at start.
        // Same palette as the Chrome extension popup.
        static readonly bool Dark = !Icons.LightApps();
        static readonly Color Bg = Dark ? Color.FromArgb(24, 24, 27) : Color.White;
        static readonly Color Fg = Dark ? Color.FromArgb(244, 244, 245) : Color.FromArgb(24, 24, 27);
        static readonly Color Surface = Dark ? Color.FromArgb(39, 39, 42) : Color.White;
        static readonly Color Accent = Dark ? Color.FromArgb(96, 165, 250) : Color.FromArgb(37, 99, 235);
        static readonly Color AccentSoft = Dark ? Color.FromArgb(30, 41, 59) : Color.FromArgb(239, 246, 255);
        static readonly Color Muted = Dark ? Color.FromArgb(161, 161, 170) : Color.FromArgb(107, 114, 128);
        static readonly Color Line = Dark ? Color.FromArgb(63, 63, 70) : Color.FromArgb(229, 231, 235);
        static readonly Color Warn = Dark ? Color.FromArgb(251, 191, 36) : Color.FromArgb(180, 83, 9);
        static readonly Color WarnSoft = Dark ? Color.FromArgb(41, 34, 17) : Color.FromArgb(255, 251, 235);
        static readonly Color Err = Dark ? Color.FromArgb(248, 113, 113) : Color.FromArgb(220, 38, 38);

        readonly Policy policy = Policy.TryCreate();
        readonly FlowLayoutPanel list = new FlowLayoutPanel();
        readonly Label status = new Label();
        readonly Label empty = new Label();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly List<AppRow> rows = new List<AppRow>();
        readonly float scale;
        const int FocusedMs = 1500, BackgroundMs = 6000;
        // Shared: one GDI font each, not two per row per rebuild.
        readonly Font titleFont = new Font("Segoe UI Semibold", 10.5f);
        readonly Font stateFont = new Font("Segoe UI", 8.5f);
        string lastSig;
        bool suppress;

        int P(int px) { return (int)Math.Round(px * scale); }

        public MainForm()
        {
            using (Graphics g = CreateGraphics()) scale = g.DpiX / 96f;
            AutoScaleMode = AutoScaleMode.None;
            Text = "Audio Router · Apps";
            Font = new Font("Segoe UI", 10f);
            BackColor = Bg;
            ForeColor = Fg;
            ClientSize = new Size(P(560), P(520));
            MinimumSize = new Size(P(460), P(360));
            StartPosition = FormStartPosition.CenterScreen;
            Icon = Icons.Glyph(32, !Dark); // dark glyph on a light title bar, light on a dark one

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(P(20), P(16), P(20), P(12)) };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var header = new Label { Text = "¿Dónde suena cada app?", Font = new Font("Segoe UI Semibold", 15f), AutoSize = true, Margin = new Padding(0) };
            var sub = new Label
            {
                Text = "Elige la salida de cada app que está usando audio. Windows lo recuerda.",
                ForeColor = Muted, AutoSize = true, Margin = new Padding(0, P(2), 0, P(12)),
            };

            var banner = new Label
            {
                Text = "Tu versión de Windows no deja cambiar esto desde aquí. Usa el mezclador de Windows (abajo).",
                ForeColor = Warn, BackColor = WarnSoft, AutoSize = true, Padding = new Padding(P(8)),
                Margin = new Padding(0, 0, 0, P(10)), Visible = policy == null,
            };

            list.Dock = DockStyle.Fill;
            list.FlowDirection = FlowDirection.TopDown;
            list.WrapContents = false;
            list.AutoScroll = true;
            list.Margin = new Padding(0);
            list.Resize += (s, e) => FitRows();

            empty.Text = "No hay apps sonando ahora.\nAbre el juego o pon música y aparecerá aquí sola.";
            empty.ForeColor = Muted;
            empty.AutoSize = true;
            empty.Margin = new Padding(0, P(24), 0, 0);

            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, AutoSize = true, Margin = new Padding(0, P(10), 0, 0) };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            status.AutoSize = true;
            status.ForeColor = Muted;
            status.Anchor = AnchorStyles.Left;
            status.MaximumSize = new Size(P(300), 0);
            var mixer = new LinkLabel { Text = "Mezclador de Windows", AutoSize = true, Anchor = AnchorStyles.Right, LinkColor = Accent, Margin = new Padding(P(8), 0, P(8), 0) };
            mixer.LinkClicked += (s, e) => { try { Process.Start("ms-settings:apps-volume"); } catch { } };
            var reset = new Button { Text = "Restablecer todo", AutoSize = true, Anchor = AnchorStyles.Right, Enabled = policy != null, FlatStyle = FlatStyle.System };
            if (Dark)
            {
                reset.FlatStyle = FlatStyle.Flat;
                reset.BackColor = Surface;
                reset.FlatAppearance.BorderColor = Line;
                reset.Padding = new Padding(P(8), P(2), P(8), P(2));
            }
            reset.Click += (s, e) => ResetAll();
            footer.Controls.Add(status, 0, 0);
            footer.Controls.Add(mixer, 1, 0);
            footer.Controls.Add(reset, 2, 0);

            root.Controls.Add(header, 0, 0);
            root.Controls.Add(sub, 0, 1);
            root.Controls.Add(banner, 0, 2);
            root.Controls.Add(list, 0, 3);
            root.Controls.Add(footer, 0, 4);
            Controls.Add(root);

            // ponytail: polling instead of COM notification callbacks. Fast while you're using the window,
            // slow in the background, fully stopped when minimized. Upgrade to IAudioSessionNotification if ever needed.
            timer.Interval = FocusedMs;
            timer.Tick += (s, e) => RefreshAll();
            Shown += (s, e) => { RefreshAll(); timer.Start(); Native.Trim(); };
            FormClosed += (s, e) => timer.Dispose(); // a live Timer would keep ticking on a disposed form
            Activated += (s, e) => { timer.Interval = FocusedMs; if (WindowState != FormWindowState.Minimized) { RefreshAll(); timer.Start(); } };
            Deactivate += (s, e) => { timer.Interval = BackgroundMs; Native.Trim(); };
            Resize += (s, e) =>
            {
                if (WindowState == FormWindowState.Minimized) { timer.Stop(); Native.Trim(); }
                else if (!timer.Enabled) { RefreshAll(); timer.Start(); }
            };
        }

        static void DrawDarkItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var combo = (ComboBox)sender;
            // Highlight only inside the open list; the closed box keeps the plain surface
            bool hot = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
            using (var b = new SolidBrush(hot ? AccentSoft : Surface)) e.Graphics.FillRectangle(b, e.Bounds);
            var text = new Rectangle(e.Bounds.X + 4, e.Bounds.Y, e.Bounds.Width - 4, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, combo.Items[e.Index].ToString(), combo.Font, text, Fg,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (!Dark) return;
            int on = 1;
            Native.DwmSetWindowAttribute(Handle, 20, ref on, 4); // DWMWA_USE_IMMERSIVE_DARK_MODE: dark title bar
            Native.SetWindowTheme(list.Handle, "DarkMode_Explorer", null); // dark scroll bar
        }

        void SetStatus(string text, bool error)
        {
            status.Text = text;
            status.ForeColor = error ? Err : Muted;
        }

        void RefreshAll()
        {
            List<Device> devices; List<AppInfo> apps; string defaultName;
            try
            {
                devices = Audio.Outputs(out defaultName);
                apps = Audio.Apps();
            }
            catch (Exception ex) { SetStatus("No pude leer el audio: " + ex.Message, true); return; }

            foreach (AppInfo a in apps)
                try { a.Current = policy != null ? policy.Get(a.Pid) : ""; } catch { a.Current = ""; }

            string sig = defaultName + "#" + string.Join("|", devices.Select(d => d.Id + "=" + d.Name)) + "#" + string.Join("|", apps.Select(a => a.Path));
            if (sig != lastSig)
            {
                lastSig = sig;
                Rebuild(devices, defaultName, apps);
            }
            else
            {
                foreach (AppRow r in rows)
                {
                    AppInfo a = apps.FirstOrDefault(x => string.Equals(x.Path, r.App.Path, StringComparison.OrdinalIgnoreCase));
                    if (a == null) continue;
                    r.App = a;
                    // Don't fight the user while the dropdown is open.
                    if (!r.Combo.DroppedDown) SelectDevice(r, a.Current);
                    Style(r);
                }
            }
        }

        void Rebuild(List<Device> devices, string defaultName, List<AppInfo> apps)
        {
            list.SuspendLayout();
            foreach (AppRow r in rows) r.Dispose();
            rows.Clear();
            list.Controls.Clear();

            if (apps.Count == 0) list.Controls.Add(empty);

            foreach (AppInfo a in apps)
            {
                var r = new AppRow { App = a, Height = P(60), Margin = new Padding(0, 0, 0, P(6)) };
                r.Icon.Image = IconFor(a.Path);
                r.Icon.SizeMode = PictureBoxSizeMode.Zoom;
                r.Icon.SetBounds(P(12), P(14), P(32), P(32));

                r.Title.Text = a.Name;
                r.Title.Font = titleFont;
                r.Title.AutoEllipsis = true;
                r.Title.SetBounds(P(56), P(9), P(200), P(22));

                r.State.Font = stateFont;
                r.State.AutoEllipsis = true;
                r.State.SetBounds(P(56), P(31), P(200), P(18));

                r.Combo.DropDownStyle = ComboBoxStyle.DropDownList;
                r.Combo.FlatStyle = FlatStyle.System;
                if (Dark)
                {
                    // Flat alone still paints the closed box white: draw the items ourselves
                    r.Combo.FlatStyle = FlatStyle.Flat;
                    r.Combo.BackColor = Surface;
                    r.Combo.ForeColor = Fg;
                    r.Combo.DrawMode = DrawMode.OwnerDrawFixed;
                    r.Combo.ItemHeight = P(20);
                    r.Combo.DrawItem += DrawDarkItem;
                }
                r.Combo.Width = P(230);
                r.Combo.Enabled = policy != null;
                r.Combo.Items.Add(new DeviceItem { Id = "", Text = "Predeterminado" + (defaultName.Length > 0 ? " (" + defaultName + ")" : "") });
                foreach (Device d in devices) r.Combo.Items.Add(new DeviceItem { Id = d.Id, Text = d.Name });
                AppRow row = r;
                r.Combo.SelectedIndexChanged += (s, e) => OnPick(row);
                r.Combo.DropDownWidth = Math.Max(r.Combo.Width, P(320));
                r.Paint += (s, e) => { using (var pen = new Pen(Line)) e.Graphics.DrawRectangle(pen, 0, 0, row.Width - 1, row.Height - 1); };

                r.Controls.Add(r.Icon);
                r.Controls.Add(r.Title);
                r.Controls.Add(r.State);
                r.Controls.Add(r.Combo);
                SelectDevice(r, a.Current);
                Style(r);
                rows.Add(r);
                list.Controls.Add(r);
            }
            list.ResumeLayout();
            FitRows();
        }

        void FitRows()
        {
            int w = list.ClientSize.Width - (list.VerticalScroll.Visible ? 0 : 1);
            foreach (AppRow r in rows)
            {
                r.Width = w;
                r.Combo.Location = new Point(w - r.Combo.Width - P(12), (r.Height - r.Combo.Height) / 2);
                int textW = Math.Max(P(60), r.Combo.Left - P(56) - P(10));
                r.Title.Width = textW;
                r.State.Width = textW;
                r.Invalidate();
            }
        }

        static Image IconFor(string path) { return Icons.For(path); }

        void SelectDevice(AppRow r, string deviceId)
        {
            suppress = true;
            try
            {
                int idx = -1;
                for (int i = 0; i < r.Combo.Items.Count; i++)
                    if (string.Equals(((DeviceItem)r.Combo.Items[i]).Id, deviceId, StringComparison.OrdinalIgnoreCase)) { idx = i; break; }
                if (idx < 0)
                {
                    // Remembered device isn't connected: Windows plays on the default meanwhile.
                    idx = r.Combo.Items.Add(new DeviceItem { Id = deviceId, Text = "⚠ Desconectado (suena en predeterminado)" });
                }
                if (r.Combo.SelectedIndex != idx) r.Combo.SelectedIndex = idx;
            }
            finally { suppress = false; }
        }

        public static bool IsBrowser(string path)
        {
            string f = Path.GetFileName(path).ToLowerInvariant();
            return f == "chrome.exe" || f == "msedge.exe" || f == "brave.exe";
        }

        void Style(AppRow r)
        {
            var item = r.Combo.SelectedItem as DeviceItem;
            bool routed = item != null && item.Id.Length > 0;
            bool lost = item != null && item.Text.StartsWith("⚠");
            r.BackColor = lost ? WarnSoft : routed ? AccentSoft : Bg;
            string state = r.App.Active ? "● Sonando ahora" : "En silencio";
            if (IsBrowser(r.App.Path)) state += " · las pestañas de la extensión mandan";
            r.State.Text = state;
            r.State.ForeColor = lost ? Warn : r.App.Active ? Accent : Muted;
        }

        void OnPick(AppRow r)
        {
            if (suppress || policy == null) return;
            var item = r.Combo.SelectedItem as DeviceItem;
            if (item == null) return;
            try
            {
                policy.Set(r.App.Pid, item.Id);
                r.App.Current = item.Id;
                SetStatus(r.App.Name + " → " + (item.Id.Length == 0 ? "predeterminado" : item.Text) + " ✓", false);
            }
            catch (Exception ex)
            {
                SetStatus("No se pudo cambiar " + r.App.Name + ": " + ex.Message + ". ¿Se cerró la app?", true);
                lastSig = null; // force a full rebuild with fresh PIDs
            }
            Style(r);
        }

        void ResetAll()
        {
            if (MessageBox.Show(this, "¿Regresar todas las apps a la salida predeterminada?", "Restablecer todo",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try { policy.ClearAll(); SetStatus("Todas las apps vuelven al predeterminado ✓", false); }
            catch (Exception ex) { SetStatus("No se pudo restablecer: " + ex.Message, true); }
            lastSig = null;
            RefreshAll();
        }
    }

    static class Icons
    {
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr handle);
        static readonly Dictionary<string, Image> cache = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);

        // App icons, extracted once per exe and shared by the window and the tray menu.
        public static Image For(string path)
        {
            Image img;
            if (cache.TryGetValue(path, out img)) return img;
            try
            {
                using (Icon ic = Icon.ExtractAssociatedIcon(path))
                using (Bitmap big = ic.ToBitmap())
                    img = new Bitmap(big, 16, 16);
            }
            catch { img = null; }
            cache[path] = img;
            return img;
        }

        static bool HasFont(string name) { using (var f = new Font(name, 10)) return f.Name == name; }

        // Speaker glyph from the Windows icon font, so no .ico file has to ship.
        public static Icon Glyph(int size, bool dark)
        {
            string font = HasFont("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
            using (var bmp = new Bitmap(size, size))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                using (var f = new Font(font, size * 0.8f, GraphicsUnit.Pixel))
                using (var b = new SolidBrush(dark ? Color.FromArgb(24, 24, 27) : Color.White))
                using (var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    g.DrawString("", f, b, new RectangleF(0, 0, size, size), fmt);
                }
                IntPtr h = bmp.GetHicon();
                try { using (Icon tmp = Icon.FromHandle(h)) return (Icon)tmp.Clone(); }
                finally { DestroyIcon(h); }
            }
        }

        public static bool LightApps()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    return k == null || Convert.ToInt32(k.GetValue("AppsUseLightTheme", 1)) == 1;
            }
            catch { return true; }
        }

        public static bool LightTaskbar()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    return k != null && Convert.ToInt32(k.GetValue("SystemUsesLightTheme", 0)) == 1;
            }
            catch { return false; }
        }
    }

    // Lives next to the clock. Right click = every app and where it plays; pick a device right there.
    // Idle cost is ~0: nothing polls, the menu reads the audio state only when it opens.
    class TrayApp : ApplicationContext
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", RunName = "AudioRouterApps";

        readonly NotifyIcon notify = new NotifyIcon();
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        readonly Policy policy = Policy.TryCreate();
        readonly Control invoker = new Control();
        readonly RegisteredWaitHandle showWait;
        readonly Font bold;
        AudioRouterApps.MainForm form;
        bool hinted;

        public TrayApp(bool showWindow, WaitHandle showSignal)
        {
            invoker.CreateControl();
            IntPtr force = invoker.Handle; // handle needed for cross-thread BeginInvoke
            bold = new Font(menu.Font, FontStyle.Bold);

            notify.Icon = Icons.Glyph(SystemInformation.SmallIconSize.Width, Icons.LightTaskbar());
            notify.Text = "Audio Router";
            notify.ContextMenuStrip = menu;
            notify.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowWindow(); };
            notify.Visible = true;
            menu.Opening += (s, e) => { BuildMenu(); e.Cancel = false; };
            menu.Closed += (s, e) => TrimSoon();
            SystemEvents.UserPreferenceChanged += OnThemeChanged;

            // A second launch (double-clicking the exe again) just brings the window up.
            showWait = ThreadPool.RegisterWaitForSingleObject(showSignal,
                (st, timedOut) => invoker.BeginInvoke((Action)ShowWindow), null, -1, false);

            try { if (Autostart) Autostart = true; } catch { } // self-heal the path if the exe moved

            if (showWindow) ShowWindow();
            else TrimSoon();
        }

        void OnThemeChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General) return;
            Icon old = notify.Icon;
            notify.Icon = Icons.Glyph(SystemInformation.SmallIconSize.Width, Icons.LightTaskbar());
            if (old != null) old.Dispose();
        }

        static bool Autostart
        {
            get
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue(RunName) != null;
            }
            set
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (value) k.SetValue(RunName, "\"" + Application.ExecutablePath + "\" --tray");
                    else k.DeleteValue(RunName, false);
                }
            }
        }

        void ShowWindow()
        {
            if (form == null)
            {
                form = new AudioRouterApps.MainForm();
                form.FormClosed += (s, e) =>
                {
                    form = null;
                    if (!hinted)
                    {
                        hinted = true;
                        notify.ShowBalloonTip(4000, "Audio Router sigue aquí", "Clic derecho en el ícono junto al reloj para cambiar salidas.", ToolTipIcon.Info);
                    }
                    TrimSoon(); // after the form is fully disposed
                };
                form.Show();
            }
            else
            {
                if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal;
                form.Activate();
            }
        }

        // Trim once things settle, not mid-startup when the runtime is still allocating.
        static void TrimSoon()
        {
            var t = new System.Windows.Forms.Timer { Interval = 3000 };
            t.Tick += (s, e) => { t.Dispose(); Native.Trim(); };
            t.Start();
        }

        static ToolStripMenuItem Disabled(string text) { return new ToolStripMenuItem(text) { Enabled = false }; }

        void BuildMenu()
        {
            foreach (ToolStripItem it in menu.Items.Cast<ToolStripItem>().ToList()) it.Dispose();
            menu.Items.Clear();

            menu.Items.Add(new ToolStripMenuItem("¿Dónde suena cada app?") { Enabled = false, Font = bold });
            menu.Items.Add(new ToolStripSeparator());

            int routed = 0;
            try
            {
                string defaultName;
                List<Device> devices = Audio.Outputs(out defaultName);
                List<AppInfo> apps = Audio.Apps();
                if (policy == null) menu.Items.Add(Disabled("Windows no deja cambiar salidas desde aquí"));
                if (apps.Count == 0) menu.Items.Add(Disabled("Nada está sonando ahora"));

                foreach (AppInfo a in apps)
                {
                    string current = "";
                    try { if (policy != null) current = policy.Get(a.Pid); } catch { }
                    Device dev = devices.FirstOrDefault(d => string.Equals(d.Id, current, StringComparison.OrdinalIgnoreCase));
                    string dest = current.Length == 0 ? "Predeterminado" : dev != null ? dev.Name : "⚠ Desconectado";
                    if (current.Length > 0) routed++;

                    var item = new ToolStripMenuItem((a.Active ? "● " : "") + a.Name + "   →   " + dest) { Image = Icons.For(a.Path) };
                    if (AudioRouterApps.MainForm.IsBrowser(a.Path)) item.ToolTipText = "Las pestañas enrutadas con la extensión mandan sobre esto";
                    item.Enabled = policy != null;

                    AppInfo app = a;
                    var def = new ToolStripMenuItem("Predeterminado" + (defaultName.Length > 0 ? " (" + defaultName + ")" : "")) { Checked = current.Length == 0 };
                    def.Click += (s, e) => Pick(app, "");
                    item.DropDownItems.Add(def);
                    item.DropDownItems.Add(new ToolStripSeparator());
                    foreach (Device d in devices)
                    {
                        Device target = d;
                        var di = new ToolStripMenuItem(d.Name) { Checked = dev == d };
                        di.Click += (s, e) => Pick(app, target.Id);
                        item.DropDownItems.Add(di);
                    }
                    menu.Items.Add(item);
                }
            }
            catch (Exception ex) { menu.Items.Add(Disabled("No pude leer el audio: " + ex.Message)); }

            SetTooltip(routed);

            menu.Items.Add(new ToolStripSeparator());
            var open = new ToolStripMenuItem("Abrir ventana") { Font = bold };
            open.Click += (s, e) => ShowWindow();
            var auto = new ToolStripMenuItem("Iniciar con Windows") { Checked = Autostart };
            auto.Click += (s, e) =>
            {
                try { Autostart = !Autostart; }
                catch (Exception ex) { notify.ShowBalloonTip(4000, "Audio Router", "No se pudo cambiar el inicio: " + ex.Message, ToolTipIcon.Error); }
            };
            var reset = new ToolStripMenuItem("Restablecer todo") { Enabled = policy != null };
            reset.Click += (s, e) =>
            {
                if (MessageBox.Show("¿Regresar todas las apps a la salida predeterminada?", "Restablecer todo",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                try { policy.ClearAll(); SetTooltip(0); }
                catch (Exception ex) { notify.ShowBalloonTip(4000, "Audio Router", "No se pudo restablecer: " + ex.Message, ToolTipIcon.Error); }
            };
            var quit = new ToolStripMenuItem("Salir");
            quit.Click += (s, e) => Quit();
            menu.Items.AddRange(new ToolStripItem[] { open, auto, reset, quit });
        }

        void SetTooltip(int routed)
        {
            string t = routed == 0 ? "Audio Router · todo en predeterminado"
                     : "Audio Router · " + routed + (routed == 1 ? " app con salida propia" : " apps con salida propia");
            notify.Text = t.Length > 63 ? t.Substring(0, 63) : t; // NotifyIcon throws past 63 chars
        }

        void Pick(AppInfo app, string deviceId)
        {
            try { policy.Set(app.Pid, deviceId); }
            catch (Exception ex)
            {
                notify.ShowBalloonTip(4000, "Audio Router", "No se pudo cambiar " + app.Name + ": " + ex.Message + ". ¿Se cerró la app?", ToolTipIcon.Error);
            }
        }

        void Quit()
        {
            SystemEvents.UserPreferenceChanged -= OnThemeChanged;
            showWait.Unregister(null);
            if (form != null) form.Close();
            notify.Visible = false;
            notify.Dispose();
            ExitThread();
        }
    }

    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Contains("--selftest")) return SelfTest();
            if (args.Contains("--dump")) return Dump();
            int di = Array.IndexOf(args, "--default");
            if (di >= 0) return SetDefault(di + 1 < args.Length ? args[di + 1] : "");
            bool tray = args.Contains("--tray"); // autostart: icon only, no window
            bool first;
            using (var single = new Mutex(true, @"Local\AudioRouterApps", out first))
            using (var show = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\AudioRouterApps.Show"))
            {
                if (!first) { if (!tray) show.Set(); return 0; }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                // A tray app must never die silently: log, tell the user, keep running.
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (s, e) => Log(e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) => Log(e.ExceptionObject as Exception);
                Log(null, "start tray=" + tray);
                Application.Run(new TrayApp(!tray, show));
                GC.KeepAlive(single);
            }
            return 0;
        }

        // --default "texto": the first output whose name contains it becomes Windows' default for
        // every role (scenes in Game-buttons call this). Exit 0 = done, 2 = no such output, 1 = Windows refused.
        static int SetDefault(string part)
        {
            string ignored;
            Device d = part.Length == 0 ? null
                : Audio.Outputs(out ignored).FirstOrDefault(x => x.Name.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0);
            if (d == null) { Console.WriteLine("no output matches: " + part); return 2; }
            var pc = (IPolicyConfig)new PolicyConfigClient();
            try
            {
                foreach (ERole role in new[] { ERole.eConsole, ERole.eMultimedia, ERole.eCommunications })
                    if (pc.SetDefaultEndpoint(d.Id, role) < 0) return 1;
            }
            finally { Marshal.ReleaseComObject(pc); }
            Console.WriteLine("default: " + d.Name);
            return 0;
        }

        public static readonly string LogPath = Path.Combine(Path.GetTempPath(), "AudioRouterApps.log");

        public static void Log(Exception ex, string note = null)
        {
            try
            {
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 256 * 1024) File.Delete(LogPath);
                File.AppendAllText(LogPath, DateTime.Now.ToString("s") + " " + (note ?? "") + (ex != null ? " " + ex : "") + Environment.NewLine);
            }
            catch { }
        }

        static int SelfTest()
        {
            int fails = 0;
            Action<bool, string> check = (ok, name) => { if (!ok) { fails++; Console.WriteLine("FAIL " + name); } };
            string mm = "{0.0.0.00000000}.{8a5b4b0e-1111-2222-3333-444455556666}";
            check(DeviceIds.FromPolicy(DeviceIds.ToPolicy(mm)) == mm, "roundtrip");
            check(DeviceIds.FromPolicy("") == "", "empty");
            check(DeviceIds.FromPolicy(@"\\?\swd#mmdevapi#" + mm + "#{e6327cad-dcec-4949-ae8a-991e976a79d2}") == mm, "case-insensitive prefix");
            check(DeviceIds.FromPolicy(mm) == mm, "already plain");
            Console.WriteLine(fails == 0 ? "selftest ok" : fails + " failed");
            return fails;
        }

        // Read-only diagnostics: lists devices, audio apps and their current per-app output. Changes nothing.
        static int Dump()
        {
            Policy p = Policy.TryCreate();
            Console.WriteLine("policy: " + (p != null ? "ok" : "NOT AVAILABLE"));
            string def;
            foreach (Device d in Audio.Outputs(out def)) Console.WriteLine("device: " + d.Name + "  " + d.Id);
            Console.WriteLine("default: " + def);
            foreach (AppInfo a in Audio.Apps())
                Console.WriteLine("app: " + a.Name + " pid=" + a.Pid + " active=" + a.Active + " current=" + (p != null ? p.Get(a.Pid) : "?"));
            return 0;
        }
    }
}
