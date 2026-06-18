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
        private const string AS = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";

        [DllImport(CG)]
        private static extern IntPtr CGEventCreateKeyboardEvent(IntPtr source, ushort virtualKey, [MarshalAs(UnmanagedType.I1)] bool keyDown);

        [DllImport(CG)]
        private static extern void CGEventPost(uint tap, IntPtr evt);

        [DllImport(CF)]
        private static extern void CFRelease(IntPtr cf);

        [DllImport(AS)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool AXIsProcessTrusted();

        private const uint kCGHIDEventTap = 0;

        // Carbon virtual key codes (HIToolbox/Events.h), indexed by direction (0=L,1=D,2=U,3=R).
        private const ushort kVK_LeftArrow = 0x7B;  // 123
        private const ushort kVK_DownArrow = 0x7D;  // 125
        private const ushort kVK_UpArrow = 0x7E;    // 126
        private const ushort kVK_RightArrow = 0x7C; // 124
        private static readonly ushort[] ArrowKeys = { kVK_LeftArrow, kVK_DownArrow, kVK_UpArrow, kVK_RightArrow };

        /// <summary>
        /// Whether this app is trusted for Accessibility. macOS silently drops synthetic key
        /// events from untrusted apps, so the UI uses this to warn instead of failing quietly.
        /// </summary>
        public static bool IsTrusted()
        {
            try { return AXIsProcessTrusted(); }
            catch { return true; } // framework/symbol unavailable: don't false-alarm
        }

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
