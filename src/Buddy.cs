// ============================================================================
//  Buddy.cs - the desktop buddy for Spidey Companion (v1.3).
//
//  A chibi Spider-Man who lives on the real Windows desktop: he walks
//  along the bottom of the screen (the taskbar edge), pauses (sometimes
//  sitting down or lying flat), does web-swing jumps, climbs the screen's
//  side walls, and can be grabbed and dropped. He hides himself while a
//  fullscreen app (game, video) owns the screen, and clicks pass through
//  the transparent parts of his pose.
//
//  THE ART (v1.3): the 46 poses of a "Shimeji" Spider-Man desktop-pet
//  pack (shime1.png - shime46.png, 128x128, embedded in this exe). The
//  pose-to-behaviour mapping follows the pack's own action definitions
//  (walk = 1-2-1-3, wall climb = 14-12-13 cycle, jump = 22, fall = 4,
//  dragged = 9, trip/land = 18-19, sits = 11/26/30, lie = 21). The
//  Sprite art by haydentheartist (Reddit); used here for a free,
//  non-commercial fan project. (an earlier unreleased build used pixel data from "Buddy" by
//  Rahaman Bin Ujit, MIT - that art was dropped before release at the owner's
//  request; the extraction is kept in the project history.)
//
//  Behaviour timing still mirrors the original Buddy where it applies:
//  walk 60 px/s, pauses 4-10 s (2-5 s after a fall), first jump after
//  15-30 s then every 30-60 s of walking, falling gravity 2800 px/s^2
//  with a bounce at 0.4 damping when impact speed > 80 px/s.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SpideyCompanion
{
    // ------------------------------------------------------------------
    //  Settings: %AppData%\SpideyCompanion\settings.txt, key=value lines.
    // ------------------------------------------------------------------
    internal static class BuddySettings
    {
        public static bool BuddyVisible = false;   // off by default (v1.3.1): fresh installs start service-only, buddy enabled from the tray
        public static int SizePx = 128;       // the poses' native size
        public static double Pace = 1.0;      // 0.7 chill / 1.0 normal / 1.5 zippy
        public static int Monitor = 0;
        public static bool WallpaperOnly = false;  // stay behind other windows, on the desktop layer
        public static int Swing = 2;          // jump profile: 0 small / 1 classic / 2 big

        private static string FilePath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "SpideyCompanion");
                try { Directory.CreateDirectory(dir); } catch { }
                return Path.Combine(dir, "settings.txt");
            }
        }

        public static void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    string[] kv = line.Split(new[] { '=' }, 2);
                    if (kv.Length != 2) continue;
                    string k = kv[0].Trim(), v = kv[1].Trim();
                    if (k == "buddy") BuddyVisible = v != "0";
                    else if (k == "wallonly") WallpaperOnly = v != "0";
                    else if (k == "swing") { int n; if (int.TryParse(v, out n) && n >= 0 && n <= 2) Swing = n; }
                    else if (k == "size") { int n; if (int.TryParse(v, out n) && (n == 96 || n == 128 || n == 160)) SizePx = n; }
                    else if (k == "pace") { double d; if (double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d) && d > 0.2 && d < 3.0) Pace = d; }
                    else if (k == "monitor") { int n; if (int.TryParse(v, out n) && n >= 0 && n < 16) Monitor = n; }
                }
            }
            catch { }
        }

        public static void Save()
        {
            try
            {
                File.WriteAllLines(FilePath, new[]
                {
                    "buddy=" + (BuddyVisible ? "1" : "0"),
                    "wallonly=" + (WallpaperOnly ? "1" : "0"),
                    "swing=" + Swing,
                    "size=" + SizePx,
                    "pace=" + Pace.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "monitor=" + Monitor
                });
            }
            catch { }
        }
    }

    // ------------------------------------------------------------------
    //  Pose rendering: the pack's 128x128 PNGs, embedded as resources.
    //  Frames are pre-scaled (smooth scaling - this is illustration,
    //  not pixel art) and pre-flipped per size; an alpha mask of the
    //  native pose drives per-pixel click-through.
    // ------------------------------------------------------------------
    internal static class BuddyRenderer
    {
        public const int NativeSize = 128;

        private static readonly Dictionary<int, Bitmap> Sources = new Dictionary<int, Bitmap>();
        private static readonly Dictionary<int, bool[,]> Masks = new Dictionary<int, bool[,]>();
        private static readonly Dictionary<string, Bitmap> Cache = new Dictionary<string, Bitmap>();

        private static Bitmap LoadSource(int pose)
        {
            Bitmap bmp;
            if (Sources.TryGetValue(pose, out bmp)) return bmp;
            bmp = null;
            try
            {
                var asm = typeof(BuddyRenderer).Assembly;
                string suffix = "shime" + pose + ".png";
                foreach (string name in asm.GetManifestResourceNames())
                {
                    if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    {
                        using (Stream s = asm.GetManifestResourceStream(name))
                        using (var tmp = new Bitmap(s))
                            bmp = new Bitmap(tmp, NativeSize, NativeSize);
                        break;
                    }
                }
            }
            catch { }
            if (bmp == null)
            {
                // Never crash over a missing pose: draw a stand-in.
                bmp = new Bitmap(NativeSize, NativeSize);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    using (Brush b = new SolidBrush(Color.FromArgb(200, 30, 30)))
                        g.FillEllipse(b, 34, 14, 60, 60);
                    using (Brush b = new SolidBrush(Color.FromArgb(30, 60, 200)))
                        g.FillRectangle(b, 44, 74, 40, 54);
                }
            }
            Sources[pose] = bmp;
            return bmp;
        }

        public static bool[,] Mask(int pose)
        {
            bool[,] mask;
            if (Masks.TryGetValue(pose, out mask)) return mask;
            Bitmap src = LoadSource(pose);
            mask = new bool[NativeSize, NativeSize];
            for (int y = 0; y < NativeSize; y++)
                for (int x = 0; x < NativeSize; x++)
                    mask[x, y] = src.GetPixel(x, y).A > 24;
            Masks[pose] = mask;
            return mask;
        }

        public static Bitmap GetFrame(int pose, int size, bool flipped)
        {
            string key = pose + "|" + size + "|" + (flipped ? "L" : "R");
            Bitmap bmp;
            if (Cache.TryGetValue(key, out bmp)) return bmp;
            Bitmap src = LoadSource(pose);
            bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                if (flipped)
                    g.DrawImage(src, new Rectangle(size, 0, -size, size), new Rectangle(0, 0, NativeSize, NativeSize), GraphicsUnit.Pixel);
                else
                    g.DrawImage(src, new Rectangle(0, 0, size, size), new Rectangle(0, 0, NativeSize, NativeSize), GraphicsUnit.Pixel);
            }
            Cache[key] = bmp;
            return bmp;
        }

        // Click-through test in window pixel coordinates, against the
        // CURRENT pose's alpha (poses differ a lot in silhouette).
        public static bool IsOpaqueAt(int pose, int size, bool flipped, int px, int py)
        {
            if (px < 0 || py < 0 || px >= size || py >= size) return false;
            int sx = px * NativeSize / size;
            int sy = py * NativeSize / size;
            if (flipped) sx = NativeSize - 1 - sx;
            return Mask(pose)[sx, sy];
        }

        public static Bitmap IconBitmap()
        {
            return GetFrame(1, 32, false);
        }
    }

    // ------------------------------------------------------------------
    //  Layered-window plumbing shared by the buddy and the web line.
    // ------------------------------------------------------------------
    internal abstract class LayeredForm : Form
    {
        protected const int WS_EX_LAYERED = 0x80000;
        protected const int WS_EX_TOOLWINDOW = 0x80;
        protected const int ULW_ALPHA = 0x2;
        protected const byte AC_SRC_OVER = 0;
        protected const byte AC_SRC_ALPHA = 1;

        [StructLayout(LayoutKind.Sequential)]
        protected struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }
        [StructLayout(LayoutKind.Sequential)]
        protected struct SIZE { public int CX, CY; public SIZE(int cx, int cy) { CX = cx; CY = cy; } }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        protected struct BLENDFUNCTION
        {
            public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat;
        }

        [DllImport("user32.dll", SetLastError = true)]
        protected static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst,
            ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc,
            int crKey, ref BLENDFUNCTION pblend, int dwFlags);
        [DllImport("user32.dll")]
        protected static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")]
        protected static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")]
        protected static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")]
        protected static extern IntPtr CreateCompatibleDC(IntPtr hDC);
        [DllImport("gdi32.dll")]
        protected static extern bool DeleteDC(IntPtr hDC);
        [DllImport("gdi32.dll")]
        protected static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);
        [DllImport("gdi32.dll")]
        protected static extern bool DeleteObject(IntPtr hObject);

        protected LayeredForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        // Paint `bitmap` at screen position (x, y) with per-pixel alpha.
        protected void PaintLayered(Bitmap bitmap, int x, int y)
        {
            if (bitmap == null || !IsHandleCreated) return;
            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memDc = CreateCompatibleDC(screenDc);
            IntPtr hBmp = IntPtr.Zero;
            IntPtr old = IntPtr.Zero;
            try
            {
                hBmp = bitmap.GetHbitmap(Color.FromArgb(0));
                old = SelectObject(memDc, hBmp);
                var dst = new POINT(x, y);
                var size = new SIZE(bitmap.Width, bitmap.Height);
                var src = new POINT(0, 0);
                var blend = new BLENDFUNCTION
                {
                    BlendOp = AC_SRC_OVER,
                    BlendFlags = 0,
                    SourceConstantAlpha = 255,
                    AlphaFormat = AC_SRC_ALPHA
                };
                UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, ULW_ALPHA);
            }
            catch { }
            finally
            {
                if (old != IntPtr.Zero) SelectObject(memDc, old);
                if (hBmp != IntPtr.Zero) DeleteObject(hBmp);
                DeleteDC(memDc);
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }
    }

    // ------------------------------------------------------------------
    //  The web line shown during jumps (a separate thin layered window).
    // ------------------------------------------------------------------
    internal sealed class WebLineForm : LayeredForm
    {
        private Bitmap _bmp;
        private int _height = -1;

        public void ShowLine(int centerX, int topY, int bottomY)
        {
            int h = bottomY - topY;
            if (h <= 4) { Hide(); return; }
            if (_bmp == null || h != _height)
            {
                if (_bmp != null) _bmp.Dispose();
                _height = h;
                _bmp = new Bitmap(12, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(_bmp))
                {
                    g.Clear(Color.Transparent);
                    using (var pen = new Pen(Color.FromArgb(235, 245, 255), 2f))
                        g.DrawLine(pen, 6, 0, 6, h);
                    using (var pen2 = new Pen(Color.FromArgb(140, 180, 200), 1f))
                        g.DrawLine(pen2, 8, 0, 8, h);
                    using (Brush b = new SolidBrush(Color.FromArgb(235, 245, 255)))
                        g.FillEllipse(b, 3, 0, 6, 6);
                }
            }
            if (!Visible) Show();
            PaintLayered(_bmp, centerX - 6, topY);
        }

        public new void Hide()
        {
            if (Visible) base.Hide();
        }
    }

    // ------------------------------------------------------------------
    //  The buddy himself: a layered window plus the behaviour brain.
    //  Poses are the Shimeji pack's shimeNN numbers.
    // ------------------------------------------------------------------
    internal sealed class BuddyForm : LayeredForm
    {
        private enum State { Paused, Walking, Jumping, Falling, Dragging, Landing, Climbing, WallHang }

        private static readonly Random Rng = new Random();

        // Pose sequences, from the pack's own action definitions.
        private static readonly int[] WalkCycle = { 1, 2, 1, 3 };       // 歩く
        private static readonly int[] ClimbCycle = { 14, 12, 13 };      // 壁を登る
        private static readonly int[] IdlePoses = { 1, 1, 1, 1, 11, 30, 26, 21 }; // stand-weighted, then sits / lie
        private static readonly int[] EmotionPoses = { 9, 15, 27, 33, 37, 41 };

        private readonly Timer _timer = new Timer();
        private readonly WebLineForm _web = new WebLineForm();
        private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
        private double _lastTick;

        private State _state = State.Paused;
        private double _x, _yTop;              // window top-left, screen coords
        private bool _facingRight = true;

        // Wallpaper-only layer mode (see Tick).
        private bool _wallApplied;
        private double _lastBottomAt;
        private static readonly IntPtr HwndBottom = (IntPtr)1;
        private const uint SwpNoMove = 0x0002, SwpNoSize = 0x0001, SwpNoActivate = 0x0010;

        private void ApplyWallMode()
        {
            _wallApplied = BuddySettings.WallpaperOnly;
            TopMost = !_wallApplied;
            try { if (_web != null) _web.TopMost = !_wallApplied; } catch { }
            if (_wallApplied)
                SetWindowPos(Handle, HwndBottom, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        }
        private int _pose = 1;

        // pause / idle / emotion
        private double _pauseEnd, _nextEmotionAt;
        private int _idlePose = 1;
        private double _emotionEnd;
        private int _emotionPose = -1;
        // walk
        private double _walkTargetX, _animTimer;
        private int _animStep;
        private bool _targetIsEdge, _edgeIsLeft;
        // jump
        private double _jumpT, _jumpFromX, _jumpToX, _jumpPeak, _lastJumpAt, _nextJumpDelay;
        // fall / land
        private double _fallV, _landEnd;
        private bool _landSecond;
        // climb
        private bool _climbOnLeftWall;
        private double _hangEnd;
        // drag
        private Point _dragOffset, _dragStartPos;
        private double _dragStartAt;
        // fullscreen
        private int _fsTickCounter;
        private bool _hiddenByFullscreen;

        public BuddyForm()
        {
            double now = Now();
            _pauseEnd = now + 1.2;
            _nextEmotionAt = now + 12 + Rng.NextDouble() * 10;
            _lastJumpAt = now;
            _nextJumpDelay = 15 + Rng.NextDouble() * 15;
            PlaceAtStart();

            _timer.Interval = 33;
            _timer.Tick += (s, e) => Tick();
            _timer.Start();

            MouseDown += OnMouseDown;
            MouseMove += OnMouseMove;
            MouseUp += OnMouseUp;
            Shown += (s, e) => Render();
        }

        // ---------------- environment ----------------

        private double Now() { return _clock.Elapsed.TotalSeconds; }

        private Screen BuddyScreen
        {
            get
            {
                Screen[] all = Screen.AllScreens;
                int i = BuddySettings.Monitor;
                if (i < 0 || i >= all.Length) i = 0;
                return all[i];
            }
        }

        public int SizePx { get { return BuddySettings.SizePx; } }
        private Rectangle Wa { get { return BuddyScreen.WorkingArea; } }
        private double GroundTop { get { return Wa.Bottom - SizePx; } }

        private void PlaceAtStart()
        {
            _x = Wa.Left + (Wa.Width - SizePx) / 2.0;
            _yTop = GroundTop;
        }

        public void OnSettingsChanged()
        {
            _x = Math.Max(Wa.Left, Math.Min(_x, Wa.Right - SizePx));
            if (_state == State.Paused || _state == State.Walking) _yTop = GroundTop;
            Render();
        }

        // ---------------- rendering ----------------

        private void Render()
        {
            if (!IsHandleCreated) return;
            bool suppressed = _hiddenByFullscreen || !BuddySettings.BuddyVisible;
            if (suppressed) { if (Visible) Hide(); _web.Hide(); return; }
            if (!Visible) Show();
            Bitmap bmp = BuddyRenderer.GetFrame(_pose, SizePx, _facingRight);
            PaintLayered(bmp, (int)Math.Round(_x), (int)Math.Round(_yTop));
        }

        private void SetPose(int pose, ref bool dirty)
        {
            if (pose != _pose) { _pose = pose; dirty = true; }
        }

        // ---------------- the brain ----------------

        private void Tick()
        {
            double now = Now();
            double dt = now - _lastTick;
            _lastTick = now;
            if (dt <= 0 || dt > 0.1) dt = 0.033;

            if (++_fsTickCounter >= 15)
            {
                _fsTickCounter = 0;
                bool fs = IsFullscreenForeground();
                if (fs != _hiddenByFullscreen) { _hiddenByFullscreen = fs; Render(); }
            }

            // Wallpaper-only mode: live on the desktop layer, behind every
            // app window - re-assert bottom position twice a second (not
            // while he is being dragged).
            if (BuddySettings.WallpaperOnly != _wallApplied) ApplyWallMode();
            if (_wallApplied && _state != State.Dragging && now - _lastBottomAt > 0.5)
            {
                _lastBottomAt = now;
                SetWindowPos(Handle, HwndBottom, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
            }
            if (_hiddenByFullscreen || !BuddySettings.BuddyVisible) return;

            bool dirty = false;

            // Emotion overlay timer.
            if (_emotionPose >= 0 && now >= _emotionEnd)
            {
                _emotionPose = -1;
                _nextEmotionAt = now + 15 + Rng.NextDouble() * 20;
                dirty = true;
            }

            switch (_state)
            {
                case State.Dragging:
                    return; // mouse handlers own position + pose

                case State.Falling:
                {
                    _fallV += 2800 * dt;
                    _yTop += _fallV * dt;
                    if (_yTop >= GroundTop)
                    {
                        _yTop = GroundTop;
                        if (_fallV > 80) { _fallV = -_fallV * 0.4; }
                        else
                        {
                            _fallV = 0;
                            _state = State.Landing;
                            _landEnd = now + 0.28;
                            _landSecond = false;
                        }
                    }
                    SetPose(4, ref dirty);   // 落ちる
                    break;
                }

                case State.Landing:
                {
                    SetPose(_landSecond ? 19 : 18, ref dirty);   // 転ぶ
                    if (!_landSecond && now >= _landEnd - 0.14) _landSecond = true;
                    if (now >= _landEnd) EnterPause(now, 2 + Rng.NextDouble() * 3);
                    break;
                }

                case State.Jumping:
                {
                    _jumpT += dt * new[] { 2.0, 1.8, 1.5 }[BuddySettings.Swing];
                    if (_jumpT >= 1.0)
                    {
                        _x = _jumpToX;
                        _yTop = GroundTop;
                        _web.Hide();
                        EnterPause(now, 4 + Rng.NextDouble() * 6);
                    }
                    else
                    {
                        _x = _jumpFromX + (_jumpToX - _jumpFromX) * _jumpT;
                        double t = _jumpT;
                        _yTop = GroundTop - _jumpPeak * 4 * t * (1 - t);
                        int handX = (int)Math.Round(_x + SizePx / 2.0);
                        _web.ShowLine(handX, BuddyScreen.Bounds.Top, (int)Math.Round(_yTop) + 6);
                    }
                    SetPose(22, ref dirty);   // ジャンプ
                    break;
                }

                case State.Climbing:
                {
                    _animTimer += dt;
                    if (_animTimer >= 0.22) { _animTimer = 0; _animStep = (_animStep + 1) % ClimbCycle.Length; dirty = true; }
                    _yTop -= 45 * BuddySettings.Pace * dt;
                    double topLimit = Wa.Top + 4;
                    if (_yTop <= topLimit)
                    {
                        _yTop = topLimit;
                        _state = State.WallHang;
                        _hangEnd = now + 2 + Rng.NextDouble() * 2.5;
                    }
                    SetPose(ClimbCycle[_animStep], ref dirty);
                    break;
                }

                case State.WallHang:
                {
                    SetPose(13, ref dirty);   // 壁に掴まる
                    if (now >= _hangEnd)
                    {
                        // Let go: drop with a little inward push.
                        _x = _climbOnLeftWall ? Wa.Left + 6 : Wa.Right - SizePx - 6;
                        _fallV = 0;
                        _state = State.Falling;
                    }
                    break;
                }

                case State.Paused:
                {
                    _yTop = GroundTop;
                    MaybeEmotion(now, ref dirty);
                    if (now >= _pauseEnd) StartWalk(now);
                    else SetPose(_emotionPose >= 0 ? _emotionPose : _idlePose, ref dirty);
                    break;
                }

                case State.Walking:
                {
                    if (now - _lastJumpAt > _nextJumpDelay)
                    {
                        StartJump(now);
                        dirty = true;
                        break;
                    }
                    _animTimer += dt;
                    if (_animTimer >= 0.18) { _animTimer = 0; _animStep = (_animStep + 1) % WalkCycle.Length; dirty = true; }
                    double step = 60 * BuddySettings.Pace * dt;
                    bool arrived;
                    if (_facingRight) { _x += step; arrived = _x >= _walkTargetX; if (arrived) _x = _walkTargetX; }
                    else { _x -= step; arrived = _x <= _walkTargetX; if (arrived) _x = _walkTargetX; }
                    _yTop = GroundTop;
                    if (arrived)
                    {
                        // At a screen edge, sometimes he climbs instead.
                        if (_targetIsEdge && Rng.NextDouble() < 0.4) StartClimb();
                        else EnterPause(now, 4 + Rng.NextDouble() * 6);
                    }
                    else SetPose(WalkCycle[_animStep], ref dirty);
                    break;
                }
            }

            if (dirty) Render();
            else if (_state == State.Walking || _state == State.Jumping || _state == State.Falling || _state == State.Climbing) Render();
        }

        private void MaybeEmotion(double now, ref bool dirty)
        {
            if (_emotionPose >= 0 || now < _nextEmotionAt) return;
            _emotionPose = EmotionPoses[Rng.Next(EmotionPoses.Length)];
            _emotionEnd = now + 1.4;
            dirty = true;
        }

        public void TriggerEmotion()
        {
            _emotionPose = -1;
            _nextEmotionAt = 0;
            bool d = false;
            MaybeEmotion(Now(), ref d);
            _emotionEnd = Now() + 1.6;
            Render();
        }

        private void EnterPause(double now, double duration)
        {
            _state = State.Paused;
            _pauseEnd = now + duration;
            _idlePose = IdlePoses[Rng.Next(IdlePoses.Length)];
            _emotionPose = -1;
        }

        private void StartWalk(double now)
        {
            double margin = 4;
            double left = Wa.Left + margin;
            double right = Wa.Right - SizePx - margin;
            if (_x >= right - 20) _facingRight = false;
            else if (_x <= left + 20) _facingRight = true;
            else _facingRight = Rng.Next(2) == 0;

            double dist = 80 + Rng.NextDouble() * 120;
            if (_facingRight)
            {
                _walkTargetX = Math.Min(_x + dist, right);
                _targetIsEdge = _walkTargetX >= right - 0.5;
                _edgeIsLeft = false;
            }
            else
            {
                _walkTargetX = Math.Max(_x - dist, left);
                _targetIsEdge = _walkTargetX <= left + 0.5;
                _edgeIsLeft = true;
            }
            _animTimer = 0;
            _animStep = 0;
            _emotionPose = -1;
            _state = State.Walking;
        }

        public void TriggerWalk()
        {
            if (_state == State.Paused) StartWalk(Now());
        }

        private void StartClimb()
        {
            _climbOnLeftWall = _edgeIsLeft;
            _x = _climbOnLeftWall ? Wa.Left : Wa.Right - SizePx;
            _facingRight = !_climbOnLeftWall;   // face the wall
            _animTimer = 0;
            _animStep = 0;
            _emotionPose = -1;
            _state = State.Climbing;
        }

        public void TriggerClimb()
        {
            if (_state != State.Paused && _state != State.Walking) return;
            // Climb whichever wall is nearer.
            _edgeIsLeft = (_x - Wa.Left) < (Wa.Right - SizePx - _x);
            StartClimb();
        }

        private void StartJump(double now)
        {
            _lastJumpAt = now;
            _nextJumpDelay = 30 + Rng.NextDouble() * 30;
            _jumpT = 0;
            _jumpFromX = _x;
            // Swing profiles (tray: Swing height). Classic = the macOS
            // original's numbers; Big is the default - higher and longer.
            double[] distBase = { 60, 100, 200 }, distVar = { 100, 200, 240 };
            double[] peakBase = { 90, 160, 280 }, peakVar = { 50, 80, 160 };
            int sw = BuddySettings.Swing;
            double dist = (distBase[sw] + Rng.NextDouble() * distVar[sw]) * (Rng.Next(2) == 0 ? 1 : -1);
            double min = Wa.Left + 10, max = Wa.Right - SizePx - 10;
            _jumpToX = Math.Max(min, Math.Min(_x + dist, max));
            _jumpPeak = peakBase[sw] + Rng.NextDouble() * peakVar[sw];
            _facingRight = _jumpToX >= _jumpFromX;
            _emotionPose = -1;
            _state = State.Jumping;
        }

        public void TriggerJump()
        {
            if (_state == State.Walking || _state == State.Paused) StartJump(Now());
        }

        // ---------------- mouse: drag him around ----------------

        private void OnMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _state = State.Dragging;
            _emotionPose = -1;
            _web.Hide();
            _dragOffset = new Point(e.X, e.Y);
            _dragStartAt = Now();
            _dragStartPos = Cursor.Position;
            bool d = false;
            SetPose(9, ref d);   // つままれる (grabbed: startled)
            Render();
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (_state != State.Dragging) return;
            _x = Cursor.Position.X - _dragOffset.X;
            _yTop = Cursor.Position.Y - _dragOffset.Y;
            _x = Math.Max(Wa.Left - SizePx / 2, Math.Min(_x, Wa.Right - SizePx / 2));
            _yTop = Math.Max(Wa.Top, Math.Min(_yTop, GroundTop));
            Render();
        }

        private void OnMouseUp(object sender, MouseEventArgs e)
        {
            if (_state != State.Dragging) return;
            double held = Now() - _dragStartAt;
            int moved = Math.Abs(Cursor.Position.X - _dragStartPos.X) + Math.Abs(Cursor.Position.Y - _dragStartPos.Y);
            if (held < 0.25 && moved < 6)
            {
                // A poke, not a drag: he reacts.
                EnterPause(Now(), 2.5);
                TriggerEmotion();
                return;
            }
            _fallV = 0;
            _state = State.Falling;
            Render();
        }

        // -------- per-pixel click-through (current pose's alpha) --------

        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x0084;
            const int HTTRANSPARENT = -1;
            const int HTCLIENT = 1;
            if (m.Msg == WM_NCHITTEST && _state != State.Dragging)
            {
                int sx = (short)(m.LParam.ToInt32() & 0xFFFF);
                int sy = (short)(m.LParam.ToInt32() >> 16);
                int px = sx - (int)Math.Round(_x);
                int py = sy - (int)Math.Round(_yTop);
                bool opaque = BuddyRenderer.IsOpaqueAt(_pose, SizePx, _facingRight, px, py);
                m.Result = (IntPtr)(opaque ? HTCLIENT : HTTRANSPARENT);
                return;
            }
            base.WndProc(ref m);
        }

        // ---------------- fullscreen detection ----------------

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        private bool IsFullscreenForeground()
        {
            try
            {
                IntPtr fg = GetForegroundWindow();
                if (fg == IntPtr.Zero || fg == Handle || fg == _web.Handle) return false;
                var sb = new System.Text.StringBuilder(64);
                GetClassName(fg, sb, 64);
                string cls = sb.ToString();
                if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd") return false;
                RECT r;
                if (!GetWindowRect(fg, out r)) return false;
                Rectangle b = Screen.FromHandle(fg).Bounds;
                int w = r.Right - r.Left, h = r.Bottom - r.Top;
                return w >= b.Width * 0.995 && h >= b.Height * 0.995 && r.Left <= b.Left + 2 && r.Top <= b.Top + 2;
            }
            catch { return false; }
        }
    }

    // ------------------------------------------------------------------
    //  Tray icon + menu: the buddy's control surface.
    // ------------------------------------------------------------------
    internal sealed class BuddyContext : ApplicationContext
    {
        private readonly BuddyForm _form;
        private readonly NotifyIcon _tray;
        private readonly ContextMenuStrip _menu;

        private ToolStripMenuItem _showItem, _wallItem;
        private ToolStripMenuItem _sizeItem, _paceItem, _monitorItem, _swingItem;

        public BuddyContext()
        {
            BuddySettings.Load();
            _form = new BuddyForm();

            _menu = new ContextMenuStrip();
            _menu.Opening += (s, e) => RefreshMenu();

            var header = new ToolStripMenuItem("Spidey Companion") { Enabled = false };
            _menu.Items.Add(header);
            _menu.Items.Add(new ToolStripSeparator());

            _showItem = new ToolStripMenuItem("Show buddy");
            _showItem.Click += (s, e) =>
            {
                BuddySettings.BuddyVisible = !BuddySettings.BuddyVisible;
                BuddySettings.Save();
                _form.OnSettingsChanged();
            };
            _menu.Items.Add(_showItem);

            _wallItem = new ToolStripMenuItem("Wallpaper only");
            _wallItem.Click += (s, e) =>
            {
                BuddySettings.WallpaperOnly = !BuddySettings.WallpaperOnly;
                BuddySettings.Save();
            };
            _menu.Items.Add(_wallItem);

            _sizeItem = new ToolStripMenuItem("Buddy size");
            _menu.Items.Add(_sizeItem);
            _paceItem = new ToolStripMenuItem("Pace");
            _menu.Items.Add(_paceItem);
            _swingItem = new ToolStripMenuItem("Swing height");
            _menu.Items.Add(_swingItem);
            _monitorItem = new ToolStripMenuItem("Display");
            _menu.Items.Add(_monitorItem);
            _menu.Items.Add(new ToolStripSeparator());

            var jump = new ToolStripMenuItem("Jump now");
            jump.Click += (s, e) => _form.TriggerJump();
            _menu.Items.Add(jump);
            var climb = new ToolStripMenuItem("Climb a wall");
            climb.Click += (s, e) => _form.TriggerClimb();
            _menu.Items.Add(climb);
            var emo = new ToolStripMenuItem("Show an emotion");
            emo.Click += (s, e) => _form.TriggerEmotion();
            _menu.Items.Add(emo);
            var walk = new ToolStripMenuItem("Walk now");
            walk.Click += (s, e) => _form.TriggerWalk();
            _menu.Items.Add(walk);
            _menu.Items.Add(new ToolStripSeparator());

            var quit = new ToolStripMenuItem("Quit Spidey Companion");
            quit.Click += (s, e) =>
            {
                _tray.Visible = false;
                _form.Close();
                ExitThread();
            };
            _menu.Items.Add(quit);

            _tray = new NotifyIcon
            {
                Text = "Spidey Companion",
                Icon = MakeIcon(),
                ContextMenuStrip = _menu,
                Visible = true
            };
            _tray.DoubleClick += (s, e) =>
            {
                BuddySettings.BuddyVisible = !BuddySettings.BuddyVisible;
                BuddySettings.Save();
                _form.OnSettingsChanged();
            };

            _form.Show();
        }

        private Icon MakeIcon()
        {
            try
            {
                return Icon.FromHandle(BuddyRenderer.IconBitmap().GetHicon());
            }
            catch { return SystemIcons.Application; }
        }

        private void RefreshMenu()
        {
            _showItem.Checked = BuddySettings.BuddyVisible;
            _wallItem.Checked = BuddySettings.WallpaperOnly;

            _swingItem.DropDownItems.Clear();
            foreach (var opt in new[]
            {
                new KeyValuePair<int, string>(0, "Small hop"),
                new KeyValuePair<int, string>(1, "Classic"),
                new KeyValuePair<int, string>(2, "Big swing")
            })
            {
                int v = opt.Key;
                var it = new ToolStripMenuItem(opt.Value) { Checked = BuddySettings.Swing == v };
                it.Click += (s, e) =>
                {
                    BuddySettings.Swing = v;
                    BuddySettings.Save();
                };
                _swingItem.DropDownItems.Add(it);
            }

            _sizeItem.DropDownItems.Clear();
            foreach (var opt in new[]
            {
                new KeyValuePair<int, string>(96, "Compact - 96 px"),
                new KeyValuePair<int, string>(128, "Native - 128 px"),
                new KeyValuePair<int, string>(160, "Large - 160 px")
            })
            {
                int sc = opt.Key;
                var it = new ToolStripMenuItem(opt.Value) { Checked = BuddySettings.SizePx == sc };
                it.Click += (s, e) =>
                {
                    BuddySettings.SizePx = sc;
                    BuddySettings.Save();
                    _form.OnSettingsChanged();
                };
                _sizeItem.DropDownItems.Add(it);
            }

            _paceItem.DropDownItems.Clear();
            foreach (var opt in new[]
            {
                new KeyValuePair<double, string>(0.7, "Chill"),
                new KeyValuePair<double, string>(1.0, "Normal"),
                new KeyValuePair<double, string>(1.5, "Zippy")
            })
            {
                double p = opt.Key;
                var it = new ToolStripMenuItem(opt.Value) { Checked = Math.Abs(BuddySettings.Pace - p) < 0.01 };
                it.Click += (s, e) =>
                {
                    BuddySettings.Pace = p;
                    BuddySettings.Save();
                };
                _paceItem.DropDownItems.Add(it);
            }

            _monitorItem.DropDownItems.Clear();
            Screen[] all = Screen.AllScreens;
            for (int i = 0; i < all.Length; i++)
            {
                int idx = i;
                var it = new ToolStripMenuItem("Display " + (i + 1) + " (" + all[i].Bounds.Width + "x" + all[i].Bounds.Height + ")")
                {
                    Checked = BuddySettings.Monitor == i
                };
                it.Click += (s, e) =>
                {
                    BuddySettings.Monitor = idx;
                    BuddySettings.Save();
                    _form.OnSettingsChanged();
                };
                _monitorItem.DropDownItems.Add(it);
            }
            _monitorItem.Enabled = all.Length > 1;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_tray != null) _tray.Dispose();
                if (_menu != null) _menu.Dispose();
                if (_form != null) _form.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    // ------------------------------------------------------------------
    //  Entry point used by Program.Main: runs the UI on the main (STA)
    //  thread while the HTTP listener and media worker keep their own
    //  threads, exactly as before. Returns false if the UI could not
    //  start, so Main can fall back to the old invisible wait.
    // ------------------------------------------------------------------
    internal static class BuddyHost
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        public static bool Run()
        {
            try { SetProcessDPIAware(); } catch { }
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (var ctx = new BuddyContext())
                {
                    Application.Run(ctx);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
