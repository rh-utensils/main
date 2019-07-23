﻿using MahApps.Metro.Controls;
using Main.Wpf.Utilities;
using MaterialDesignThemes.Wpf;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Panel = System.Windows.Forms.Panel;
using Path = System.IO.Path;

namespace Main.Wpf
{
    public partial class MainWindow : MetroWindow
    {
        public static bool IsAbout;
        private readonly Panel _panel = new Panel();

        private IntPtr _appWin;
        public Process _currentProcess;
        public Grid IndexGrid = new Grid();

        public Frame Menu = new Frame();

        private readonly Rectangle Wipe = new Rectangle();

        public MainWindow()
        {
            InitializeComponent();

            var palette = new PaletteHelper().QueryPalette();
            var hue = palette.PrimarySwatch.PrimaryHues.ToArray()[palette.PrimaryDarkHueIndex];

            Wipe.Fill = new SolidColorBrush(hue.Color);
            Wipe.Margin = new Thickness(0);
            Wipe.Visibility = Visibility.Collapsed;
            System.Windows.Controls.Panel.SetZIndex(Wipe, 100);

            MainGrid.Children.Add(Wipe);
        }

        private async void MetroWindow_Loaded(object sender, RoutedEventArgs e)
        {

            MessageHelper.ReceiveDataMessages();

            try
            {
                if (!string.IsNullOrEmpty(Config.Informations.Extension.Favicon))
                {
                    Uri iconUri = new Uri(Config.Informations.Extension.Favicon, UriKind.Relative);
                    Icon = new BitmapImage(iconUri);
                }
            }
            catch (Exception ex)
            {
                LogFile.WriteLog(ex);
            }

            MinHeight = Config.Informations.Extension.WindowHeight;
            MinWidth = Config.Informations.Extension.WindowWidth;

            CenterWindowOnScreen();

            if (File.Exists(Path.Combine(Config.ExtensionsDirectory, Config.Informations.Extension.Name, "updater.exe")))
            {
                Index.Navigate(new Uri("Pages/Update.xaml", UriKind.Relative));
                return;
            }

            if (File.Exists(Path.GetFullPath(@".\updater.exe")))
            {
                Index.Navigate(new Uri("Pages/Update.xaml", UriKind.Relative));
                return;
            }

            await Login();
        }

        public async Task Login()
        {
            if (Config.Login.SkipLogin)
            {
                await LoadExtensionAsync();
            }
            else if (await Config.Login.FirstRun.Get())
            {
                Index.Navigate(new Uri("Pages/Login.xaml", UriKind.Relative));
            }
            else if (await Config.Login.LoggedIn.Get())
            {
                Index.Navigate(new Uri("Pages/Login.xaml", UriKind.Relative));
            }
            else
            {
                await LoadExtensionAsync();
            }
        }

        public async Task LoadExtensionAsync(bool wipeAnimation = false)
        {
            if (wipeAnimation) Wipe.Visibility = Visibility.Visible;

            await SettingsHelper.StartSync();

            var timer = new DispatcherTimer(new TimeSpan(0, 0, 0, 0, 1), DispatcherPriority.Normal, delegate
            {
                if (_appWin != IntPtr.Zero) MoveWindow(_appWin, 0, 0, _panel.Width, _panel.Height, true);
            }, Application.Current.Dispatcher);

            timer.Start();

            if (Config.Menu.SingleSite.HideMenu)
            {
                MainGrid.Children.Add(IndexGrid);

                Index.Visibility = Visibility.Collapsed;

                if (wipeAnimation) await RunWipeAnimation();

                await SetExe(Config.Menu.SingleSite.Path, Config.Menu.SingleSite.StartArguments);
            }
            else
            {
                IndexGrid.Margin = new Thickness(250, 0, 0, 0);
                MainGrid.Children.Add(IndexGrid);

                Index.Margin = new Thickness(250, 0, 0, 0);
                Index.Visibility = Visibility.Collapsed;

                Menu.HorizontalAlignment = HorizontalAlignment.Left;
                Menu.NavigationUIVisibility = NavigationUIVisibility.Hidden;
                Menu.Navigate(new Uri("Pages/Menu.xaml", UriKind.Relative));
                Menu.Width = 250;
                MainGrid.Children.Add(Menu);

                if (wipeAnimation) await RunWipeAnimation();
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern long SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool MoveWindow(IntPtr hWnd, int x, int y, int cx, int cy, bool repaint);

        public const int SW_HIDE = 0;
        public const int SW_SHOWNORMAL = 1;

        [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Auto)]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        static extern int SetForegroundWindow(IntPtr point);

        private async Task WaitForExtension(Process p)
        {
            while (string.IsNullOrEmpty(p.MainWindowTitle))
            {
                await Task.Delay(100);
                p.Refresh();
            }

            return;
        }

        public static bool _loadingEXE;

        public static List<(Process proc, int id)> backgroundProcesses = new List<(Process proc, int id)>();

        public async Task SetExe(string exe, string argument, int id = -1)
        {
            if (_loadingEXE) return;

            _loadingEXE = true;

            var host = new WindowsFormsHost();

            Index.Visibility = Visibility.Collapsed;
            IndexGrid.Visibility = Visibility.Visible;

            Pages.Menu.ListViewMenu.IsEnabled = false;

            try
            {
                Index.Navigate(new Uri("Pages/Load.xaml", UriKind.Relative));
                Index.Visibility = Visibility.Visible;
                IndexGrid.Visibility = Visibility.Collapsed;

                var ps = new ProcessStartInfo(exe)
                {
                    Arguments = argument,

                    WindowStyle = ProcessWindowStyle.Minimized
                };

                bool hideExe = false;

                for (int i = 0; i < backgroundProcesses.Count; ++i)
                {
                    var proc = backgroundProcesses[i].proc;
                    var procID = backgroundProcesses[i].id;

                    if (proc == _currentProcess)
                    {
                        ShowWindow(proc.MainWindowHandle, SW_HIDE);
                        hideExe = true;
                        break;
                    }
                }

                if (!hideExe) _currentProcess?.Kill();

                var p = Process.Start(ps);

                bool exeIsHidden = false;

                for (int i = 0; i < backgroundProcesses.Count; ++i)
                {
                    var proc = backgroundProcesses[i].proc;
                    var procID = backgroundProcesses[i].id;

                    if (procID == id)
                    {
                        ShowWindow(proc.MainWindowHandle, SW_SHOWNORMAL);
                        _appWin = proc.MainWindowHandle;
                        p?.Kill();
                        exeIsHidden = true;
                        break;
                    }
                }

                if (!exeIsHidden)
                {
                    await WaitForExtension(p);

                    _currentProcess = p;
                    backgroundProcesses.Add((p, id));

                    if (p != null) _appWin = p.MainWindowHandle;

                    await Task.Delay(100);

                    MessageHelper.SendDataMessage(p, "set Name \"" + Config.Informations.Extension.Name + "\"");
                    MessageHelper.SendDataMessage(p, "set Color \"" + Config.Informations.Extension.Color + "\"");

                    SettingsHelper.SendSettingsBroadcast();

                    if (!string.IsNullOrEmpty(ExtensionsManager.FileToOpen)) MessageHelper.SendDataMessage(p, "open File \"" + ExtensionsManager.FileToOpen + "\"");
                }

                SetParent(_appWin, _panel.Handle);

                Index.Visibility = Visibility.Collapsed;
                IndexGrid.Visibility = Visibility.Visible;

                

                ps.WindowStyle = ProcessWindowStyle.Maximized;

                host.Child = _panel;

                IndexGrid.Children.Add(host);

                if (_appWin != IntPtr.Zero) MoveWindow(_appWin, 0, 0, _panel.Width, _panel.Height, true);
            }
            catch (Exception ex)
            {
                LogFile.WriteLog(ex);
                Index.Navigate(new Uri("Pages/Error.xaml", UriKind.Relative));
                Index.Visibility = Visibility.Visible;
                IndexGrid.Visibility = Visibility.Collapsed;

                _currentProcess = null;
            }

            Pages.Menu.ListViewMenu.IsEnabled = true;

            _loadingEXE = false;
        }

        public void CenterWindowOnScreen()
        {
            var screenWidth = SystemParameters.PrimaryScreenWidth;
            var screenHeight = SystemParameters.PrimaryScreenHeight;
            var windowWidth = Width;
            var windowHeight = Height;
            Left = (screenWidth / 2) - (windowWidth / 2);
            Top = (screenHeight / 2) - (windowHeight / 2);
        }

        private void MetroWindow_Closed(object sender, EventArgs e)
        {
            Application.Current.Shutdown();
        }

        private async Task RunWipeAnimation()
        {
            DoubleAnimation Opacity = new DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(300)
            };
            Storyboard.SetTargetProperty(Opacity, new PropertyPath(OpacityProperty));

            Storyboard sb = new Storyboard();
            Storyboard.SetTarget(sb, Wipe);

            sb.Children.Add(Opacity);

            sb.Begin();

            await Task.Delay(300);

            Wipe.Visibility = Visibility.Collapsed;

            sb.Stop();
        }

        private void MetroWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                if (_currentProcess == null) return;

                IntPtr h = _currentProcess.MainWindowHandle;
                SetForegroundWindow(h);

                var k = new KeyConverter();

                var convertedKey = k.ConvertToString(e.Key);

                if (convertedKey.Length > 0)
                {
                    convertedKey = "{" + convertedKey.ToUpper() + "}";
                }

                switch (convertedKey)
                {
                    case "+":
                        convertedKey = "{+}";
                        break;
                    case "^":
                        convertedKey = "{^}";
                        break;
                    case "%":
                        convertedKey = "{%}";
                        break;
                    case "~":
                        convertedKey = "{~}";
                        break;
                    case "{":
                        convertedKey = "{{}";
                        break;
                    case "}":
                        convertedKey = "{}}";
                        break;


                    case "{SPACE}":
                        convertedKey = " ";
                        break;
                    case "{RETURN}":
                        convertedKey = "{ENTER}";
                        break;

                    case "{SHIFT}":
                    case "{LEFTSHIFT}":
                    case "{RIGHTSHIFT}":
                        convertedKey = "+";
                        break;
                    case "{CTRL}":
                    case "{LEFTCTRL}":
                    case "{RIGHTCTRL}":
                        convertedKey = "^";
                        break;
                    
                    case "{SYSTEM}":
                        convertedKey = "{F10}";
                        break;
                    case "{PAUSE}":
                        convertedKey = "{BREAK}";
                        break;

                    // [To-Do] Test and add more
                }

                System.Windows.Forms.SendKeys.SendWait(convertedKey);
            }
            catch (Exception ex)
            {
                LogFile.WriteLog(ex);
            }
        }
    }
}