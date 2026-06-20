using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FNFBot.Core;
using FNFBot.Core.Input;
using FNFBot.Core.Memory;

namespace FNFBot.App
{
    public partial class MainWindow : Window
    {
        private readonly BotEngine _engine;
        private readonly DispatcherTimer _labelTimer;
        private bool _closing;

        public MainWindow()
        {
            InitializeComponent();

            _engine = new BotEngine();
            Field.Engine = _engine;

            _engine.Log += s => Dispatcher.UIThread.Post(() => { if (!_closing) AppendLog(s); });
            _engine.Loaded += () => Dispatcher.UIThread.Post(() => { if (!_closing) UpdateLabels(); });
            _engine.SettingsChanged += () => Dispatcher.UIThread.Post(() => { if (!_closing) UpdateLabels(); });
            _engine.MemoryStatusChanged += () => Dispatcher.UIThread.Post(() => { if (!_closing) UpdateAttachLabel(); });

            CheckBtn.Click += (_, _) => ScanFolder();
            BrowseBtn.Click += async (_, _) => await BrowseFolder();
            SongTree.DoubleTapped += (_, _) => PlaySelected();
            RenderCheck.IsCheckedChanged += (_, _) => Field.RenderEnabled = RenderCheck.IsChecked == true;
            SettingsBtn.Click += async (_, _) => await ShowSettingsDialog();
            KeysBtn.Click += async (_, _) => await ShowKeyConfigDialog();
            AttachBtn.Click += async (_, _) => await ShowAttachDialog();
            OppBtn.Click += (_, _) => ToggleOpponent();

            Closing += (_, _) =>
            {
                _closing = true;
                _labelTimer.Stop();
                _engine.Dispose();
            };
            // Global hotkey listener (WindowsHotkeyListener) already handles F1-F4 via
            // GetAsyncKeyState polling. A window-level handler would double-fire.

            _labelTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _labelTimer.Tick += (_, _) =>
            {
                if (!_closing)
                    TimeLabel.Text = "Time: " + (_engine.IsPlaying ? (_engine.CurrentTimeMs / 1000.0).ToString("0.00") : "0");
            };
            _labelTimer.Start();

            UpdateLabels();
            AppendLog("Ready. Pick a folder, Check Dir, double-click a chart. Then either press F2 in-game, or click \"Attach Game\" to auto-follow the song's countdown.");

            _engine.Start();
        }

        private void UpdateLabels()
        {
            OffsetLabel.Text = "Offset: " + _engine.Settings.Offset;
            UpdateAttachLabel();
        }

        private void UpdateAttachLabel()
        {
            if (!_engine.MemAttached)
            {
                AttachLabel.Text = "Not attached (manual F2)";
                AttachLabel.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x7A));
            }
            else if (_engine.MemActive)
            {
                AttachLabel.Text = "Following: " + _engine.AttachedProcess;
                AttachLabel.Foreground = new SolidColorBrush(Color.FromRgb(0x7C, 0xF9, 0x6E));
            }
            else
            {
                AttachLabel.Text = "Attached: " + _engine.AttachedProcess + " (scanning...)";
                AttachLabel.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0x6A));
            }
        }

        private void ToggleOpponent()
        {
            _engine.OpponentMode = !_engine.OpponentMode;
            OppBtn.Content = _engine.OpponentMode ? "Mode: Opponent" : "Mode: Player";
            AppendLog(_engine.OpponentMode ? "Opponent mode: playing opponent notes." : "Player mode: playing player notes.");
            if (!string.IsNullOrEmpty(_chartPath))
                _engine.Load(_chartPath, _chartDifficulty);
        }

        private string _chartPath;
        private string _chartDifficulty;

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
                _chartPath = chart.Path;
                _chartDifficulty = chart.Difficulty;
                _engine.Load(_chartPath, _chartDifficulty);
            }
        }

        private async System.Threading.Tasks.Task ShowAttachDialog()
        {
            if (!ProcessMemory.IsSupported)
            {
                AppendLog("Attaching to a game isn't supported on this OS; use manual F2.");
                return;
            }

            var win = new Window
            {
                Title = "Attach Game",
                Width = 480,
                Height = 520,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Icon = Icon,
                CanResize = false
            };

            var root = new Grid { Margin = new Thickness(12), RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto,Auto") };

            root.Children.Add(new TextBlock
            {
                Text = "Pick the running FNF process and select its engine type. The bot finds Conductor.songPosition and plays only when a song's countdown starts.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });

            var list = new ListBox { Margin = new Thickness(0, 0, 0, 6) };
            Grid.SetRow(list, 1);
            root.Children.Add(list);

            var engineBox = new ComboBox { Margin = new Thickness(0, 0, 0, 8) };
            Grid.SetRow(engineBox, 2);
            engineBox.ItemsSource = new[] { "Auto", "Psych / Shadow", "Codename", "Kade", "NightmareVision", "Troll", "V-Slice", "CDev Engine", "Generic" };
            engineBox.SelectedIndex = 0;
            root.Children.Add(engineBox);

            void Populate()
            {
                var items = BotEngine.ListProcesses();
                list.ItemsSource = items;
                AppendLog($"Found {items.Count} window(s) to attach to.");
            }

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 6 };
            Grid.SetRow(buttons, 3);
            var refreshBtn = new Button { Content = "Refresh" };
            var detachBtn = new Button { Content = "Detach" };
            var attachBtn = new Button { Content = "Attach" };
            var cancelBtn = new Button { Content = "Close" };
            buttons.Children.Add(refreshBtn);
            buttons.Children.Add(detachBtn);
            buttons.Children.Add(attachBtn);
            buttons.Children.Add(cancelBtn);
            root.Children.Add(buttons);

            win.Content = root;

            EngineType MapEngine(int idx) => idx switch
            {
                1 => EngineType.Psych,
                2 => EngineType.Codename,
                3 => EngineType.Kade,
                4 => EngineType.NightmareVision,
                5 => EngineType.Troll,
                6 => EngineType.VSlice,
                7 => EngineType.CDev,
                8 => EngineType.Generic,
                _ => EngineType.Auto
            };

            void DoAttach()
            {
                if (list.SelectedItem is ProcessPick pick)
                {
                    _engine.AttachTo(pick.Pid, pick.Name, MapEngine(engineBox.SelectedIndex));
                    win.Close();
                }
                else
                {
                    AppendLog("Select a process first.");
                }
            }

            refreshBtn.Click += (_, _) => Populate();
            attachBtn.Click += (_, _) => DoAttach();
            detachBtn.Click += (_, _) => { _engine.DetachMemory(); win.Close(); };
            cancelBtn.Click += (_, _) => win.Close();
            list.DoubleTapped += (_, _) => DoAttach();

            Populate();
            await win.ShowDialog(this);
        }

        private async System.Threading.Tasks.Task ShowKeyConfigDialog()
        {
            var s = _engine.Settings;
            var keyNames = (string[])s.KeyNames.Clone();

            var win = new Window
            {
                Title = "Configure Keys",
                Width = 400,
                Height = 480,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Icon = Icon,
                CanResize = false
            };

            // Outer stack: lane list in ScrollViewer, then Close button fixed at bottom
            var outer = new DockPanel { Margin = new Thickness(12) };

            var saveBtn = new Button
            {
                Content = "Close",
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 8, 0, 0)
            };
            DockPanel.SetDock(saveBtn, Dock.Bottom);
            outer.Children.Add(saveBtn);

            var scrollContent = new StackPanel { Spacing = 8 };
            var scroll = new ScrollViewer { Content = scrollContent };
            outer.Children.Add(scroll);

            // Lane count row
            var countRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto") };
            countRow.Children.Add(new TextBlock { Text = "Lane count:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            var countBox = new ComboBox { Width = 80 };
            for (int i = 1; i <= 13; i++) countBox.Items.Add(i);
            int initialIdx = keyNames.Length - 1;
            if (initialIdx < 0) initialIdx = 0;
            if (initialIdx >= countBox.Items.Count) initialIdx = countBox.Items.Count - 1;
            countBox.SelectedIndex = initialIdx;
            Grid.SetColumn(countBox, 1);
            countRow.Children.Add(countBox);
            scrollContent.Children.Add(countRow);

            scrollContent.Children.Add(new TextBlock
            {
                Text = "Click a lane, then press the key to bind.",
                TextWrapping = TextWrapping.Wrap
            });

            StackPanel listPanel = null;
            TextBlock[] labels = null;
            int capturingLane = -1;

            void Rebuild(int count, bool resetToDefault)
            {
                capturingLane = -1;
                if (listPanel != null) scrollContent.Children.Remove(listPanel);

                if (resetToDefault || keyNames.Length != count)
                    keyNames = KeyMap.DefaultNames(count);

                labels = new TextBlock[count];
                listPanel = new StackPanel { Spacing = 4 };

                for (int i = 0; i < count; i++)
                {
                    int idx = i;
                    var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
                    row.Children.Add(new TextBlock
                    {
                        Text = $"Lane {idx}:",
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 12, 0)
                    });

                    var lbl = new TextBlock
                    {
                        Text = keyNames[idx],
                        Width = 120,
                        Padding = new Thickness(8, 4),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    labels[idx] = lbl;
                    Grid.SetColumn(lbl, 1);
                    row.Children.Add(lbl);
                    var border = new Border { Child = row, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(6) };

                    border.PointerPressed += (_, _) =>
                    {
                        capturingLane = idx;
                        labels[idx].Text = "... press a key ...";
                        labels[idx].Foreground = Brushes.Orange;
                    };

                    listPanel.Children.Add(border);
                }

                scrollContent.Children.Add(listPanel);
            }

            Rebuild(keyNames.Length, resetToDefault: false);

            countBox.SelectionChanged += (_, _) =>
            {
                if (countBox.SelectedItem is int c) Rebuild(c, resetToDefault: true);
            };

            win.Content = outer;

            win.KeyDown += (_, e) =>
            {
                if (capturingLane < 0) return;
                string name = KeyNameFromAvalonia(e.Key);
                if (name != null)
                {
                    keyNames[capturingLane] = name;
                    labels[capturingLane].Text = name;
                    labels[capturingLane].Foreground = Brushes.White;
                    capturingLane = -1;
                }
                e.Handled = true;
            };

            saveBtn.Click += (_, _) =>
            {
                s.KeyNames = keyNames;
                _engine.ApplyKeyMapping();
                s.Save();
                AppendLog("Key mapping saved.");
                win.Close();
            };

            await win.ShowDialog(this);
        }

        private static string KeyNameFromAvalonia(Avalonia.Input.Key key)
        {
            return key switch
            {
                Avalonia.Input.Key.Left => "Left",
                Avalonia.Input.Key.Down => "Down",
                Avalonia.Input.Key.Up => "Up",
                Avalonia.Input.Key.Right => "Right",
                Avalonia.Input.Key.Space => "Space",
                Avalonia.Input.Key.A => "A",
                Avalonia.Input.Key.B => "B",
                Avalonia.Input.Key.C => "C",
                Avalonia.Input.Key.D => "D",
                Avalonia.Input.Key.E => "E",
                Avalonia.Input.Key.F => "F",
                Avalonia.Input.Key.G => "G",
                Avalonia.Input.Key.H => "H",
                Avalonia.Input.Key.I => "I",
                Avalonia.Input.Key.J => "J",
                Avalonia.Input.Key.K => "K",
                Avalonia.Input.Key.L => "L",
                Avalonia.Input.Key.M => "M",
                Avalonia.Input.Key.N => "N",
                Avalonia.Input.Key.O => "O",
                Avalonia.Input.Key.P => "P",
                Avalonia.Input.Key.Q => "Q",
                Avalonia.Input.Key.R => "R",
                Avalonia.Input.Key.S => "S",
                Avalonia.Input.Key.T => "T",
                Avalonia.Input.Key.U => "U",
                Avalonia.Input.Key.V => "V",
                Avalonia.Input.Key.W => "W",
                Avalonia.Input.Key.X => "X",
                Avalonia.Input.Key.Y => "Y",
                Avalonia.Input.Key.Z => "Z",
                Avalonia.Input.Key.D0 => "0",
                Avalonia.Input.Key.D1 => "1",
                Avalonia.Input.Key.D2 => "2",
                Avalonia.Input.Key.D3 => "3",
                Avalonia.Input.Key.D4 => "4",
                Avalonia.Input.Key.D5 => "5",
                Avalonia.Input.Key.D6 => "6",
                Avalonia.Input.Key.D7 => "7",
                Avalonia.Input.Key.D8 => "8",
                Avalonia.Input.Key.D9 => "9",
                Avalonia.Input.Key.OemSemicolon => ";",
                Avalonia.Input.Key.OemComma => ",",
                Avalonia.Input.Key.OemPeriod => ".",
                Avalonia.Input.Key.OemQuestion => "/",
                Avalonia.Input.Key.OemQuotes => "'",
                _ => null
            };
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
