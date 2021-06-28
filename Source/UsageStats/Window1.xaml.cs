using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using PropertyTools.Wpf;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Reflection;
using System.Threading;

namespace UsageStats
{
    // http://mousetracker.jbfreeman.net/index.htm

    // http://www.codeproject.com/KB/system/KeyLogger.aspx
    // http://www.axino.net/tutorial/2009/05/keylogger-in-c-hooking-and-unhooking-keyboard-hook

    /// <summary>
    /// Interaction logic for Window1.xaml
    /// </summary>
    public partial class Window1 : Window
    {
        private readonly MainViewModel vm;

        public Window1()
        {
            // Single instance mode by MUTEX implementation.
            var appName = Assembly.GetEntryAssembly().GetName().Name;
            using (var mutex = new Mutex(true, appName + "Singleton", out bool notAlreadyRunning))
            {
                if (notAlreadyRunning)
                {
                    try
                    {
                        this.InitializeComponent();
                        this.vm = new MainViewModel();
                        this.DataContext = vm;

                        this.Loaded += this.Window1_Loaded;
                        this.Closed += this.Window1_Closed;
                        CommandBindings.Add(new CommandBinding(ApplicationCommands.Open, this.OpenCmdExecuted));
                    }
                    catch (Exception ex)
                    {
                        do
                        {
                            MessageBox.Show(ex.Message);
                            ex = ex.InnerException;
                        } while (ex != null);
                    }
                }
                else
                {
                    Application.Current.Shutdown();
                }
            }
        }
        public bool CanClose { get; set; }

        private void Window1_Closed(object sender, EventArgs e)
        {
            this.vm.OnClosed();
        }

        private void OpenCmdExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            this.ShowAndActivate();
        }

        private void Window1_Loaded(object sender, RoutedEventArgs e)
        {
            // this will remove the application from the task manager list (alt+tab list)
            this.SetDesktopAsOwner();
        }

        public void SetDesktopAsOwner()
        {
            var wih = new WindowInteropHelper(this) { Owner = GetDesktopWindow() };
        }

        [DllImport("user32.dll", SetLastError = false)]
        private static extern IntPtr GetDesktopWindow();

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                this.vm.SaveReports();
            }
            catch (Exception ex)
            {
                do
                {
                    MessageBox.Show(ex.Message);
                    ex = ex.InnerException;
                } while (ex != null);
            }
            this.vm.Dispose();
            base.OnClosed(e);
        }

        private void ReportFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(vm.Settings.ReportPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void SaveReport_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                this.vm.SaveReports();
            }
            catch (Exception ex)
            {
                do
                {
                    MessageBox.Show(ex.Message);
                    ex = ex.InnerException;
                } while (ex != null);
            }
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            var d = new PropertyDialog
                        {
                            Icon = Icon,
                            DataContext = vm.Settings,
                            Title = "Application preferences",
                            Topmost = this.Topmost
                        };
            d.ShowDialog();
            this.vm.OnSettingsChanged();
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            this.CanClose = true;
            this.Close();
        }

        private void ProjectWebPage_Click(object sender, RoutedEventArgs e)
        {
            Process.Start("https://github.com/objorke/UsageStats");
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            var d = new AboutDialog(this)
                        {
                            Topmost = this.Topmost,
                            Image =
                                new BitmapImage(new Uri(@"pack://application:,,,/UsageStats;component/Images/chart.png")),
                            Title = "About Application Usage Statistics",
                            UpdateStatus = "The program is updated."
                        };
            d.ShowDialog();
        }

        private void Show_Click(object sender, RoutedEventArgs e)
        {
            this.ShowAndActivate();
        }

        private void ShowAndActivate()
        {
            this.WindowState = WindowState.Normal;
            this.Activate();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            e.Cancel = !this.CanClose;
            if (e.Cancel)
            {
                this.WindowState = WindowState.Minimized;
            }
        }
        /// <summary>
        /// Resets the statistics by calling <see cref="MainViewModel.InitStatistics"/>
        /// </summary>
        /// <param name="sender">The object which fired the event</param>
        /// <param name="e">Provides event informations</param>
        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            this.vm.SaveReport(this.vm.Statistics, String.Empty);
            this.vm.InitStatistics();
        }

        double _WindowWidth = double.NaN;
        double _WindowHeight = double.NaN;
        /// <summary>
        /// Enables the usability test mode
        /// </summary>
        /// <param name="sender">The object which fired the event</param>
        /// <param name="e">Provides event informations</param>
        private void Usability_Click(object sender, RoutedEventArgs e)
        {
            if (this.vm.UsabilityModeDisabled)
            {
                //save current window size
                _WindowWidth = Width;
                _WindowHeight = Height;
                //Set window size for the usability test
                WindowStyle = WindowStyle.ToolWindow;
                Width = 160;
                Height = 90;
                this.vm.InitStatistics();
            }
            else
            {
                //reassign window size
                Width = _WindowWidth;
                Height = _WindowHeight;
                WindowStyle = WindowStyle.SingleBorderWindow;
            }
            this.vm.UsabilityModeDisabled = !this.vm.UsabilityModeDisabled;
            ShowInTaskbar = !this.vm.UsabilityModeDisabled;
            Topmost = !this.vm.UsabilityModeDisabled;
        }
    }
}