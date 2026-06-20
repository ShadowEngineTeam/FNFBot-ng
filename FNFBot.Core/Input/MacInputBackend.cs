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

        private ushort[] _keys = { 0x7B, 0x7D, 0x7E, 0x7C };

        /// <summary>
        /// Whether this app is trusted for Accessibility. macOS silently drops synthetic key
        /// events from untrusted apps, so the UI uses this to warn instead of failing quietly.
        /// </summary>
        public static bool IsTrusted()
        {
            try { return AXIsProcessTrusted(); }
            catch { return true; }
        }

        public void SetKeyCodes(int[] codes)
        {
            _keys = new ushort[codes.Length];
            for (int i = 0; i < codes.Length; i++)
                _keys[i] = (ushort)(codes[i] & 0xFFFF);
        }

        public void KeyDown(int direction) => Post(_keys[direction], true);
        public void KeyUp(int direction) => Post(_keys[direction], false);

        private static void Post(ushort vk, bool down)
        {
            IntPtr evt = CGEventCreateKeyboardEvent(IntPtr.Zero, vk, down);
            if (evt == IntPtr.Zero) return;
            CGEventPost(kCGHIDEventTap, evt);
            CFRelease(evt);
        }
    }
}
