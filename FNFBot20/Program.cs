using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FNFBot20
{
    static class Program
    {
        // Raise the system timer resolution to 1ms. Without this, Windows defaults to
        // ~15.6ms, so Thread.Sleep(1) in the play loop actually sleeps ~15ms — which makes
        // the bot check for due notes only ~64x/sec and press notes late / in clumps.
        [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint uMilliseconds);
        [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint uMilliseconds);

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            timeBeginPeriod(1);
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new Form1());
            }
            finally
            {
                timeEndPeriod(1);
            }
        }
    }
}