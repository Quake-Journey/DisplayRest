using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Shell;
using Forms = System.Windows.Forms;

namespace DisplayRest;

public partial class App : Application
{
    private const string Instance = @"Local\DisplayRest-23E24AF4-B771-4A7E-A2C6-3B7753BD5680";
    private const string AppId = "DisplayRest.DisplayRest";
    private const string ExitEventName = Instance + "-exit";
    private Mutex? _instance;
    private EventWaitHandle? _openEvent;
    private EventWaitHandle? _exitEvent;
    private RegisteredWaitHandle? _openWait;
    private RegisteredWaitHandle? _exitWait;
    private Icon? _icon;
    private Forms.NotifyIcon? _tray;
    private Forms.ToolStripItem? _trayOpen;
    private Forms.ToolStripItem? _trayToggle;
    private Forms.ToolStripItem? _trayExit;
    private DisplayControl? _control;
    private MainWindow? _window;
    private bool _displaysOff;
    private bool _taskbarIdSet;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--exit", StringComparer.OrdinalIgnoreCase))
        {
            if (EventWaitHandle.TryOpenExisting(ExitEventName, out var signal))
            {
                using (signal)
                    signal.Set();
            }
            Shutdown();
            return;
        }
        bool trayOnly = e.Args.Contains("--tray", StringComparer.OrdinalIgnoreCase);
        _openEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Instance + "-open");
        _instance = new Mutex(false, Instance, out bool primary);
        if (!primary)
        {
            if (!trayOnly)
                _openEvent.Set();
            Shutdown();
            return;
        }

        UserSettings settings;
        bool useDefaults = false;
        try { settings = UserSettings.Load(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            settings = new UserSettings();
            useDefaults = true;
        }
        UiText.Use(settings.Language);
        _taskbarIdSet = WindowsNative.SetCurrentProcessExplicitAppUserModelID(AppId) == 0;

        try
        {
            _control = new DisplayControl(settings);
            _window = new MainWindow(_control, settings, useDefaults);
            MainWindow = _window;
            _exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
            CreateTray(_control);
            ApplyTaskbarMenu();
            _window.LanguageChanged += () =>
            {
                UpdateTrayMenu();
                ApplyTaskbarMenu();
            };
            _openWait = ThreadPool.RegisterWaitForSingleObject(_openEvent, (_, _) => Dispatcher.BeginInvoke(OpenWindow),
                null, Timeout.Infinite, executeOnlyOnce: false);
            _exitWait = ThreadPool.RegisterWaitForSingleObject(_exitEvent, (_, _) => Dispatcher.BeginInvoke(ExitApp),
                null, Timeout.Infinite, executeOnlyOnce: false);
            if (!trayOnly)
                OpenWindow();
        }
        catch (Win32Exception error)
        {
            MessageBox.Show(UiText.Get("DisplayControlError", error.NativeErrorCode), "DisplayRest",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void CreateTray(DisplayControl control)
    {
        _icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? (Icon)SystemIcons.Application.Clone();
        var menu = new Forms.ContextMenuStrip();
        _trayOpen = menu.Items.Add("", null, (_, _) => OpenWindow());
        _trayToggle = menu.Items.Add("", null, (_, _) => control.Toggle());
        menu.Items.Add(new Forms.ToolStripSeparator());
        _trayExit = menu.Items.Add("", null, (_, _) => ExitApp());
        UpdateTrayMenu();

        _tray = new Forms.NotifyIcon { Text = "DisplayRest", Icon = _icon, ContextMenuStrip = menu, Visible = true };
        _tray.MouseClick += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Left)
                OpenWindow();
        };
        control.DisplayChanged += off =>
        {
            _displaysOff = off;
            _trayToggle.Text = UiText.Get(off ? "MenuDisplayOn" : "MenuDisplayOff");
        };
    }

    private void UpdateTrayMenu()
    {
        _trayOpen!.Text = UiText.Get("MenuOpen");
        _trayToggle!.Text = UiText.Get(_displaysOff ? "MenuDisplayOn" : "MenuDisplayOff");
        _trayExit!.Text = UiText.Get("MenuExit");
    }

    private void ApplyTaskbarMenu()
    {
        if (!_taskbarIdSet)
            return;
        var menu = new JumpList();
        menu.JumpItems.Add(new JumpTask
        {
            Title = UiText.Get("MenuExit"),
            ApplicationPath = Environment.ProcessPath,
            Arguments = "--exit"
        });
        JumpList.SetJumpList(this, menu);
    }

    private void OpenWindow()
    {
        if (_window is null)
            return;
        _window.Show();
        if (_window.WindowState == WindowState.Minimized)
            _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void ExitApp()
    {
        _window?.AllowClose();
        Shutdown();
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        _window?.AllowClose();
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _exitWait?.Unregister(null);
        _openWait?.Unregister(null);
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.ContextMenuStrip?.Dispose();
            _tray.Dispose();
        }
        _icon?.Dispose();
        _control?.Dispose();
        _exitEvent?.Dispose();
        _openEvent?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
