using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SylphyHorn.Services
{
	/// <summary>
	/// Low-level keyboard hook owned by this process. Replaces the netfx-only Open.WinKeyboardHook package.
	/// </summary>
	public sealed class KeyboardInterceptor : IDisposable
	{
		private const int WhKeyboardLl = 13;
		private const int WmKeyDown = 0x0100;
		private const int WmKeyUp = 0x0101;
		private const int WmSysKeyDown = 0x0104;
		private const int WmSysKeyUp = 0x0105;

		public event EventHandler<KeyEventArgs> KeyDown;
		public event EventHandler<KeyEventArgs> KeyUp;

		private HookProc _callback;
		private IntPtr _handle;

		public void StartCapturing()
		{
			if (this._handle != IntPtr.Zero) return;

			// A managed assembly can report HINSTANCE -1. The low-level hook runs in this process.
			this._callback = this.HookProcedure;
			this._handle = SetWindowsHookEx(WhKeyboardLl, this._callback, GetModuleHandle(null), 0);
			if (this._handle == IntPtr.Zero)
			{
				this._callback = null;
				throw new System.ComponentModel.Win32Exception();
			}
		}

		public void StopCapturing()
		{
			if (this._handle == IntPtr.Zero) return;
			// The hook still calls this delegate until UnhookWindowsHookEx succeeds. Dropping it earlier is a use-after-free.
			if (!UnhookWindowsHookEx(this._handle)) return;
			this._handle = IntPtr.Zero;
			this._callback = null;
		}

		public void Dispose()
		{
			this.StopCapturing();
		}

		private IntPtr HookProcedure(int code, IntPtr wParam, IntPtr lParam)
		{
			if (code >= 0)
			{
				var data = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
				var message = wParam.ToInt32();
				var args = new KeyEventArgs((Keys)data.vkCode);
				if (message == WmKeyDown || message == WmSysKeyDown)
				{
					this.KeyDown?.Invoke(this, args);
				}
				else if (message == WmKeyUp || message == WmSysKeyUp)
				{
					this.KeyUp?.Invoke(this, args);
				}

				if (args.SuppressKeyPress)
				{
					return (IntPtr)1;
				}
			}

			return CallNextHookEx(this._handle, code, wParam, lParam);
		}

		private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

		[StructLayout(LayoutKind.Sequential)]
		private struct KbdLlHookStruct
		{
			public uint vkCode;
			public uint scanCode;
			public uint flags;
			public uint time;
			public IntPtr dwExtraInfo;
		}

		[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
		private static extern IntPtr GetModuleHandle(string moduleName);

		[DllImport("user32.dll", SetLastError = true)]
		private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

		[DllImport("user32.dll")]
		private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

		[DllImport("user32.dll", SetLastError = true)]
		private static extern bool UnhookWindowsHookEx(IntPtr hhk);
	}
}
