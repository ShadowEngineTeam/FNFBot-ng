using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using FNFDataManager.Assets;
using FridayNightFunkin;

namespace FNFBot20
{
    public class RenderBot
    {
        public static float stepCrochet { get; set; }

        private volatile bool _renderPending;

        public RenderBot(float bpm)
        {
            var crochet = (float)(60 / bpm * 1000);
            stepCrochet = (float) (crochet / 4);
        }

        public void ListNotes(List<FNFSong.FNFNote> notes)
        {
            Panel field = Form1.pnlField;
            if (field == null || field.IsDisposed || !field.IsHandleCreated)
                return;

            if (_renderPending)
                return;
            _renderPending = true;

            try
            {
                if (field.InvokeRequired)
                    field.BeginInvoke((MethodInvoker)(() => Draw(field, notes)));
                else
                    Draw(field, notes);
            }
            catch
            {
                _renderPending = false;
            }
        }

        private void Draw(Panel field, List<FNFSong.FNFNote> notes)
        {
            try
            {
                field.SuspendLayout();

                for (int i = field.Controls.Count - 1; i >= 0; i--)
                {
                    Control c = field.Controls[i];
                    field.Controls.RemoveAt(i);
                    c.Dispose();
                }

                int h = field.Height > 0 ? field.Height : 1;

                foreach (FNFSong.FNFNote n in notes)
                {
                    int dir = (int) n.Type % 4;
                    int x = dir * 32;
                    int y = (int) (Math.Floor(remapToRange((float) n.Time, 0, 16 * stepCrochet, 0, h)) % h);

                    if (n.Length > 0)
                    {
                        int len = (int) remapToRange((float) n.Length, 0, 16 * stepCrochet, 0, h);
                        if (len < 4) len = 4;

                        var hold = new Panel
                        {
                            Size = new Size(14, len),
                            Location = new Point(x + 9, y),
                            BackgroundImage = TrailImage(dir),
                            BackgroundImageLayout = ImageLayout.Stretch
                        };
                        field.Controls.Add(hold);
                        hold.SendToBack();
                    }

                    UserControl arrow = CreateArrow(dir);
                    arrow.Location = new Point(x, y);
                    field.Controls.Add(arrow);
                    arrow.BringToFront();
                }

                field.ResumeLayout();
            }
            catch (Exception e)
            {
                Form1.WriteToConsole("Failed to render notes.\n" + e.Message);
            }
            finally
            {
                _renderPending = false;
            }
        }

        private static UserControl CreateArrow(int dir)
        {
            switch (dir)
            {
                case 0:  return new LeftArrow();
                case 1:  return new DownArrow();
                case 2:  return new UpArrow();
                default: return new RightArrow();
            }
        }

        private static Image TrailImage(int dir)
        {
            switch (dir)
            {
                case 0:  return Properties.Resources.purpleTrail; // left
                case 1:  return Properties.Resources.blueTrail;   // down
                case 2:  return Properties.Resources.greenTrail;  // up
                default: return Properties.Resources.redTrail;    // right
            }
        }

        public static float remapToRange(float value, float start1, float stop1, float start2, float stop2) // stolen from https://github.com/HaxeFlixel/flixel/blob/b38c74b85170d7457353881713a796310187ddd2/flixel/math/FlxMath.hx#L285
        {
            return start2 + (value - start1) * ((stop2 - start2) / (stop1 - start1));
        }
    }
}
