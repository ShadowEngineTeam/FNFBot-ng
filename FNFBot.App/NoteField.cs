using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using FNFBot.Core;

namespace FNFBot.App
{
    /// <summary>
    /// Upscroll note visualizer, drawn directly with Avalonia's DrawingContext and refreshed
    /// on its own 60 fps timer — fully decoupled from the bot's timing thread.
    /// </summary>
    public class NoteField : Control
    {
        public BotEngine Engine { get; set; }
        public bool RenderEnabled { get; set; } = true;

        private static readonly IBrush Background = new SolidColorBrush(Color.FromRgb(20, 20, 30));

        // FNF arrow colours, indexed by direction (0=L,1=D,2=U,3=R).
        private static readonly IBrush[] HeadBrush =
        {
            new SolidColorBrush(Color.FromRgb(0xC2, 0x4B, 0x99)), // left  - purple
            new SolidColorBrush(Color.FromRgb(0x00, 0xC0, 0xFF)), // down  - cyan
            new SolidColorBrush(Color.FromRgb(0x12, 0xFA, 0x05)), // up    - green
            new SolidColorBrush(Color.FromRgb(0xF9, 0x39, 0x3F)), // right - red
        };

        private static readonly IBrush[] TrailBrush =
        {
            new SolidColorBrush(Color.FromArgb(0x99, 0xC2, 0x4B, 0x99)),
            new SolidColorBrush(Color.FromArgb(0x99, 0x00, 0xC0, 0xFF)),
            new SolidColorBrush(Color.FromArgb(0x99, 0x12, 0xFA, 0x05)),
            new SolidColorBrush(Color.FromArgb(0x99, 0xF9, 0x39, 0x3F)),
        };

        private const double Lane = 36; // px per lane
        private const double Size = 32; // arrow size

        public NoteField()
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            timer.Tick += (_, _) => InvalidateVisual();
            timer.Start();
        }

        public override void Render(DrawingContext context)
        {
            double w = Bounds.Width, h = Bounds.Height;
            context.DrawRectangle(Background, null, new Rect(0, 0, w, h));

            var eng = Engine;
            if (!RenderEnabled || eng == null)
                return;

            var notes = eng.Notes;
            if (notes == null || notes.Count == 0)
                return;

            double cur = eng.CurrentTimeMs;
            double secLen = eng.SectionLenMs;
            if (secLen <= 0 || h <= 0)
                return;

            double speed = eng.Speed <= 0 ? 1 : eng.Speed;
            double window = secLen / speed;

            foreach (var n in notes)
            {
                double timeDiff = n.Time - cur;
                if (timeDiff > window * 2) break; // notes are time-sorted

                double endDiff = (n.Length > 0 ? n.Time + n.Length : n.Time) - cur;
                if (endDiff < -window * 0.25) continue;

                int dir = ((int)n.Type) % 4;
                double x = 4 + dir * Lane;
                double y = (timeDiff / window) * h;

                if (n.Length > 0)
                {
                    double tailY = ((n.Time + n.Length - cur) / window) * h;
                    double top = Math.Min(y, tailY) + Size / 2;
                    double bottom = Math.Max(y, tailY) + Size / 2;
                    double len = Math.Max(4, bottom - top);
                    context.DrawRectangle(TrailBrush[dir], null, new Rect(x + 10, top, 12, len));
                }

                context.DrawRectangle(HeadBrush[dir], null, new RoundedRect(new Rect(x, y, Size, Size), 6));
            }
        }
    }
}
