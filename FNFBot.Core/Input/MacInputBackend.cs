using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace FNFBot.Core.Input
{
    /// <summary>
    /// macOS key injection via CGEventPost. Without Accessibility permission, macOS silently
    /// drops injected events.
    /// </summary>
    [SupportedOSPlatform("macos")]
    public sealed class MacInputBackend : IInputBackend
    {
        private const string CG = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
        private const string CF = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

        [DllImport(CG)]
        private static extern IntPtr CGEventCreateKeyboardEvent(IntPtr source, ushort virtualKey, [MarshalAs(UnmanagedType.I1)] bool keyDown);

        [DllImport(CG)]
        private static extern void CGEventPost(uint tap, IntPtr evt);

        [DllImport(CF)]
        private static extern void CFRelease(IntPtr cf);

        private const uint kCGHIDEventTap = 0;

        // macOS virtual key codes for the arrows, indexed by direction (0=L,1=D,2=U,3=R).
        private static readonly ushort[] ArrowKeys = { 123, 125, 126, 124 };

        public void KeyDown(int direction) => Post(ArrowKeys[direction], true);
        public void KeyUp(int direction) => Post(ArrowKeys[direction], false);

        private static void Post(ushort vk, bool down)
        {
            IntPtr evt = CGEventCreateKeyboardEvent(IntPtr.Zero, vk, down);
            if (evt == IntPtr.Zero) return;
            CGEventPost(kCGHIDEventTap, evt);
            CFRelease(evt);
        }
    }
}
