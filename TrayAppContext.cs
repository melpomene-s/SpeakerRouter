namespace SpeakerRouter;

internal sealed class TrayAppContext : ApplicationContext
{
    private readonly AppSettings settings;
    private readonly RouterService router;
    private readonly NotifyIcon notifyIcon;
    private readonly ToolStripMenuItem routingItem;
    private readonly ToolStripMenuItem startupItem;
    private bool syncingMenu;
    private MainForm? mainForm;

    public TrayAppContext()
    {
        settings = SettingsStore.Load();
        router = new RouterService(settings);
        router.StatusChanged += Router_StatusChanged;

        routingItem = new ToolStripMenuItem("启用自动检测并切换") { Checked = settings.RoutingEnabled, CheckOnClick = true };
        routingItem.CheckedChanged += (_, _) =>
        {
            if (syncingMenu)
            {
                return;
            }

            settings.RoutingEnabled = routingItem.Checked;
            SettingsStore.Save(settings);
            router.RoutingEnabled = settings.RoutingEnabled;
            SyncFormChecks();
        };

        startupItem = new ToolStripMenuItem("开机启动") { Checked = StartupManager.IsEnabled(), CheckOnClick = true };
        startupItem.CheckedChanged += (_, _) =>
        {
            if (syncingMenu)
            {
                return;
            }

            StartupManager.SetEnabled(startupItem.Checked);
            SyncFormChecks();
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("主界面（绑定窗口）", null, (_, _) => ShowMainForm());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(routingItem);
        menu.Items.Add(startupItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出进程", null, (_, _) => Exit());

        notifyIcon = new NotifyIcon
        {
            Icon = AppIcon.Load(),
            Text = "SpeakerRouter",
            Visible = true,
            ContextMenuStrip = menu
        };
        notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                ShowMainForm();
            }
        };
        notifyIcon.DoubleClick += (_, _) => ShowMainForm();

        router.RoutingEnabled = settings.RoutingEnabled;
    }

    private void ShowMainForm()
    {
        if (mainForm is null || mainForm.IsDisposed)
        {
            mainForm = new MainForm(settings, router);
            mainForm.CheckStateChanged += MainForm_CheckStateChanged;
        }

        mainForm.Show();
        if (mainForm.WindowState == FormWindowState.Minimized)
        {
            mainForm.WindowState = FormWindowState.Normal;
        }

        mainForm.Activate();
    }

    private void MainForm_CheckStateChanged(object? sender, EventArgs e)
    {
        syncingMenu = true;
        routingItem.Checked = settings.RoutingEnabled;
        startupItem.Checked = StartupManager.IsEnabled();
        syncingMenu = false;
    }

    private void SyncFormChecks()
    {
        if (mainForm is not null && !mainForm.IsDisposed)
        {
            mainForm.SyncChecks();
        }
    }

    private void Router_StatusChanged(object? sender, RouterStatusEventArgs e)
    {
        var text = e.Message.Length > 60 ? e.Message[..60] : e.Message;
        notifyIcon.Text = string.IsNullOrWhiteSpace(text) ? "SpeakerRouter" : text;
    }

    private void Exit()
    {
        SettingsStore.Save(settings);
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
        router.Dispose();
        mainForm?.Dispose();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            notifyIcon.Dispose();
            router.Dispose();
            mainForm?.Dispose();
        }

        base.Dispose(disposing);
    }
}
