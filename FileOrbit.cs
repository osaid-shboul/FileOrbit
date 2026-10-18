// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace FileOrbit
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool created;
            using (var mutex = new System.Threading.Mutex(true, "Local\\FileOrbit.Tray", out created))
            {
                if (!created)
                {
                    MessageBox.Show("FileOrbit is already running. / الأداة قيد التشغيل", "FileOrbit");
                    return;
                }
                Native.SetProcessDPIAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApp(args));
            }
        }
    }

    static class Native
    {
        public const int WH_MOUSE_LL = 14;
        public const int WM_MOUSEMOVE = 0x200, WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202;
        public const int WM_RBUTTONDOWN = 0x204, WM_RBUTTONUP = 0x205;
        public const int GA_ROOT = 2;
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }
        [StructLayout(LayoutKind.Sequential)] public struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData, flags, time; public IntPtr extraInfo; }
        public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] public static extern IntPtr SetWindowsHookEx(int idHook, HookProc callback, IntPtr module, uint threadId);
        [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT point);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hwnd, StringBuilder name, int maxCount);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)] public static extern IntPtr GetModuleHandle(string moduleName);
        [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int cx, cy; public SIZE(int x, int y) { cx = x; cy = y; } }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr screenDc, ref POINT dst, ref SIZE size, IntPtr srcDc, ref POINT src, int colorKey, ref BLENDFUNCTION blend, int flags);
    }

    class Settings
    {
        public bool ShiftDrag = true;
        public string Language = "ar";
        public static string PathName
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FileOrbit", "settings.ini"); }
        }
        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(PathName)) return s;
                foreach (string line in File.ReadAllLines(PathName))
                {
                    int at = line.IndexOf('=');
                    if (at < 0) continue;
                    string key = line.Substring(0, at).Trim(), value = line.Substring(at + 1).Trim();
                    if (key == "shift_drag") s.ShiftDrag = value == "1";
                    if (key == "language") s.Language = value == "en" ? "en" : "ar";
                }
            }
            catch { }
            return s;
        }
    }

    class MenuAction
    {
        public string Id, Ar, En;
        public MenuAction(string id, string ar, string en) { Id = id; Ar = ar; En = en; }
        public string Label(string language) { return language == "en" ? En : Ar; }
    }

    class TrayApp : ApplicationContext
    {
        private readonly NotifyIcon tray;
        private readonly Timer timer;
        private readonly Native.HookProc hookProc;
        private IntPtr hook;
        private Settings settings;
        private Wheel wheel;
        private List<string> activeFiles = new List<string>();
        private bool leftDown, shiftArmed, oleDragActive, pendingDropResolution;
        private bool pendingShiftWheel;
        private Point pendingShiftPoint;
        private Point oleReleasePoint;
        private DateTime oleReleaseAt = DateTime.MinValue;
        private string pendingAction;
        private List<string> pendingFiles;
        private Point downPoint;
        private DateTime lastSettingsRead = DateTime.MinValue;
        private DateTime lastOutsideClick = DateTime.MinValue;
        private readonly string[] startupFiles;

        public TrayApp(string[] args)
        {
            startupFiles = args.Where(File.Exists).ToArray();
            settings = Settings.Load();
            tray = new NotifyIcon();
            tray.Icon = CreateIcon();
            tray.Text = "FileOrbit";
            tray.Visible = true;
            tray.MouseClick += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ShowLauncher(); };
            hookProc = MouseCallback;
            hook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, hookProc, Native.GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero) MessageBox.Show("Could not register mouse gesture listener.", "FileOrbit");
            timer = new Timer();
            timer.Interval = 35;
            timer.Tick += TimerTick;
            timer.Start();
            if (startupFiles.Length > 0)
                timer.Tick += OpenStartupOnce;
        }

        private Icon CreateIcon()
        {
            using (var bmp = new Bitmap(32, 32))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (var b = new SolidBrush(Color.FromArgb(239, 130, 72))) g.FillEllipse(b, 2, 2, 28, 28);
                using (var b = new SolidBrush(Color.FromArgb(255, 242, 213))) g.FillEllipse(b, 10, 10, 12, 12);
                return Icon.FromHandle(bmp.GetHicon());
            }
        }

        private void OpenStartupOnce(object sender, EventArgs e)
        {
            timer.Tick -= OpenStartupOnce;
            ShowWheel(startupFiles.ToList(), Cursor.Position);
        }

        private void TimerTick(object sender, EventArgs e)
        {
            if (pendingAction != null)
            {
                string action = pendingAction;
                List<string> files = pendingFiles;
                pendingAction = null; pendingFiles = null;
                LaunchWorker(action, files);
            }
            if ((DateTime.UtcNow - lastSettingsRead).TotalSeconds > 3)
            {
                settings = Settings.Load();
                lastSettingsRead = DateTime.UtcNow;
            }
            if (pendingShiftWheel && leftDown && wheel == null)
            {
                pendingShiftWheel = false;
                if (IsExplorerAt(downPoint)) ShowWheel(new List<string>(), pendingShiftPoint);
            }
            if (wheel != null && pendingDropResolution && !leftDown &&
                (DateTime.UtcNow - oleReleaseAt).TotalMilliseconds > 350)
            {
                pendingDropResolution = false;
                if (oleDragActive) CompleteWheel(oleReleasePoint);
                else
                {
                    activeFiles = ExplorerSelection(downPoint);
                    if (activeFiles.Count > 0) wheel.SetActions(ActionsFor(activeFiles));
                    else CompleteWheel((MenuAction)null);
                }
            }
            if (wheel != null) wheel.UpdateHover(Cursor.Position);
        }

        private IntPtr MouseCallback(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code < 0) return Native.CallNextHookEx(hook, code, wParam, lParam);
            var info = (Native.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.MSLLHOOKSTRUCT));
            Point p = new Point(info.pt.X, info.pt.Y);
            int message = wParam.ToInt32();
            if (wheel != null && (message == Native.WM_LBUTTONDOWN || message == Native.WM_RBUTTONDOWN) &&
                (message == Native.WM_RBUTTONDOWN || wheel.ActionAt(p) == null))
            {
                CompleteWheel((MenuAction)null);
                lastOutsideClick = DateTime.UtcNow;
                leftDown = shiftArmed = pendingShiftWheel = false;
            }
            if (message == Native.WM_LBUTTONDOWN && wheel == null &&
                (DateTime.UtcNow - lastOutsideClick).TotalMilliseconds > 200)
            {
                leftDown = true; downPoint = p;
                shiftArmed = settings.ShiftDrag && (Native.GetAsyncKeyState(0x10) & 0x8000) != 0;
            }
            else if (message == Native.WM_MOUSEMOVE && leftDown && wheel == null && settings.ShiftDrag &&
                (shiftArmed || (Native.GetAsyncKeyState(0x10) & 0x8000) != 0) && Distance(downPoint, p) > 30)
            {
                if (!shiftArmed) shiftArmed = true;
                pendingShiftWheel = true;
                pendingShiftPoint = p;
            }
            else if (message == Native.WM_LBUTTONUP)
            {
                leftDown = false;
                shiftArmed = false;
                pendingShiftWheel = false;
                if (wheel != null)
                {
                    oleReleasePoint = p;
                    oleReleaseAt = DateTime.UtcNow;
                    pendingDropResolution = true;
                    return Native.CallNextHookEx(hook, code, wParam, lParam);
                }
            }
            return Native.CallNextHookEx(hook, code, wParam, lParam);
        }

        private static int Distance(Point a, Point b)
        {
            int x = a.X - b.X, y = a.Y - b.Y;
            return (int)Math.Sqrt(x * x + y * y);
        }

        private static List<string> ExplorerSelection(Point point)
        {
            var output = new List<string>();
            IntPtr root = Native.GetAncestor(Native.WindowFromPoint(new Native.POINT(point.X, point.Y)), Native.GA_ROOT);
            IntPtr foreground = Native.GetForegroundWindow();
            try
            {
                Type type = Type.GetTypeFromProgID("Shell.Application");
                if (type == null) return output;
                dynamic shell = Activator.CreateInstance(type);
                dynamic windows = shell.Windows();
                foreach (dynamic window in windows)
                {
                    try
                    {
                        IntPtr hwnd = new IntPtr(Convert.ToInt64(window.HWND));
                        if (hwnd != root && hwnd != foreground) continue;
                        dynamic selected = window.Document.SelectedItems();
                        foreach (dynamic item in selected)
                        {
                            string path = Convert.ToString(item.Path);
                            if (File.Exists(path) || Directory.Exists(path)) output.Add(path);
                        }
                        if (output.Count > 0) break;
                    }
                    catch { }
                }
            }
            catch { }
            return output.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static bool IsExplorerAt(Point point)
        {
            IntPtr root = Native.GetAncestor(Native.WindowFromPoint(new Native.POINT(point.X, point.Y)), Native.GA_ROOT);
            var name = new StringBuilder(100);
            Native.GetClassName(root, name, name.Capacity);
            return name.ToString() == "CabinetWClass" || name.ToString() == "ExploreWClass";
        }

        private static List<MenuAction> ActionsFor(List<string> files)
        {
            string[] images = { ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif", ".tif", ".tiff" };
            string[] videos = { ".mp4", ".mkv", ".mov", ".avi", ".webm" };
            string[] audios = { ".mp3", ".wav", ".m4a", ".flac", ".ogg", ".aac" };
            string[] archives = { ".zip", ".7z", ".tar", ".gz", ".tgz", ".xz" };
            if (files.Count > 1 && files.All(f => images.Contains(Path.GetExtension(f).ToLowerInvariant())))
                return new List<MenuAction> { new MenuAction("to_pdf", "PDF واحد", "One PDF"), new MenuAction("convert", "تحويل", "Convert"), new MenuAction("edit", "تعديل", "Edit"), new MenuAction("archive", "أرشفة", "Archive") };
            if (files.Count > 1 && files.All(f => Path.GetExtension(f).Equals(".pdf", StringComparison.OrdinalIgnoreCase)))
                return new List<MenuAction> { new MenuAction("merge", "دمج PDF", "Merge PDF"), new MenuAction("each", "كل ملف", "Each file"), new MenuAction("archive", "أرشفة", "Archive") };
            if (files.Count > 1)
                return new List<MenuAction> { new MenuAction("archive", "أرشيف واحد", "One archive"), new MenuAction("each", "كل ملف", "Each file") };
            string ext = Path.GetExtension(files[0]).ToLowerInvariant();
            if (images.Contains(ext))
                return new List<MenuAction> { new MenuAction("convert", "تحويل", "Convert"), new MenuAction("to_pdf", "إلى PDF", "To PDF"), new MenuAction("crop", "قص", "Crop"), new MenuAction("edit", "تعديل", "Edit"), new MenuAction("compress", "ضغط", "Compress"), new MenuAction("background", "خلفية", "Background") };
            if (ext == ".pdf")
                return new List<MenuAction> { new MenuAction("pdf_docx", "DOCX", "DOCX"), new MenuAction("pdf_txt", "TXT", "TXT"), new MenuAction("pdf_jpg", "JPG", "JPG"), new MenuAction("pdf_png", "PNG", "PNG"), new MenuAction("compress", "ضغط", "Compress"), new MenuAction("pages", "صفحات", "Pages") };
            if (videos.Contains(ext))
                return new List<MenuAction> { new MenuAction("convert", "تحويل", "Convert"), new MenuAction("compress", "ضغط", "Compress"), new MenuAction("extract_audio", "استخراج صوت", "Extract audio") };
            if (audios.Contains(ext))
                return new List<MenuAction> { new MenuAction("convert", "تحويل", "Convert"), new MenuAction("compress", "ضغط", "Compress") };
            if (archives.Contains(ext))
                return new List<MenuAction> { new MenuAction("extract", "فك", "Extract"), new MenuAction("archive", "إعادة أرشفة", "Repack") };
            if (ext == ".txt" || ext == ".md" || ext == ".docx")
                return new List<MenuAction> { new MenuAction("convert", "تحويل", "Convert"), new MenuAction("archive", "أرشفة", "Archive") };
            return new List<MenuAction> { new MenuAction("archive", "أرشفة", "Archive") };
        }

        private void ShowWheel(List<string> files, Point p)
        {
            if (wheel != null) return;
            pendingDropResolution = false;
            oleDragActive = false;
            activeFiles = files;
            wheel = new Wheel(files.Count > 0 ? ActionsFor(files) : new List<MenuAction> { new MenuAction("drop", "أفلت الملف", "Drop file") }, settings.Language);
            wheel.ActionClicked += delegate(MenuAction action) { CompleteWheel(action); };
            wheel.FilesDragged += delegate(List<string> dragged)
            {
                activeFiles = dragged;
                oleDragActive = true;
                oleReleaseAt = DateTime.UtcNow;
                wheel.SetActions(ActionsFor(dragged));
            };
            wheel.FilesDropped += delegate(Point at) { CompleteWheel(at); };
            PlaceWheel(p);
        }

        private void CompleteWheel(Point p)
        {
            if (wheel == null) return;
            CompleteWheel(wheel.ActionAt(p));
        }

        private void CompleteWheel(MenuAction action)
        {
            if (wheel == null) return;
            wheel.Close();
            wheel = null;
            oleDragActive = false;
            pendingDropResolution = false;
            if (action != null)
            {
                if (action.Id == "choose") ChooseFiles();
                else if (action.Id == "quit") ExitThread();
                else if (activeFiles.Count > 0 && action.Id != "drop") { pendingAction = action.Id; pendingFiles = activeFiles; }
            }
            activeFiles = new List<string>();
        }

        private void ShowLauncher()
        {
            if ((DateTime.UtcNow - lastOutsideClick).TotalMilliseconds < 250) return;
            if (wheel != null) { CompleteWheel((MenuAction)null); return; }
            activeFiles = new List<string>();
            wheel = new Wheel(new List<MenuAction> {
                new MenuAction("choose", "اختيار ملفات", "Choose files"),
                new MenuAction("settings", "الإعدادات", "Settings"),
                new MenuAction("quit", "خروج", "Exit") }, settings.Language);
            wheel.ActionClicked += delegate(MenuAction action) { CompleteWheel(action); };
            PlaceWheel(Cursor.Position);
        }

        private void PlaceWheel(Point p)
        {
            wheel.StartPosition = FormStartPosition.Manual;
            Rectangle area = Screen.FromPoint(p).WorkingArea;
            wheel.Location = new Point(Math.Max(area.Left, Math.Min(area.Right - wheel.Width, p.X - wheel.Width / 2)), Math.Max(area.Top, Math.Min(area.Bottom - wheel.Height, p.Y - wheel.Height / 2)));
            wheel.CaptureBackdrop();
            wheel.Show();
            wheel.UpdateHover(p);
        }

        private void LaunchWorker(string action, List<string> files)
        {
            try
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                string jobDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FileOrbit", "jobs");
                Directory.CreateDirectory(jobDir);
                string jobPath = Path.Combine(jobDir, Guid.NewGuid().ToString("N") + ".json");
                var data = new Dictionary<string, object>();
                data["action"] = action; data["files"] = files; data["settings"] = Settings.PathName;
                File.WriteAllText(jobPath, new JavaScriptSerializer().Serialize(data), Encoding.UTF8);
                string worker = Path.Combine(dir, "FileOrbit.Worker.exe");
                ProcessStartInfo start;
                if (File.Exists(worker)) start = new ProcessStartInfo(worker, "--job " + Quote(jobPath));
                else
                {
                    string python = Environment.GetEnvironmentVariable("FILEORBIT_WORKER");
                    if (String.IsNullOrEmpty(python)) throw new FileNotFoundException("FileOrbit.Worker.exe was not found.");
                    start = new ProcessStartInfo(python, Quote(Path.Combine(dir, "worker.py")) + " --job " + Quote(jobPath));
                }
                start.WorkingDirectory = dir;
                start.UseShellExecute = false;
                Process.Start(start);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "FileOrbit"); }
        }

        private static string Quote(string value) { return "\"" + value.Replace("\"", "\\\"") + "\""; }

        private void ChooseFiles()
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Multiselect = true;
                dialog.Filter = "All files|*.*";
                if (dialog.ShowDialog() == DialogResult.OK) ShowWheel(dialog.FileNames.ToList(), Cursor.Position);
            }
        }

        protected override void ExitThreadCore()
        {
            if (hook != IntPtr.Zero) Native.UnhookWindowsHookEx(hook);
            timer.Stop(); timer.Dispose();
            if (wheel != null) wheel.Close();
            tray.Visible = false; tray.Dispose();
            base.ExitThreadCore();
        }
    }

    class Wheel : Form
    {
        public event Action<MenuAction> ActionClicked;
        public event Action<List<string>> FilesDragged;
        public event Action<Point> FilesDropped;
        private List<MenuAction> actions;
        private readonly string language;
        private float[] hoverBlend;
        private readonly Timer animation;
        private Bitmap backdrop;
        private int hover = -1;
        private const int size = 320, outer = 132, inner = 54;
        public Wheel(List<MenuAction> actions, string language)
        {
            this.actions = actions; this.language = language;
            hoverBlend = new float[actions.Count];
            animation = new Timer { Interval = 16 };
            animation.Tick += delegate
            {
                bool moving = false;
                for (int i = 0; i < hoverBlend.Length; i++)
                {
                    float target = i == hover ? 1f : 0f;
                    hoverBlend[i] += (target - hoverBlend[i]) * .27f;
                    if (Math.Abs(hoverBlend[i] - target) < .015f) hoverBlend[i] = target;
                    else moving = true;
                }
                RedrawLayered();
                if (!moving) animation.Stop();
            };
            Size = new Size(size, size);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            AllowDrop = true;
            Shown += delegate { RedrawLayered(); };
        }
        public void SetActions(List<MenuAction> next)
        {
            if (next == null || next.Count == 0) return;
            actions = next;
            hoverBlend = new float[next.Count];
            hover = -1;
            animation.Stop();
            RedrawLayered();
        }
        protected override void OnDragEnter(DragEventArgs e)
        {
            base.OnDragEnter(e);
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            string[] paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (paths == null) return;
            var files = paths.Where(path => File.Exists(path) || Directory.Exists(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (files.Count == 0) return;
            e.Effect = DragDropEffects.Copy;
            if (FilesDragged != null) FilesDragged(files);
            UpdateHover(new Point(e.X, e.Y));
        }
        protected override void OnDragOver(DragEventArgs e)
        {
            base.OnDragOver(e);
            e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            UpdateHover(new Point(e.X, e.Y));
        }
        protected override void OnDragDrop(DragEventArgs e)
        {
            base.OnDragDrop(e);
            if (FilesDropped != null) FilesDropped(new Point(e.X, e.Y));
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { CreateParams p = base.CreateParams; p.ExStyle |= 0x08000000 | 0x00000080 | 0x00080000; return p; }
        }
        public void UpdateHover(Point screen)
        {
            int next = IndexAt(screen);
            if (next != hover) { hover = next; animation.Start(); }
        }
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            animation.Stop(); animation.Dispose();
            if (backdrop != null) backdrop.Dispose();
            base.OnFormClosed(e);
        }
        public void CaptureBackdrop()
        {
            try
            {
                using (var captured = new Bitmap(size, size, PixelFormat.Format32bppPArgb))
                using (var source = Graphics.FromImage(captured))
                using (var tiny = new Bitmap(size / 15, size / 15, PixelFormat.Format32bppPArgb))
                {
                    source.CopyFromScreen(Location, Point.Empty, Size);
                    using (var reduced = Graphics.FromImage(tiny))
                    {
                        reduced.InterpolationMode = InterpolationMode.HighQualityBilinear;
                        reduced.DrawImage(captured, new Rectangle(0, 0, tiny.Width, tiny.Height));
                    }
                    backdrop = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
                    using (var enlarged = Graphics.FromImage(backdrop))
                    {
                        enlarged.InterpolationMode = InterpolationMode.HighQualityBilinear;
                        enlarged.DrawImage(tiny, new Rectangle(0, 0, size, size));
                    }
                }
            }
            catch { if (backdrop != null) { backdrop.Dispose(); backdrop = null; } }
        }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            MenuAction action = ActionAt(PointToScreen(e.Location));
            if (ActionClicked != null) ActionClicked(action);
        }
        public MenuAction ActionAt(Point screen)
        {
            int index = IndexAt(screen);
            return index >= 0 ? actions[index] : null;
        }
        private int IndexAt(Point screen)
        {
            Point p = PointToClient(screen);
            double x = p.X - size / 2, y = p.Y - size / 2;
            double radius = DistanceFromCenter(p);
            if (radius < inner || radius > outer + 15) return -1;
            double angle = (Math.Atan2(y, x) * 180 / Math.PI + 450 + 180.0 / actions.Count) % 360;
            int index = Math.Min(actions.Count - 1, (int)(angle / (360.0 / actions.Count)));
            float sweep = 360f / actions.Count;
            using (var path = SegmentPath(outer + 10 * hoverBlend[index], -90 - sweep / 2 + index * sweep, sweep))
                return path.IsVisible(p) ? index : -1;
        }
        private static double DistanceFromCenter(Point p)
        {
            double x = p.X - size / 2, y = p.Y - size / 2;
            return Math.Sqrt(x * x + y * y);
        }
        private void RedrawLayered()
        {
            if (!IsHandleCreated) return;
            using (var bitmap = new Bitmap(size, size, PixelFormat.Format32bppPArgb))
            {
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.Clear(Color.Transparent);
                    DrawWheel(g);
                }
                IntPtr screen = Native.GetDC(IntPtr.Zero);
                IntPtr memory = Native.CreateCompatibleDC(screen);
                IntPtr handle = bitmap.GetHbitmap(Color.FromArgb(0));
                IntPtr old = Native.SelectObject(memory, handle);
                try
                {
                    var dst = new Native.POINT(Left, Top);
                    var src = new Native.POINT(0, 0);
                    var bounds = new Native.SIZE(size, size);
                    var blend = new Native.BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
                    Native.UpdateLayeredWindow(Handle, screen, ref dst, ref bounds, memory, ref src, 0, ref blend, 2);
                }
                finally
                {
                    Native.SelectObject(memory, old);
                    Native.DeleteObject(handle);
                    Native.DeleteDC(memory);
                    Native.ReleaseDC(IntPtr.Zero, screen);
                }
            }
        }
        private void DrawWheel(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            // One frame around the cards keeps the wheel from looking like stacked circles.
            using (var frame = new Pen(Color.FromArgb(150, 255, 250, 239), 1.6f))
                g.DrawEllipse(frame, size / 2 - outer - 5, size / 2 - outer - 5, (outer + 5) * 2, (outer + 5) * 2);
            float sweep = 360f / actions.Count;
            for (int i = 0; i < actions.Count; i++)
            {
                float blend = hoverBlend[i];
                float r = outer + 10 * blend;
                float start = -90 - sweep / 2 + i * sweep;
                using (GraphicsPath path = SegmentPath(r, start, sweep))
                {
                    DrawFrosted(g, path);
                    using (var brush = new LinearGradientBrush(new PointF(0, size / 2 - r), new PointF(0, size / 2 + r),
                        BlendColor(Color.FromArgb(96, 250, 253, 255), Color.FromArgb(151, 255, 255, 255), blend),
                        BlendColor(Color.FromArgb(55, 169, 194, 217), Color.FromArgb(111, 220, 242, 255), blend)))
                        g.FillPath(brush, path);
                    using (var pen = new Pen(BlendColor(Color.FromArgb(158, 244, 251, 255), Color.FromArgb(239, 255, 255, 255), blend), 2f + 1.3f * blend))
                        g.DrawPath(pen, path);
                }
                double mid = (start + sweep / 2) * Math.PI / 180;
                float centerRadius = (48 + r) / 2 + 2;
                float tx = size / 2 + (float)Math.Cos(mid) * centerRadius;
                float ty = size / 2 + (float)Math.Sin(mid) * centerRadius;
                using (Font font = new Font("Segoe UI", 9f + 1.1f * blend, FontStyle.Bold))
                using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    using (var shadow = new SolidBrush(Color.FromArgb(173, 24, 37, 51)))
                        g.DrawString(actions[i].Label(language), font, shadow, new RectangleF(tx - 47, ty - 20, 96, 42), format);
                    using (var brush = new SolidBrush(Color.White))
                        g.DrawString(actions[i].Label(language), font, brush, new RectangleF(tx - 48, ty - 21, 96, 42), format);
                }
            }
        }
        private void DrawFrosted(Graphics g, GraphicsPath shape)
        {
            if (backdrop == null) return;
            GraphicsState saved = g.Save();
            g.SetClip(shape);
            using (var attributes = new ImageAttributes())
            {
                var matrix = new ColorMatrix();
                matrix.Matrix33 = .88f;
                attributes.SetColorMatrix(matrix);
                g.DrawImage(backdrop, new Rectangle(0, 0, size, size), 0, 0, size, size, GraphicsUnit.Pixel, attributes);
            }
            g.Restore(saved);
        }
        private static PointF Polar(float radius, float angle)
        {
            double radians = angle * Math.PI / 180;
            return new PointF(size / 2f + radius * (float)Math.Cos(radians), size / 2f + radius * (float)Math.Sin(radians));
        }
        private static GraphicsPath SegmentPath(float radius, float start, float sweep)
        {
            const float gap = 3f, cornerAngle = 3f, cornerDepth = 11f;
            float a = start + gap, b = start + sweep - gap;
            var path = new GraphicsPath();
            path.AddArc(size / 2 - radius, size / 2 - radius, radius * 2, radius * 2, a + cornerAngle, b - a - 2 * cornerAngle);
            path.AddBezier(Polar(radius, b - cornerAngle), Polar(radius, b), Polar(radius - cornerDepth / 2, b), Polar(radius - cornerDepth, b));
            path.AddLine(Polar(radius - cornerDepth, b), Polar(inner + cornerDepth, b));
            path.AddBezier(Polar(inner + cornerDepth, b), Polar(inner + cornerDepth / 2, b), Polar(inner, b), Polar(inner, b - cornerAngle));
            path.AddArc(size / 2 - inner, size / 2 - inner, inner * 2, inner * 2, b - cornerAngle, -(b - a - 2 * cornerAngle));
            path.AddBezier(Polar(inner, a + cornerAngle), Polar(inner, a), Polar(inner + cornerDepth / 2, a), Polar(inner + cornerDepth, a));
            path.AddLine(Polar(inner + cornerDepth, a), Polar(radius - cornerDepth, a));
            path.AddBezier(Polar(radius - cornerDepth, a), Polar(radius - cornerDepth / 2, a), Polar(radius, a), Polar(radius, a + cornerAngle));
            path.CloseFigure();
            return path;
        }
        private static Color BlendColor(Color a, Color b, float t)
        {
            return Color.FromArgb((int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }
    }
}
