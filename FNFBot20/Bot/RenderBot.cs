using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using FridayNightFunkin;

namespace FNFBot20
{
    public class RenderBot
    {
        private volatile List<FNFSong.FNFNote> _notes;
        private float _speed = 1;
        private bool _hooked;
        private System.Windows.Forms.Timer _timer;

        public void SetScrollSpeed(float speed)
        {
            _speed = speed > 0 ? speed : 1;
        }

        // Feed the renderer the whole (time-sorted) note list once. The Paint handler culls
        // to the visible time window itself, so it never needs per-section updates from the
        // play thread — that keeps rendering fully decoupled from note timing.
        public void SetNotes(List<FNFSong.FNFNote> notes)
        {
            _notes = notes;
            EnsureHooked();
        }

        private void EnsureHooked()
        {
            Panel field = Form1.pnlField;
            if (field == null || field.IsDisposed || !field.IsHandleCreated || _hooked)
                return;

            _hooked = true;
            try
            {
                if (field.InvokeRequired)
                    field.Invoke((MethodInvoker)(() => HookField(field)));
                else
                    HookField(field);
            }
            catch { }
        }

        // One-time setup, always on the UI thread.
        private void HookField(Panel field)
        {
            // Double-buffer the panel so the per-frame clear+redraw doesn't flicker.
            // DoubleBuffered is protected, so set it via reflection on the instance.
            typeof(Control)
                .GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(field, true);

            field.Paint += OnFieldPaint;
            field.BackColor = Color.FromArgb(20, 20, 30);

            for (int i = field.Controls.Count - 1; i >= 0; i--)
            {
                var c = field.Controls[i];
                field.Controls.RemoveAt(i);
                c.Dispose();
            }
            field.BackgroundImage = null;

            // Steady ~60 fps repaint, independent of the play loop. The Paint handler reads
            // the live song clock, so this alone produces smooth scrolling.
            _timer = new System.Windows.Forms.Timer { Interval = 16 };
            _timer.Tick += (s, e) =>
            {
                if (!Form1.Rendering)
                    return;
                if (Bot.Playing && Bot.watch != null)
                    Form1.watchTime.Text = "Time: " + (Bot.watch.Elapsed.TotalMilliseconds / 1000.0).ToString("0.00");
                field.Invalidate();
            };
            _timer.Start();
        }

        private void OnFieldPaint(object sender, PaintEventArgs e)
        {
            var notes = _notes;
            var g = e.Graphics;
            var field = (Panel)sender;

            g.Clear(field.BackColor);

            if (!Form1.Rendering || notes == null || notes.Count == 0)
                return;

            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            double currentTime = Bot.watch?.Elapsed.TotalMilliseconds ?? 0;
            double sectionLenMs = Bot.SectionLenMs;
            if (sectionLenMs <= 0) return;

            double visibleWindow = sectionLenMs / _speed;
            int h = field.Height > 0 ? field.Height : 1;

            // _notes is sorted by time, so once a head is far enough in the future every
            // later note is too — we can stop scanning.
            foreach (var n in notes)
            {
                double timeDiff = n.Time - currentTime;
                if (timeDiff > visibleWindow * 2) break;

                double endDiff = (n.Length > 0 ? n.Time + n.Length : n.Time) - currentTime;
                if (endDiff < -visibleWindow * 0.25) continue;

                int dir = (int)n.Type % 4;
                int x = dir * 32;

                // Upscroll: notes scroll bottom→top, hit position at top (y=0).
                int y = (int)((timeDiff / visibleWindow) * h);

                if (n.Length > 0)
                {
                    // The sustain runs from the head (n.Time) to n.Time + n.Length, which is
                    // later in time and therefore *below* the head on an upscroll field.
                    double tailDiff = (n.Time + n.Length) - currentTime;
                    int tailY = (int)((tailDiff / visibleWindow) * h);

                    int trailTop = Math.Min(y, tailY);
                    int trailBottom = Math.Max(y, tailY);
                    int len = trailBottom - trailTop;
                    if (len < 4) len = 4;

                    if (trailBottom >= -32 && trailTop <= h + 32)
                    {
                        var trail = TrailImage(dir);
                        if (trail != null)
                            g.DrawImage(trail, x + 9, trailTop, 14, len); // upright, no flip
                    }
                }

                // Head arrow on top of the tail.
                if (y >= -32 && y <= h + 32)
                {
                    var arrow = ArrowImage(dir);
                    if (arrow != null)
                        g.DrawImage(arrow, x, y, 32, 32);
                }
            }
        }

        private static Image ArrowImage(int dir)
        {
            switch (dir)
            {
                case 0: return Properties.Resources.LeftArrow;
                case 1: return Properties.Resources.DownArrow;
                case 2: return Properties.Resources.UpArrow;
                default: return Properties.Resources.RightArrow;
            }
        }

        private static Image TrailImage(int dir)
        {
            switch (dir)
            {
                case 0: return Properties.Resources.purpleTrail;
                case 1: return Properties.Resources.blueTrail;
                case 2: return Properties.Resources.greenTrail;
                default: return Properties.Resources.redTrail;
            }
        }
    }
}
