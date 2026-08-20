using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Diagnostics;

namespace Notify
{
    public class NotifyForm : Form
    {
        private readonly string messageText;
        private readonly string appNameText;
        private readonly float durationSeconds;
        private readonly Color progressColor;

        private readonly Timer moveTimer;
        private readonly Timer progressTimer;

        private readonly Stopwatch moveWatch = new Stopwatch();
        private readonly Stopwatch progressWatch = new Stopwatch();

        private Point targetLocation;
        private Point moveStartPos;

        private float progress = 1f;
        private bool closing;
        private int closingY;
        private int closeTargetX;

        private int lastFilled = -1;

        private Bitmap cachedBackground;
        private Bitmap workingBitmap;
        private Rectangle barRect;

        private const int PanelWidth = 340;
        private const int PanelHeight = 70;

        private const int TimerInterval = 16;
        private const float MoveDuration = 300f;

        private const int LeftMargin = 12;
        private const int TopMarginTitle = 8;
        private const int SpacingBetween = 4;

        private static PrivateFontCollection regularFontCollection;
        private static PrivateFontCollection semiBoldFontCollection;
        private static Font titleFont;
        private static Font descFont;
        private static bool fontsLoaded;
        private static readonly object fontLock = new object();

        [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern bool UpdateLayeredWindow(
            IntPtr hwnd, IntPtr hdcDst,
            ref POINT pptDst, ref SIZE psize,
            IntPtr hdcSrc, ref POINT pptSrc,
            uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);

        [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll", ExactSpelling = true)]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll", ExactSpelling = true)]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll", ExactSpelling = true)]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll", ExactSpelling = true)]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll", ExactSpelling = true)]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(
            IntPtr hWnd, IntPtr hWndInsertAfter,
            int x, int y, int cx, int cy, uint uFlags);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE { public int cx; public int cy; }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        private const byte AC_SRC_OVER = 0x00;
        private const byte AC_SRC_ALPHA = 0x01;
        private const uint ULW_ALPHA = 0x02;

        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_NOSENDCHANGING = 0x0400;

        public NotifyForm(string message, string appName, float duration, Color progressColor)
        {
            messageText = message ?? "";
            appNameText = appName ?? "Notify";
            durationSeconds = duration;
            this.progressColor = progressColor;

            AutoScaleMode = AutoScaleMode.None;

            LoadFonts();

            Size = new Size(PanelWidth, PanelHeight);
            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            BackColor = Color.FromArgb(11, 11, 11);

            DoubleBuffered = false;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Opaque, true);
            UpdateStyles();

            Click += (s, e) => StartClosing();

            BuildBitmaps();

            moveTimer = new Timer { Interval = TimerInterval };
            moveTimer.Tick += MoveTimerTick;

            progressTimer = new Timer { Interval = TimerInterval };
            progressTimer.Tick += ProgressTimerTick;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x00080000 | 0x00000080 | 0x08000000 | 0x00000020;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation => true;

        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }

        private static void LoadFonts()
        {
            if (fontsLoaded) return;
            lock (fontLock)
            {
                if (fontsLoaded) return;
                try
                {
                    regularFontCollection = new PrivateFontCollection();
                    semiBoldFontCollection = new PrivateFontCollection();

                    byte[] regularData = Properties.Resources.Poppins_Regular;
                    byte[] semiBoldData = Properties.Resources.Poppins_SemiBold;

                    if (regularData != null && regularData.Length > 0 &&
                        semiBoldData != null && semiBoldData.Length > 0)
                    {
                        IntPtr regularPtr = Marshal.AllocCoTaskMem(regularData.Length);
                        Marshal.Copy(regularData, 0, regularPtr, regularData.Length);
                        regularFontCollection.AddMemoryFont(regularPtr, regularData.Length);
                        Marshal.FreeCoTaskMem(regularPtr);

                        IntPtr semiPtr = Marshal.AllocCoTaskMem(semiBoldData.Length);
                        Marshal.Copy(semiBoldData, 0, semiPtr, semiBoldData.Length);
                        semiBoldFontCollection.AddMemoryFont(semiPtr, semiBoldData.Length);
                        Marshal.FreeCoTaskMem(semiPtr);

                        descFont = new Font(regularFontCollection.Families[0], 9f, FontStyle.Regular);
                        titleFont = new Font(semiBoldFontCollection.Families[0], 10f, FontStyle.Regular);
                    }
                    else
                    {
                        titleFont = new Font("Segoe UI", 10f, FontStyle.Bold);
                        descFont = new Font("Segoe UI", 9f, FontStyle.Regular);
                    }
                }
                catch
                {
                    titleFont = new Font("Segoe UI", 10f, FontStyle.Bold);
                    descFont = new Font("Segoe UI", 9f, FontStyle.Regular);
                }
                fontsLoaded = true;
            }
        }

        private void BuildBitmaps()
        {
            cachedBackground?.Dispose();
            workingBitmap?.Dispose();

            cachedBackground = new Bitmap(PanelWidth, PanelHeight, PixelFormat.Format32bppPArgb);

            using (var g = Graphics.FromImage(cachedBackground))
            {
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.Clear(Color.FromArgb(11, 11, 11));

                SizeF titleSize = g.MeasureString(messageText, titleFont, PanelWidth - 2 * LeftMargin);
                float titleHeight = titleSize.Height;

                if (!string.IsNullOrEmpty(messageText))
                {
                    using (var titleBrush = new SolidBrush(Color.White))
                    using (var sf = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Near })
                    {
                        var rect = new RectangleF(LeftMargin, TopMarginTitle, PanelWidth - 2 * LeftMargin, titleHeight);
                        g.DrawString(messageText, titleFont, titleBrush, rect, sf);
                    }
                }

                using (var descBrush = new SolidBrush(Color.FromArgb(180, 180, 180)))
                using (var sf = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Near })
                {
                    float descY = TopMarginTitle + titleHeight + SpacingBetween;
                    var rect = new RectangleF(LeftMargin, descY, PanelWidth - 2 * LeftMargin, PanelHeight - descY - 10);
                    g.DrawString(appNameText, descFont, descBrush, rect, sf);
                }

                int barHeight = 3;
                int margin = 5;
                int barWidth = PanelWidth - 2 * margin;
                int barY = PanelHeight - barHeight - margin;
                barRect = new Rectangle(margin, barY, barWidth, barHeight);

                using (var bgBrush = new SolidBrush(Color.FromArgb(40, 40, 40)))
                    g.FillRectangle(bgBrush, barRect);
            }

            workingBitmap = (Bitmap)cachedBackground.Clone();
            lastFilled = barRect.Width;
        }

        private void UpdateProgressInBitmap(int filled)
        {
            using (var g = Graphics.FromImage(workingBitmap))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;

                g.DrawImage(cachedBackground, 0, 0, PanelWidth, PanelHeight);

                if (filled > 0)
                {
                    using (var brush = new SolidBrush(progressColor))
                        g.FillRectangle(brush, barRect.X, barRect.Y, filled, barRect.Height);
                }
            }

            PushToScreen();
        }

        private void PushToScreen()
        {
            if (!IsHandleCreated || workingBitmap == null) return;

            var hDCScreen = GetDC(IntPtr.Zero);
            var hDCMemory = CreateCompatibleDC(hDCScreen);

            var hBitmap = workingBitmap.GetHbitmap();
            var hOld = SelectObject(hDCMemory, hBitmap);

            try
            {
                var pos = new POINT { X = Left, Y = Top };
                var size = new SIZE { cx = PanelWidth, cy = PanelHeight };
                var src = new POINT { X = 0, Y = 0 };
                var blend = new BLENDFUNCTION
                {
                    BlendOp = AC_SRC_OVER,
                    BlendFlags = 0,
                    SourceConstantAlpha = 255,
                    AlphaFormat = AC_SRC_ALPHA
                };

                UpdateLayeredWindow(Handle, hDCScreen, ref pos, ref size,
                                    hDCMemory, ref src, 0, ref blend, ULW_ALPHA);
            }
            finally
            {
                SelectObject(hDCMemory, hOld);
                DeleteObject(hBitmap);
                DeleteDC(hDCMemory);
                ReleaseDC(IntPtr.Zero, hDCScreen);
            }
        }

        public void SetTarget(Point target)
        {
            if (closing || IsDisposed) return;
            if (targetLocation == target) return;

            targetLocation = target;

            if (Visible)
            {
                moveStartPos = Location;
                moveWatch.Restart();

                if (!moveTimer.Enabled)
                    moveTimer.Start();
            }
        }

        public void ShowAnimated(Point start, Point target)
        {
            if (IsDisposed) return;

            closing = false;
            progress = 1f;

            moveStartPos = start;
            targetLocation = target;

            Location = start;

            UpdateProgressInBitmap(barRect.Width);

            Show();

            if (IsHandleCreated)
            {
                SetWindowPos(Handle, IntPtr.Zero, start.X, start.Y,
                             PanelWidth, PanelHeight,
                             SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOSENDCHANGING);
            }

            progressWatch.Restart();
            moveWatch.Restart();

            progressTimer.Start();
            moveTimer.Start();
        }

        private void MoveTimerTick(object sender, EventArgs e)
        {
            if (IsDisposed) return;

            if (!moveWatch.IsRunning)
                moveWatch.Restart();

            float elapsed = (float)moveWatch.ElapsedMilliseconds;

            if (closing)
            {
                float t = Math.Min(elapsed / MoveDuration, 1f);
                float eased = EaseOutCubic(t);

                int x = (int)Lerp(moveStartPos.X, closeTargetX, eased);
                int y = closingY;

                SetWindowPos(Handle, IntPtr.Zero, x, y, PanelWidth, PanelHeight,
                             SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOSENDCHANGING);

                if (t >= 1f)
                {
                    moveTimer.Stop();
                    Close();
                }
                return;
            }

            float moveProgress = Math.Min(elapsed / MoveDuration, 1f);
            float easedMove = EaseOutCubic(moveProgress);

            int newX = (int)Lerp(moveStartPos.X, targetLocation.X, easedMove);
            int newY = (int)Lerp(moveStartPos.Y, targetLocation.Y, easedMove);

            SetWindowPos(Handle, IntPtr.Zero, newX, newY, PanelWidth, PanelHeight,
                         SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOSENDCHANGING);

            if (moveProgress >= 1f)
            {
                SetWindowPos(Handle, IntPtr.Zero, targetLocation.X, targetLocation.Y,
                             PanelWidth, PanelHeight,
                             SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOSENDCHANGING);
                moveTimer.Stop();
            }
        }

        private void ProgressTimerTick(object sender, EventArgs e)
        {
            if (IsDisposed) return;

            if (!progressWatch.IsRunning)
                progressWatch.Restart();

            float totalMs = durationSeconds * 1000f;
            float elapsed = (float)progressWatch.ElapsedMilliseconds;

            progress = 1f - (elapsed / totalMs);
            if (progress < 0f) progress = 0f;

            int filled = (int)(barRect.Width * progress);
            if (filled < 0) filled = 0;
            if (filled > barRect.Width) filled = barRect.Width;

            if (filled != lastFilled)
            {
                lastFilled = filled;
                UpdateProgressInBitmap(filled);
            }

            if (progress <= 0f)
            {
                progressTimer.Stop();
                StartClosing();
            }
        }

        public void StartClosing()
        {
            if (closing || IsDisposed) return;

            closing = true;
            progressTimer.Stop();

            moveStartPos = Location;
            closingY = Location.Y;
            closeTargetX = Screen.PrimaryScreen.WorkingArea.Right + PanelWidth;

            moveWatch.Restart();

            if (!moveTimer.Enabled)
                moveTimer.Start();
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;
        private static float EaseOutCubic(float t) => 1f - (float)Math.Pow(1f - t, 3);

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            moveTimer?.Stop();
            moveTimer?.Dispose();

            progressTimer?.Stop();
            progressTimer?.Dispose();

            cachedBackground?.Dispose();
            workingBitmap?.Dispose();

            base.OnFormClosed(e);
        }
    }

    public enum NotifyState
    {
        Success,
        Warning,
        Error,
        Neutral
    }

    public static class NotifyColors
    {
        public static Color Success => Color.FromArgb(5, 134, 105);
        public static Color Warning => Color.FromArgb(236, 129, 44);
        public static Color Error => Color.FromArgb(250, 50, 56);

        public static Color Neutral => Color.FromArgb(209, 209, 209);
    }
}