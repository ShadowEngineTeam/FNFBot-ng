using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FNFBot.Core;
using FNFBot.Core.Input;

namespace FNFBot.App
{
    public partial class MainWindow : Window
    {
        private readonly BotEngine _engine;
        private readonly DispatcherTimer _labelTimer;

        public MainWindow()
        {
            InitializeComponent();

            _engine = new BotEngine();
            Field.Engine = _engine;

            _engine.Log += s => Dispatcher.UIThread.Post(() => AppendLog(s));
            _engine.Loaded += () => Dispatcher.UIThread.Post(UpdateLabels);
            _engine.SettingsChanged += () => Dispatcher.UIThread.Post(UpdateLabels);

            CheckBtn.Click += (_, _) => ScanFolder();
            BrowseBtn.Click += async (_, _) => await BrowseFolder();
            SongTree.DoubleTapped += (_, _) => PlaySelected();
            RenderCheck.IsCheckedChanged += (_, _) => Field.RenderEnabled = RenderCheck.IsChecked == true;
            SettingsBtn.Click += async (_, _) => await ShowSettingsDialog();

            Closing += (_, _) => _engine.Dispose();
            // Global hotkey listener (WindowsHotkeyListener) already handles F1-F4 via
            // GetAsyncKeyState polling. A window-level handler would double-fire.

            _labelTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _labelTimer.Tick += (_, _) =>
            {
                TimeLabel.Text = "Time: " + (_engine.IsPlaying ? (_engine.CurrentTimeMs / 1000.0).ToString("0.00") : "0");
            };
            _labelTimer.Start();

            UpdateLabels();
            AppendLog("Ready. Pick a folder, Check Dir, double-click a chart, then press F2 in-game.");

            _engine.Start();
        }

        private void UpdateLabels()
        {
            OffsetLabel.Text = "Offset: " + _engine.Settings.Offset;
        }

        private void AppendLog(string text)
        {
            string line = "[" + DateTime.Now.ToShortTimeString() + "] " + text + "\n";
            string current = LogBox.Text ?? "";
            current += line;
            if (current.Length > 16000)
                current = current.Substring(current.Length - 8000);
            LogBox.Text = current;
            LogBox.CaretIndex = current.Length;
        }

        private void ScanFolder()
        {
            try
            {
                var songs = ChartLibrary.Scan(DirBox.Text);
                SongTree.Items.Clear();

                foreach (var song in songs)
                {
                    var parent = new TreeViewItem { Header = song.Name };
                    foreach (var chart in song.Charts)
                        parent.Items.Add(new TreeViewItem { Header = chart.Label, Tag = chart });
                    SongTree.Items.Add(parent);
                }

                int count = ChartLibrary.CountCharts(songs);
                AppendLog(count == 0
                    ? "No charts found. Point at the engine/mod folder, an 'assets' folder, or a 'data' folder."
                    : $"Found {count} chart(s).");
            }
            catch (Exception e)
            {
                AppendLog("Failed to retrieve data.\n" + e.Message);
            }
        }

        private async System.Threading.Tasks.Task BrowseFolder()
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false });
            if (folders.Count > 0)
                DirBox.Text = folders[0].Path.LocalPath;
        }

        private void PlaySelected()
        {
            if (SongTree.SelectedItem is TreeViewItem tvi && tvi.Tag is ChartEntry chart)
            {
                AppendLog("Selecting " + chart.Label);
                _engine.Load(chart.Path, chart.Difficulty);
            }
        }

        private async System.Threading.Tasks.Task ShowSettingsDialog()
        {
            var s = _engine.Settings;
            var win = new Window
            {
                Title = "Settings",
                Width = 380,
                Height = 520,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Icon = Icon,
                CanResize = false
            };

            var panel = new StackPanel { Margin = new Thickness(12), Spacing = 6 };
            var offsetRow = MakeSliderRow("Offset", s.Offset, -200, 200);
            var failRow = MakeSliderRow("Fail count", s.FailCount, 0, 200);
            var pressMinRow = MakeSliderRow("Press min", s.PressMinMs, 1, 200);
            var pressMaxRow = MakeSliderRow("Press max", s.PressMaxMs, 1, 200);
            var holdMinRow = MakeSliderRow("Hold min", s.HoldMinMs, 0, 200);
            var holdMaxRow = MakeSliderRow("Hold max", s.HoldMaxMs, 0, 200);
            var autoFailBox = new CheckBox { Content = "Auto fail (miss notes)", IsChecked = s.AutoFail, Margin = new Thickness(0, 2, 0, 0) };
            var pressRateRow = MakeSliderRow("Press rate", s.PressRate, 0, 100);
            panel.Children.Add(offsetRow);
            panel.Children.Add(failRow);
            panel.Children.Add(pressMinRow);
            panel.Children.Add(pressMaxRow);
            panel.Children.Add(holdMinRow);
            panel.Children.Add(holdMaxRow);
            panel.Children.Add(autoFailBox);
            panel.Children.Add(pressRateRow);

            var saveBtn = new Button { Content = "Save", HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
            panel.Children.Add(saveBtn);

            win.Content = panel;

            saveBtn.Click += (_, _) =>
            {
                s.Offset = (int)((Slider)offsetRow.Children[1]).Value;
                s.FailCount = (int)((Slider)failRow.Children[1]).Value;
                s.PressMinMs = (int)((Slider)pressMinRow.Children[1]).Value;
                s.PressMaxMs = (int)((Slider)pressMaxRow.Children[1]).Value;
                s.HoldMinMs = (int)((Slider)holdMinRow.Children[1]).Value;
                s.HoldMaxMs = (int)((Slider)holdMaxRow.Children[1]).Value;
                s.AutoFail = autoFailBox.IsChecked == true;
                s.PressRate = (int)((Slider)pressRateRow.Children[1]).Value;
                _engine.ApplySettings();
                win.Close();
            };

            await win.ShowDialog(this);
        }

        private static Grid MakeSliderRow(string label, int val, int min, int max)
        {
            var slider = new Slider
            {
                Value = val,
                Minimum = min,
                Maximum = max,
                TickFrequency = 1,
                IsSnapToTickEnabled = true
            };
            var valText = new TextBlock { Text = val.ToString(), VerticalAlignment = VerticalAlignment.Center, Width = 30, HorizontalAlignment = HorizontalAlignment.Right };
            slider.ValueChanged += (_, _) => valText.Text = ((int)slider.Value).ToString();

            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 0, 0, 4) };
            g.Children.Add(new TextBlock { Text = label + ":", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            Grid.SetColumn(slider, 1);
            slider.Margin = new Thickness(0, 0, 6, 0);
            g.Children.Add(slider);
            Grid.SetColumn(valText, 2);
            g.Children.Add(valText);
            return g;
        }

    }
}
