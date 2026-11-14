using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using SylphyHorn.Serialization;

namespace SylphyHorn.Services
{
    internal sealed class AlwaysOnTopService : IDisposable
    {
        private readonly Dictionary<IntPtr, Window> windows = new Dictionary<IntPtr, Window>();
        private readonly string marker = "SylphyHorn.Topmost." + Guid.NewGuid().ToString("N");
        private readonly DispatcherTimer timer;
        private bool enabled;

        public AlwaysOnTopService()
        {
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            timer.Tick += (s, e) => Refresh();
        }

        public void SetEnabled(bool value)
        {
            enabled = value;
            if (!value) RestoreAll();
        }

        public void Toggle(IntPtr handle)
        {
            if (!enabled || handle == IntPtr.Zero || !IsWindow(handle)) return;
            handle = GetAncestor(handle, 2);
            var name = new System.Text.StringBuilder(256);
            GetClassName(handle, name, name.Capacity);
            if (name.ToString() == "Progman" || name.ToString() == "WorkerW" || name.ToString().StartsWith("Shell_Tray")) return;
            if (windows.ContainsKey(handle)) { Release(handle); return; }
            // Uma janela já fixada por outro programa também pode ser desafixada explicitamente.
            if ((GetWindowLong(handle, -20) & 8) != 0)
            {
                SetTopmost(handle, false);
                return;
            }
            if (!SetProp(handle, marker, new IntPtr(1))) { ReportError(); return; }
            if (!SetTopmost(handle, true)) { RemoveProp(handle, marker); return; }
            windows.Add(handle, null);
            timer.Start();
            Refresh();
        }

        private static bool SetTopmost(IntPtr handle, bool value)
        {
            if (SetWindowPos(handle, new IntPtr(value ? -1 : -2), 0, 0, 0, 0, 0x13)) return true;
            ReportError();
            return false;
        }

        private static void ReportError() => LoggingService.Instance.Register(new Win32Exception(Marshal.GetLastWin32Error()));

        private void Refresh()
        {
            foreach (var handle in windows.Keys.ToArray())
            {
                // A propriedade desaparece ao destruir a janela, evitando atuar em um HWND reutilizado.
                if (GetProp(handle, marker) == IntPtr.Zero) { Forget(handle); continue; }
                if ((GetWindowLong(handle, -20) & 8) == 0) { Release(handle); continue; }
                var thickness = Math.Max(0, Math.Min(20, Settings.General.AlwaysOnTopBorderWidth.Value));
                int cloaked;
                DwmGetWindowAttribute(handle, 14, out cloaked, sizeof(int));
                if (thickness == 0 || !IsWindowVisible(handle) || IsIconic(handle) || cloaked != 0)
                {
                    windows[handle]?.Hide();
                    continue;
                }
                RectNative rect;
                if (DwmGetWindowAttributeRect(handle, 9, out rect, Marshal.SizeOf(typeof(RectNative))) != 0 && !GetWindowRect(handle, out rect)) continue;
                var overlay = windows[handle];
                if (overlay == null)
                {
                    overlay = new Window { WindowStyle = WindowStyle.None, AllowsTransparency = true,
                        Background = Brushes.Transparent, ShowInTaskbar = false, ShowActivated = false,
                        ResizeMode = ResizeMode.NoResize, Focusable = false, IsHitTestVisible = false,
                        Content = new Border() };
                    var helper = new WindowInteropHelper(overlay);
                    helper.EnsureHandle();
                    // WS_EX_TRANSPARENT + NOACTIVATE: a borda nunca recebe cliques nem foco.
                    SetWindowLong(helper.Handle, -20, GetWindowLong(helper.Handle, -20) | 0x080000A0);
                    windows[handle] = overlay;
                }
                Color color;
                try { color = (Color)ColorConverter.ConvertFromString(Settings.General.AlwaysOnTopBorderColor.Value); }
                catch { color = Color.FromRgb(0, 120, 215); }
                var dpi = GetDpiForWindow(new WindowInteropHelper(overlay).Handle);
                ((Border)overlay.Content).BorderBrush = new SolidColorBrush(color);
                ((Border)overlay.Content).BorderThickness = new Thickness(thickness * 96.0 / Math.Max(96u, dpi));
                overlay.Show();
                // Coordenadas físicas preservam o alinhamento em monitores com escalas diferentes.
                SetWindowPos(new WindowInteropHelper(overlay).Handle, new IntPtr(-1), rect.Left, rect.Top,
                    Math.Max(1, rect.Right - rect.Left), Math.Max(1, rect.Bottom - rect.Top), 0x10);
            }
        }

        private void Forget(IntPtr handle)
        {
            windows[handle]?.Close();
            windows.Remove(handle);
            if (windows.Count == 0) timer.Stop();
        }

        private void Release(IntPtr handle)
        {
            if (GetProp(handle, marker) != IntPtr.Zero)
            {
                SetTopmost(handle, false);
                RemoveProp(handle, marker);
            }
            Forget(handle);
        }

        private void RestoreAll() { foreach (var handle in windows.Keys.ToArray()) Release(handle); }
        public void Dispose() { enabled = false; timer.Stop(); RestoreAll(); }

        [StructLayout(LayoutKind.Sequential)] private struct RectNative { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr h, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, System.Text.StringBuilder text, int count);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool SetProp(IntPtr h, string name, IntPtr value);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetProp(IntPtr h, string name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr RemoveProp(IntPtr h, string name);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr h, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(IntPtr h, int index, int value);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int height, uint flags);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RectNative rect);
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr h);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr h, int attribute, out int value, int size);
        [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] private static extern int DwmGetWindowAttributeRect(IntPtr h, int attribute, out RectNative value, int size);
    }
}
