using System;
using System.Collections.Generic;

namespace FNFBot.Core.Input
{
    /// <summary>
    /// Maps human-readable key names to platform-specific key codes for each input backend.
    /// </summary>
    public static class KeyMap
    {
        private static readonly Dictionary<string, ushort> WinScan = new()
        {
            { "Left",     0x4B }, { "Down",    0x50 }, { "Up",      0x48 }, { "Right",   0x4D },
            { "Space",    0x39 },
            { "A", 0x1E }, { "B", 0x30 }, { "C", 0x2E }, { "D", 0x20 }, { "E", 0x12 },
            { "F", 0x21 }, { "G", 0x22 }, { "H", 0x23 }, { "I", 0x17 }, { "J", 0x24 },
            { "K", 0x25 }, { "L", 0x26 }, { "M", 0x32 }, { "N", 0x31 }, { "O", 0x18 },
            { "P", 0x19 }, { "Q", 0x10 }, { "R", 0x13 }, { "S", 0x1F }, { "T", 0x14 },
            { "U", 0x16 }, { "V", 0x2F }, { "W", 0x11 }, { "X", 0x2D }, { "Y", 0x15 },
            { "Z", 0x2C }, { ";", 0x27 }, { "'", 0x28 }, { ",", 0x33 }, { ".", 0x34 },
            { "/", 0x35 },
            { "1", 0x02 }, { "2", 0x03 }, { "3", 0x04 }, { "4", 0x05 }, { "5", 0x06 },
            { "6", 0x07 }, { "7", 0x08 }, { "8", 0x09 }, { "9", 0x0A }, { "0", 0x0B },
        };

        private static readonly HashSet<string> WinExtended = new()
        {
            "Left", "Down", "Up", "Right"
        };

        private static readonly Dictionary<string, ushort> LinuxEv = new()
        {
            { "Left", 105 }, { "Down", 108 }, { "Up", 103 }, { "Right", 106 },
            { "Space", 57 },
            { "A", 30 }, { "B", 48 }, { "C", 46 }, { "D", 32 }, { "E", 18 },
            { "F", 33 }, { "G", 34 }, { "H", 35 }, { "I", 23 }, { "J", 36 },
            { "K", 37 }, { "L", 38 }, { "M", 50 }, { "N", 49 }, { "O", 24 },
            { "P", 25 }, { "Q", 16 }, { "R", 19 }, { "S", 31 }, { "T", 20 },
            { "U", 22 }, { "V", 47 }, { "W", 17 }, { "X", 45 }, { "Y", 21 },
            { "Z", 44 }, { ";", 39 }, { "'", 40 }, { ",", 51 }, { ".", 52 },
            { "/", 53 },
            { "1", 2 }, { "2", 3 }, { "3", 4 }, { "4", 5 }, { "5", 6 },
            { "6", 7 }, { "7", 8 }, { "8", 9 }, { "9", 10 }, { "0", 11 },
        };

        private static readonly Dictionary<string, ushort> MacVk = new()
        {
            { "Left", 0x7B }, { "Down", 0x7D }, { "Up", 0x7E }, { "Right", 0x7C },
            { "Space", 0x31 },
            { "A", 0x00 }, { "B", 0x0B }, { "C", 0x08 }, { "D", 0x02 }, { "E", 0x0E },
            { "F", 0x03 }, { "G", 0x05 }, { "H", 0x04 }, { "I", 0x22 }, { "J", 0x26 },
            { "K", 0x28 }, { "L", 0x25 }, { "M", 0x2E }, { "N", 0x2D }, { "O", 0x1F },
            { "P", 0x23 }, { "Q", 0x0C }, { "R", 0x0F }, { "S", 0x01 }, { "T", 0x11 },
            { "U", 0x20 }, { "V", 0x09 }, { "W", 0x0D }, { "X", 0x07 }, { "Y", 0x10 },
            { "Z", 0x06 }, { ";", 0x29 }, { "'", 0x27 }, { ",", 0x2B }, { ".", 0x2F },
            { "/", 0x2C },
            { "1", 0x12 }, { "2", 0x13 }, { "3", 0x14 }, { "4", 0x15 }, { "5", 0x17 },
            { "6", 0x16 }, { "7", 0x1A }, { "8", 0x1C }, { "9", 0x19 }, { "0", 0x1D },
        };

        /// <summary>Default key name for each lane index for a given key count.</summary>
        /// <remarks>Matches Leather Engine defaults for extra keys (5+). 4K keeps vanilla arrows.</remarks>
        private static readonly Dictionary<int, string[]> Defaults = new()
        {
            [1] = new[] { "Space" },
            [2] = new[] { "F", "J" },
            [3] = new[] { "F", "Space", "J" },
            [4] = new[] { "Left", "Down", "Up", "Right" },
            [5] = new[] { "D", "F", "Space", "J", "K" },
            [6] = new[] { "S", "D", "F", "J", "K", "L" },
            [7] = new[] { "S", "D", "F", "Space", "J", "K", "L" },
            [8] = new[] { "A", "S", "D", "F", "H", "J", "K", "L" },
            [9] = new[] { "A", "S", "D", "F", "Space", "H", "J", "K", "L" },
            [10] = new[] { "Q", "W", "E", "R", "V", "N", "U", "I", "O", "P" },
            [11] = new[] { "Q", "W", "E", "R", "V", "Space", "N", "U", "I", "O", "P" },
            [12] = new[] { "A", "S", "D", "F", "C", "V", "N", "M", "J", "K", "L", ";" },
            [13] = new[] { "A", "S", "D", "F", "C", "V", "Space", "N", "M", "J", "K", "L", ";" },
        };

        public static string[] DefaultNames(int keyCount)
        {
            if (Defaults.TryGetValue(keyCount, out var names))
                return names;
            var pads = new[] { "ASDF", "GZXCV", "JKL;", "GZXCV" };
            var result = new string[keyCount];
            int half = keyCount / 2;
            for (int i = 0; i < keyCount; i++)
            {
                if (i == half && keyCount % 2 == 1)
                    result[i] = "Space";
                else if (i < half)
                {
                    int padIdx = i < 4 ? 0 : 1;
                    int off = i < 4 ? i : i - 4;
                    int len = pads[padIdx].Length;
                    result[i] = pads[padIdx][off % len].ToString();
                }
                else
                {
                    int off = i - (keyCount % 2 == 1 ? half + 1 : half);
                    int padIdx = off < 4 ? 2 : 3;
                    int adj = off < 4 ? off : off - 4;
                    int len = pads[padIdx].Length;
                    result[i] = pads[padIdx][adj % len].ToString();
                }
            }
            return result;
        }

        public static ushort[] ToWinScans(string[] names, out bool[] extended)
        {
            var scans = new ushort[names.Length];
            var ext = new bool[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                scans[i] = WinScan.TryGetValue(names[i], out var s) ? s : (ushort)0x1E; // fallback A
                ext[i] = WinExtended.Contains(names[i]);
            }
            extended = ext;
            return scans;
        }

        public static ushort[] ToLinuxEv(string[] names)
        {
            var codes = new ushort[names.Length];
            for (int i = 0; i < names.Length; i++)
                codes[i] = LinuxEv.TryGetValue(names[i], out var c) ? c : (ushort)30; // fallback A
            return codes;
        }

        public static ushort[] ToMacVk(string[] names)
        {
            var codes = new ushort[names.Length];
            for (int i = 0; i < names.Length; i++)
                codes[i] = MacVk.TryGetValue(names[i], out var c) ? c : (ushort)0x00; // fallback A
            return codes;
        }

        /// <summary>
        /// Converts key names to platform-specific integer codes for the current OS.
        /// For Windows, the high bit (0x10000) indicates the EXTENDEDKEY scancode flag.
        /// </summary>
        public static int[] ToPlatformCodes(string[] names)
        {
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                var scans = ToWinScans(names, out var ext);
                var codes = new int[names.Length];
                for (int i = 0; i < names.Length; i++)
                    codes[i] = scans[i] | (ext[i] ? 0x10000 : 0);
                return codes;
            }
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux))
            {
                var ev = ToLinuxEv(names);
                var codes = new int[names.Length];
                for (int i = 0; i < names.Length; i++)
                    codes[i] = ev[i];
                return codes;
            }
            // macOS fallback
            var vk = ToMacVk(names);
            var mc = new int[names.Length];
            for (int i = 0; i < names.Length; i++)
                mc[i] = vk[i];
            return mc;
        }
    }
}
