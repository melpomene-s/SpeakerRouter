namespace SpeakerRouter;

internal sealed class MainForm : Form
{
    private readonly AppSettings settings;
    private readonly RouterService router;
    private readonly DataGridView grid = new();
    private readonly Label statusLabel = new();
    private readonly CheckBox routingBox = new();
    private readonly CheckBox startupBox = new();
    private readonly NumericUpDown intervalBox = new();
    private IReadOnlyList<AudioDeviceInfo> devices = Array.Empty<AudioDeviceInfo>();
    private IReadOnlyList<MonitorInfo> monitors = Array.Empty<MonitorInfo>();
    private bool syncing;

    public event EventHandler? CheckStateChanged;

    public MainForm(AppSettings settings, RouterService router)
    {
        this.settings = settings;
        this.router = router;

        Text = "SpeakerRouter";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(720, 390);
        Size = new Size(820, 460);
        ShowInTaskbar = true;
        Icon = AppIcon.Load();
        Font = new Font("Microsoft YaHei UI", 9F);

        BuildUi();
        LoadData();
        SyncChecks();
        router.StatusChanged += Router_StatusChanged;
    }

    public void SyncChecks()
    {
        syncing = true;
        routingBox.Checked = settings.RoutingEnabled;
        startupBox.Checked = StartupManager.IsEnabled();
        intervalBox.Value = Math.Clamp(settings.ScanIntervalMs, 250, 5000);
        syncing = false;
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            RowCount = 4,
            ColumnCount = 1
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var switches = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true
        };

        routingBox.Text = "启用自动检测并切换";
        routingBox.AutoSize = true;
        routingBox.CheckedChanged += (_, _) =>
        {
            if (syncing) return;
            settings.RoutingEnabled = routingBox.Checked;
            router.RoutingEnabled = settings.RoutingEnabled;
            SaveAndNotify();
        };

        startupBox.Text = "开机启动";
        startupBox.AutoSize = true;
        startupBox.CheckedChanged += (_, _) =>
        {
            if (syncing) return;
            StartupManager.SetEnabled(startupBox.Checked);
            SaveAndNotify();
        };

        intervalBox.Minimum = 250;
        intervalBox.Maximum = 5000;
        intervalBox.Increment = 100;
        intervalBox.Width = 78;
        intervalBox.ValueChanged += (_, _) =>
        {
            if (syncing) return;
            settings.ScanIntervalMs = (int)intervalBox.Value;
            router.ApplyInterval();
            SaveAndNotify();
        };

        switches.Controls.AddRange(new Control[]
        {
            routingBox,
            startupBox,
            new Label { Text = "间隔(ms)", AutoSize = true, Padding = new Padding(18, 4, 0, 0) },
            intervalBox
        });

        grid.Dock = DockStyle.Fill;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.BackgroundColor = SystemColors.Window;
        grid.BorderStyle = BorderStyle.FixedSingle;
        grid.RowHeadersVisible = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = false;
        grid.CellValueChanged += (_, _) =>
        {
            if (!syncing)
            {
                ReadGridToSettings();
            }
        };
        grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (grid.IsCurrentCellDirty)
            {
                grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };
        grid.DataError += (_, _) => { };

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft
        };

        var saveButton = new Button { Text = "保存绑定", AutoSize = true };
        saveButton.Click += (_, _) =>
        {
            ReadGridToSettings();
            SettingsStore.Save(settings);
            statusLabel.Text = "绑定已保存";
        };

        var scanButton = new Button { Text = "立即检测", AutoSize = true };
        scanButton.Click += (_, _) => router.ScanOnce();

        var refreshButton = new Button { Text = "刷新设备/屏幕", AutoSize = true };
        refreshButton.Click += (_, _) => LoadData();

        actions.Controls.AddRange(new Control[] { saveButton, scanButton, refreshButton });

        statusLabel.Dock = DockStyle.Fill;
        statusLabel.AutoSize = true;
        statusLabel.MaximumSize = new Size(760, 0);
        statusLabel.Text = "就绪";

        root.Controls.Add(switches, 0, 0);
        root.Controls.Add(grid, 0, 1);
        root.Controls.Add(actions, 0, 2);
        root.Controls.Add(statusLabel, 0, 3);
        Controls.Add(root);
    }

    private void LoadData()
    {
        if (!syncing && grid.Rows.Count > 0)
        {
            grid.EndEdit();
            ReadGridToSettings();
            SettingsStore.Save(settings);
        }

        syncing = true;
        var loadError = string.Empty;
        try
        {
            try
            {
                devices = AddSavedMissingDevices(NativeAudio.GetPlaybackDevices());
                monitors = NativeWindows.GetMonitors();
            }
            catch (Exception ex)
            {
                devices = AddSavedMissingDevices(Array.Empty<AudioDeviceInfo>());
                monitors = NativeWindows.GetMonitors();
                loadError = $"加载音频设备失败：{ex.Message}";
            }

            grid.Columns.Clear();
            grid.Rows.Clear();

            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "屏幕",
                ReadOnly = true,
                FillWeight = 32
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "位置",
                ReadOnly = true,
                FillWeight = 24
            });
            grid.Columns.Add(new DataGridViewComboBoxColumn
            {
                HeaderText = "绑定播放设备",
                DataSource = devices,
                DisplayMember = nameof(AudioDeviceInfo.Name),
                ValueMember = nameof(AudioDeviceInfo.Id),
                FlatStyle = FlatStyle.Flat,
                FillWeight = 44
            });

            foreach (var monitor in monitors)
            {
                var rowIndex = grid.Rows.Add();
                var row = grid.Rows[rowIndex];
                row.Tag = monitor;
                row.Cells[0].Value = monitor.DisplayName;
                row.Cells[1].Value = $"{monitor.Bounds.X},{monitor.Bounds.Y}  {monitor.Bounds.Width}x{monitor.Bounds.Height}";
                row.Cells[2].Value = PickDeviceForMonitor(monitor);
            }

            statusLabel.Text = string.IsNullOrWhiteSpace(loadError)
                ? devices.Count == 0 ? "未找到可用播放设备" : "设备和屏幕已刷新"
                : loadError;
        }
        finally
        {
            syncing = false;
        }
    }

    private string? PickDeviceForMonitor(MonitorInfo monitor)
    {
        foreach (var key in GetMonitorKeys(monitor))
        {
            if (settings.MonitorDeviceMap.TryGetValue(key, out var configured)
                && devices.Any(device => string.Equals(device.Id, configured, StringComparison.OrdinalIgnoreCase)))
            {
                return configured;
            }
        }

        return null;
    }

    private void ReadGridToSettings()
    {
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.Tag is not MonitorInfo monitor)
            {
                continue;
            }

            var deviceId = row.Cells[2].Value as string;
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                foreach (var key in GetMonitorKeys(monitor))
                {
                    settings.MonitorDeviceMap.Remove(key);
                }
            }
            else
            {
                var selectedDevice = devices.FirstOrDefault(device => string.Equals(device.Id, deviceId, StringComparison.OrdinalIgnoreCase));
                if (selectedDevice is not null)
                {
                    settings.AudioDeviceNameMap[deviceId] = selectedDevice.Name;
                }

                foreach (var key in GetMonitorKeys(monitor))
                {
                    settings.MonitorDeviceMap[key] = deviceId;
                }
            }
        }
    }

    private IReadOnlyList<AudioDeviceInfo> AddSavedMissingDevices(IReadOnlyList<AudioDeviceInfo> currentDevices)
    {
        var result = currentDevices.ToList();
        var savedIds = settings.MonitorDeviceMap.Values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var savedId in savedIds)
        {
            if (result.Any(device => string.Equals(device.Id, savedId, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var name = settings.AudioDeviceNameMap.TryGetValue(savedId, out var savedName) && !string.IsNullOrWhiteSpace(savedName)
                ? $"{savedName}（当前未连接）"
                : $"已保存设备（当前未连接） {savedId}";
            result.Add(new AudioDeviceInfo(savedId, name, false));
        }

        return result;
    }

    private static IEnumerable<string> GetMonitorKeys(MonitorInfo monitor)
    {
        if (!string.IsNullOrWhiteSpace(monitor.StableKey))
        {
            yield return monitor.StableKey;
        }

        if (!string.IsNullOrWhiteSpace(monitor.DeviceName)
            && !string.Equals(monitor.DeviceName, monitor.StableKey, StringComparison.OrdinalIgnoreCase))
        {
            yield return monitor.DeviceName;
        }
    }

    private void Router_StatusChanged(object? sender, RouterStatusEventArgs e)
    {
        if (IsDisposed)
        {
            return;
        }

        statusLabel.Text = e.Message;
    }

    private void SaveAndNotify()
    {
        SettingsStore.Save(settings);
        CheckStateChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            router.StatusChanged -= Router_StatusChanged;
        }

        base.Dispose(disposing);
    }
}
