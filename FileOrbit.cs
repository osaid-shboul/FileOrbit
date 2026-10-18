// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
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
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)] public static extern IntPtr GetModuleHandle(string moduleName);
    }

    class Settings
    {
        public bool RightHold = true, ShiftDrag = true;
        public int HoldMs = 500;
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
                    if (key == "right_hold") s.RightHold = value == "1";
                    if (key == "shift_drag") s.ShiftDrag = value == "1";
                    if (key == "language") s.Language = value == "en" ? "en" : "ar";
                    if (key == "hold_ms") { int n; if (int.TryParse(value, out n)) s.HoldMs = Math.Max(250, Math.Min(1200, n)); }
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
        private bool rightDown, leftDown, wheelFromRight;
        private bool pendingShiftWheel;
        private Point pendingShiftPoint;
        private string pendingAction;
        private List<string> pendingFiles;
        private Point downPoint;
        private DateTime downAt;
        private DateTime lastSettingsRead = DateTime.MinValue;
        private readonly string[] startupFiles;

        public TrayApp(string[] args)
        {
            startupFiles = args.Where(File.Exists).ToArray();
            settings = Settings.Load();
            var menu = new ContextMenuStrip();
            menu.Items.Add("اختيار ملفات / Choose files", null, delegate { ChooseFiles(); });
            menu.Items.Add("الإعدادات / Settings", null, delegate { LaunchWorker("settings", new List<string>()); });
            menu.Items.Add("خروج / Exit", null, delegate { ExitThread(); });
            tray = new NotifyIcon();
            tray.Icon = CreateIcon();
            tray.Text = "FileOrbit";
            tray.ContextMenuStrip = menu;
            tray.Visible = true;
            tray.DoubleClick += delegate { ChooseFiles(); };
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
            ShowWheel(startupFiles.ToList(), Cursor.Position, false);
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
            if (rightDown && wheel == null && settings.RightHold &&
                (DateTime.UtcNow - downAt).TotalMilliseconds >= settings.HoldMs &&
                Distance(downPoint, Cursor.Position) < 12)
            {
                List<string> files = ExplorerSelection(downPoint);
                if (files.Count > 0) ShowWheel(files, downPoint, true);
                else rightDown = false;
            }
            if (pendingShiftWheel && leftDown && wheel == null)
            {
                pendingShiftWheel = false;
                List<string> files = ExplorerSelection(downPoint);
                if (files.Count > 0) ShowWheel(files, pendingShiftPoint, false);
            }
            if (wheel != null) wheel.UpdateHover(Cursor.Position);
        }

        private IntPtr MouseCallback(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code < 0) return Native.CallNextHookEx(hook, code, wParam, lParam);
            var info = (Native.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.MSLLHOOKSTRUCT));
            Point p = new Point(info.pt.X, info.pt.Y);
            int message = wParam.ToInt32();
            if (message == Native.WM_RBUTTONDOWN && wheel == null)
            {
                rightDown = true; downPoint = p; downAt = DateTime.UtcNow;
            }
            else if (message == Native.WM_LBUTTONDOWN && wheel == null && settings.ShiftDrag && (Native.GetAsyncKeyState(0x10) & 0x8000) != 0)
            {
                leftDown = true; downPoint = p;
            }
            else if (message == Native.WM_MOUSEMOVE && leftDown && wheel == null && Distance(downPoint, p) > 30)
            {
                pendingShiftWheel = true;
                pendingShiftPoint = p;
            }
            else if (message == Native.WM_RBUTTONUP)
            {
                rightDown = false;
                if (wheel != null && wheelFromRight)
                {
                    CompleteWheel(p);
                    return (IntPtr)1;
                }
            }
            else if (message == Native.WM_LBUTTONUP)
            {
                leftDown = false;
                if (wheel != null && !wheelFromRight)
                {
                    CompleteWheel(p);
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
                return new List<MenuAction> { new MenuAction("convert", "تحويل", "Convert"), new MenuAction("ocr", "النص", "Text / OCR"), new MenuAction("compress", "ضغط", "Compress"), new MenuAction("pages", "الصفحات", "Pages") };
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

        private void ShowWheel(List<string> files, Point p, bool fromRight)
        {
            if (wheel != null || files.Count == 0) return;
            activeFiles = files;
            wheelFromRight = fromRight;
            wheel = new Wheel(ActionsFor(files), settings.Language);
            wheel.StartPosition = FormStartPosition.Manual;
            Rectangle area = Screen.FromPoint(p).WorkingArea;
            wheel.Location = new Point(Math.Max(area.Left, Math.Min(area.Right - wheel.Width, p.X - wheel.Width / 2)), Math.Max(area.Top, Math.Min(area.Bottom - wheel.Height, p.Y - wheel.Height / 2)));
            wheel.Show();
            wheel.UpdateHover(p);
        }

        private void CompleteWheel(Point p)
        {
            if (wheel == null) return;
            MenuAction action = wheel.ActionAt(p);
            wheel.Close();
            wheel = null;
            if (action != null) { pendingAction = action.Id; pendingFiles = activeFiles; }
            activeFiles = new List<string>();
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
                if (dialog.ShowDialog() == DialogResult.OK) ShowWheel(dialog.FileNames.ToList(), Cursor.Position, false);
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
        private readonly List<MenuAction> actions;
        private readonly string language;
        private readonly float[] hoverBlend;
        private readonly Timer animation;
        private int hover = -1;
        private const int size = 440, outer = 185, inner = 67;
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
                Invalidate();
                if (!moving) animation.Stop();
            };
            Size = new Size(size, size);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;
            DoubleBuffered = true;
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { CreateParams p = base.CreateParams; p.ExStyle |= 0x08000000 | 0x00000080; return p; }
        }
        public void UpdateHover(Point screen)
        {
            int next = IndexAt(screen);
            if (next != hover) { hover = next; animation.Start(); }
        }
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            animation.Stop(); animation.Dispose();
            base.OnFormClosed(e);
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
            double radius = Math.Sqrt(x * x + y * y);
            if (radius < inner || radius > outer + 15) return -1;
            double angle = (Math.Atan2(y, x) * 180 / Math.PI + 450) % 360;
            return Math.Min(actions.Count - 1, (int)(angle / (360.0 / actions.Count)));
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            float sweep = 360f / actions.Count;
            for (int i = 0; i < actions.Count; i++)
            {
                float blend = hoverBlend[i];
                bool active = blend > .5f;
                float r = outer + 9 * blend;
                float start = -90 + i * sweep;
                using (GraphicsPath path = new GraphicsPath())
                {
                    path.AddArc(size / 2 - r, size / 2 - r, r * 2, r * 2, start + 2, sweep - 4);
                    path.AddArc(size / 2 - inner, size / 2 - inner, inner * 2, inner * 2, start + sweep - 2, -sweep + 4);
                    path.CloseFigure();
                    using (var brush = new SolidBrush(active ? Color.FromArgb(245, 122, 61) : Color.FromArgb(255, 231, 192))) g.FillPath(brush, path);
                    using (var pen = new Pen(Color.FromArgb(201, 146, 99), 1.5f + 1.5f * blend)) g.DrawPath(pen, path);
                }
                double mid = (start + sweep / 2) * Math.PI / 180;
                float centerRadius = (inner + r) / 2 + 2;
                float tx = size / 2 + (float)Math.Cos(mid) * centerRadius;
                float ty = size / 2 + (float)Math.Sin(mid) * centerRadius;
                using (Font font = new Font("Segoe UI", 10f + 1.5f * blend, FontStyle.Bold))
                using (var brush = new SolidBrush(active ? Color.White : Color.FromArgb(66, 48, 38)))
                using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(actions[i].Label(language), font, brush, new RectangleF(tx - 62, ty - 26, 124, 52), format);
            }
            using (var brush = new SolidBrush(Color.FromArgb(96, 62, 47))) g.FillEllipse(brush, size / 2 - inner + 5, size / 2 - inner + 5, (inner - 5) * 2, (inner - 5) * 2);
            using (var font = new Font("Segoe UI", 12f, FontStyle.Bold))
            using (var brush = new SolidBrush(Color.White))
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString("FileOrbit", font, brush, new RectangleF(size / 2 - 58, size / 2 - 30, 116, 60), format);
        }
    }
}
