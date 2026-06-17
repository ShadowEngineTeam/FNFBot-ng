using System;
using Avalonia.Controls;
using Avalonia.Input;
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

            Closing += (_, _) => _engine.Dispose();
            KeyDown += MainWindow_KeyDown;

            _labelTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _labelTimer.Tick += (_, _) =>
            {
                TimeLabel.Text = "Time: " + (_engine.IsPlaying ? (_engine.CurrentTimeMs / 1000.0).ToString("0.00") : "0");
            };
            _labelTimer.Start();

            UpdateLabels();
            AppendLog("Ready. Pick a folder, Check Dir, double-click a chart, then press F1 in-game.");

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

        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            BotHotkey? hk = e.Key switch
            {
                Key.F1 => BotHotkey.TogglePlay,
                Key.F2 => BotHotkey.OffsetUp,
                Key.F3 => BotHotkey.OffsetDown,
                Key.F4 => BotHotkey.PressUp,
                Key.F5 => BotHotkey.PressDown,
                Key.F6 => BotHotkey.HoldUp,
                Key.F7 => BotHotkey.HoldDown,
                _ => null
            };

            if (hk.HasValue)
            {
                e.Handled = true;
                // Route through the engine's hotkey handler.
                _engine.HandleHotkey(hk.Value);
            }
        }
    }
}
