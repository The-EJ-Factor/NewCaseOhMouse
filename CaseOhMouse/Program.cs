using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

class Program
{
    // ===================== Win32 =====================

    [DllImport("user32.dll")]
    static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    static extern bool SetCursorPos(int X, int Y);

    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int width, int height, bool repaint);

    [DllImport("user32.dll")]
    static extern int GetSystemMetrics(int nIndex);

    const int SM_XVIRTUALSCREEN = 76;
    const int SM_YVIRTUALSCREEN = 77;
    const int SM_CXVIRTUALSCREEN = 78;
    const int SM_CYVIRTUALSCREEN = 79;

    const int VK_PGUP = 0x21;
    const int VK_END = 0x23;
    const int VK_LBUTTON = 0x01;

    // ===== Per-monitor bounds support =====

    [DllImport("user32.dll")]
    static extern IntPtr MonitorFromPoint(POINT pt, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    const uint MONITOR_DEFAULTTONEAREST = 2;

    struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    struct POINT
    {
        public int X;
        public int Y;
    }

    struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    // ===================== Physics =====================

    static double bounce = 0.7;
    static double baseWeight = 10.0; // higher = more inertia
    static double friction = 0.992;   //value from 0-1, higher means more friction
    static double gravity = 0.6;

    static double xMomentum = 0;
    static double yMomentum = 0;

    // TRUE previous mouse position (persistent across frames)
    static int lastMouseX;
    static int lastMouseY;

    static bool running = true;
    static bool runAll = true;
    static bool canToggle = true;

    static int holdTime = 0;

    static int stamina = 50;
    static int windowStrength = 100;

    static bool windowLostGrip = false;

    static double windowDropChancePerSec = 0.75;

    static int fps = 180;

    static int screenLeft;
    static int screenTop;
    static int screenWidth;
    static int screenHeight;

    static Stopwatch timer = new Stopwatch();

    static Random rng = new Random();

    static double shakeTime = 0;
    static double shakePower = 0;

    // ===== Easy tuning variables =====
    static double shakeDurationBase = 0.30;      // seconds
    static double shakeIntensityMultiplier = 1.5; // scales with impact speed
    static double shakeRadius = 600;             // pixels: only nearby windows shake
    static int maxShakeOffset = 40;              // prevents extreme displacement

    static void Main()
    {
        screenLeft = GetSystemMetrics(SM_XVIRTUALSCREEN);
        screenTop = GetSystemMetrics(SM_YVIRTUALSCREEN);
        screenWidth = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        screenHeight = GetSystemMetrics(SM_CYVIRTUALSCREEN);

        timer.Start();

        POINT start;
        GetCursorPos(out start);

        lastMouseX = start.X;
        lastMouseY = start.Y;

        double lastTime = timer.Elapsed.TotalSeconds;

        while (runAll)
        {
            double now = timer.Elapsed.TotalSeconds;
            double deltaTime = now - lastTime;
            lastTime = now;

            if (KeyPressed(VK_END))
                runAll = false;

            if (running)
            {
                Update(deltaTime);
            }
            else
            {
                if (KeyPressed(VK_PGUP))
                {
                    if (canToggle)
                    {
                        running = true;
                        canToggle = false;
                    }
                }
                else
                {
                    canToggle = true;
                }
            }

            Thread.Sleep(1000 / fps);
        }
    }

    static void Update(double dt)
    {
        if (KeyPressed(VK_PGUP))
        {
            if (canToggle)
            {
                running = false;
                canToggle = false;
            }
        }
        else
        {
            canToggle = true;
        }

        POINT current;
        GetCursorPos(out current);

        // ===== Get bounds of current monitor =====
        MONITORINFO mi = new MONITORINFO();
        mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));

        IntPtr monitor = MonitorFromPoint(current, MONITOR_DEFAULTTONEAREST);
        GetMonitorInfo(monitor, ref mi);

        int monitorLeft = mi.rcMonitor.Left;
        int monitorTop = mi.rcMonitor.Top;
        int monitorRight = mi.rcMonitor.Right;
        int monitorBottom = mi.rcMonitor.Bottom;

        // REAL user movement (since last frame)
        int userDX = current.X - lastMouseX;
        int userDY = current.Y - lastMouseY;

        // Store immediately for next frame
        lastMouseX = current.X;
        lastMouseY = current.Y;

        // ================= Momentum =================

        double heightFactor = (current.Y - screenTop) / (double)Math.Max(1, screenHeight);
        double effectiveWeight = baseWeight * (1.0 + heightFactor);

        // Add user's movement to momentum
        xMomentum += userDX / effectiveWeight;
        yMomentum += userDY / effectiveWeight;

        // Gravity always pulls downward
        yMomentum += gravity;

        // Friction slowly removes motion
        xMomentum *= friction;
        yMomentum *= friction;

        // ================= Window grabbing =================

        if (IsUserGrabbingWindow())
        {
            holdTime++;

            if (holdTime >= windowStrength)
            {
                if (rng.NextDouble() <= windowDropChancePerSec * dt)
                    windowLostGrip = true;
            }

            if (windowLostGrip)
            {
                MoveActiveWindow((int)xMomentum, (int)yMomentum);
            }
            else if (holdTime < stamina)
            {
                xMomentum = 0;
                yMomentum = 0;
            }
            else if (holdTime < stamina * 2)
            {
                xMomentum *= 0.5;
                yMomentum *= 0.5;
            }
            else
            {
                xMomentum *= 0.75;
                yMomentum *= 0.75;
            }
        }
        else
        {
            holdTime = 0;
            windowLostGrip = false;
        }

        // ================= Move mouse =================

        int newX = current.X + (int)Math.Round(xMomentum);
        int newY = current.Y + (int)Math.Round(yMomentum);

        if (newX >= monitorRight - 1 || newX <= monitorLeft)
        {
            if (Math.Abs(xMomentum) > 3)
            {
                shakePower = Math.Min(Math.Abs(xMomentum) * shakeIntensityMultiplier, maxShakeOffset);
                shakeTime = shakeDurationBase;
            }

            xMomentum *= -bounce;
            newX = current.X;
        }

        if (newY >= monitorBottom - 1 || newY <= monitorTop)
        {
            if (Math.Abs(yMomentum) > 3)
            {
                shakePower = Math.Min(Math.Abs(yMomentum) * shakeIntensityMultiplier, maxShakeOffset);
                shakeTime = shakeDurationBase;
            }

            yMomentum *= -bounce;
            newY = current.Y;
        }

        SetCursorPos(newX, newY);

        // IMPORTANT: update stored position to physics result
        lastMouseX = newX;
        lastMouseY = newY;

        // ================= Window shaking =================

        if (shakeTime > 0)
        {
            ShakeNearbyWindows(current);
            shakeTime -= dt;
        }
        else
        {
            shakePower = 0;
        }
    }

    static bool KeyPressed(int key)
    {
        return (GetAsyncKeyState(key) & 0x8000) != 0;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    static bool IsUserGrabbingWindow()
    {
        if (!KeyPressed(VK_LBUTTON))
            return false;

        IntPtr hwnd = GetForegroundWindow();

        if (hwnd == IntPtr.Zero)
            return false;

        var className = new System.Text.StringBuilder(256);
        GetClassName(hwnd, className, className.Capacity);

        string cls = className.ToString();

        if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd")
            return false;

        if (hwnd == IntPtr.Zero)
            return false;

        RECT rect;

        if (!GetWindowRect(hwnd, out rect))
            return false;

        POINT p;
        GetCursorPos(out p);

        return p.X >= rect.Left && p.X <= rect.Right &&
               p.Y >= rect.Top && p.Y <= rect.Bottom;
    }

    static void MoveActiveWindow(int dx, int dy)
    {
        IntPtr hwnd = GetForegroundWindow();

        if (hwnd == IntPtr.Zero)
            return;

        RECT rect;

        if (!GetWindowRect(hwnd, out rect))
            return;

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;

        MoveWindow(
            hwnd,
            rect.Left + dx,
            rect.Top + dy,
            width,
            height,
            true
        );
    }

    static void ShakeNearbyWindows(POINT mousePos)
    {
        IntPtr hwnd = GetForegroundWindow();

        if (hwnd == IntPtr.Zero)
            return;

        RECT rect;

        if (!GetWindowRect(hwnd, out rect))
            return;

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;

        int offsetX = rng.Next((int)-shakePower, (int)shakePower);
        int offsetY = rng.Next((int)-shakePower, (int)shakePower);

        // Clamp so window cannot be shaken off screen
        int newLeft = Math.Max(screenLeft, Math.Min(rect.Left + offsetX, screenLeft + screenWidth - width));
        int newTop = Math.Max(screenTop, Math.Min(rect.Top + offsetY, screenTop + screenHeight - height));

        MoveWindow(
            hwnd,
            newLeft,
            rect.Top + offsetY,
            width,
            height,
            true
        );
    }
}
