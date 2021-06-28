using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace UsageStats
{
    public class MainViewModel : Observable
    {
        private readonly object syncLock = new object();

        private const string DefaultApplicationList =
            @"Microsoft Visual Studio
Reflector

Microsoft Outlook
Gmail

Word
Excel
Powerpoint

Adobe

Microsoft Internet Explorer
Google Chrome
Wikipedia

Windows Explorer";

        private readonly InterceptKeys keys;
        private readonly InterceptMouse mouse;
        private readonly DispatcherTimer timer;
        private readonly Stopwatch watch;
        private Point lastPoint;

        public bool AlwaysOnTop { get { return Settings.AlwaysOnTop; } }

        // Call the Assembly GetExecutingAssembly method to get
        // the name of this application. The first 15 characters of the
        // application name will be what the Windows OS
        // sees as the instance name unless you are running multiple instances
        // of your application.
        PerformanceCounter bytesInAllHeapsPerformanceCounter;

        public MainViewModel()
        {
            UsabilityModeDisabled = true;
            string applicationInstance = Assembly.GetExecutingAssembly().GetName().ToString().Substring(0, 15);
            try
            {
                bytesInAllHeapsPerformanceCounter = new PerformanceCounter(".NET CLR Memory", "# bytes in all heaps", applicationInstance);
            }
            catch (Exception)
            {
            }

            Settings = new SettingsViewModel();
            ScreenResolution = 96;

            if (String.IsNullOrEmpty(Settings.ReportPath))
            {
                Settings.ReportPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal),
                                                   "UsageStats");
            }
            if (String.IsNullOrEmpty(Settings.ApplicationList))
            {
                Settings.ApplicationList = DefaultApplicationList;
            }

            InitStatistics();

            watch = new Stopwatch();
            watch.Start();

            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            timer.Tick += TimerTick;
            timer.Start();

            Hook.CreateKeyboardHook(KeyReader);
            keys = new InterceptKeys();
            mouse = new InterceptMouse(MouseHandler);
        }

        public DateTime RecordingStarted { get; set; }
        public DateTime FirstActivity { get; set; }
        public DateTime LastActivity { get; set; }

        public ActiveTime Recording { get; set; }

        public SettingsViewModel Settings { get; private set; }

        public string Title
        {
            get { return "Application Usage Statistics"; }
        }

        public double ScreenResolution { get; set; }

        public string CurrentApplication { get; set; }
        public Point CurrentPosition { get; set; }
        public Statistics CurrentStatistics { get; set; }
        public Statistics Statistics { get; set; }
        public Dictionary<string, Statistics> ApplicationUsage { get; set; }

        public Statistics GeneralStatistics
        {
            get { return CurrentStatistics; }
        }

        public MouseStatistics MouseStatistics
        {
            get { return CurrentStatistics.MouseStatistics; }
        }

        public KeyboardStatistics KeyboardStatistics
        {
            get { return CurrentStatistics.KeyboardStatistics; }
        }

        public string ApplicationReport
        {
            get { return CreateApplicationReport(); }
        }

        public string Report
        {
            get
            {
                var sb = new StringBuilder();
                sb.AppendLine(String.Format("Recording started: {0}", RecordingStarted));
                sb.AppendLine(String.Format("Recording ended:   {0}", Recording.LastActivity));
                sb.AppendLine(String.Format("First activity:    {0}", FirstActivity));
                sb.AppendLine(String.Format("Last activity:     {0}", LastActivity));
                
                sb.AppendLine(String.Format("Duration:                     {0}", Duration.ToShortString()));
                sb.AppendLine();
                sb.AppendLine(Statistics.ToString());
                RaisePropertyChanged(nameof(Duration));
                return sb.ToString();
            }
        }
        public TimeSpan Duration
        {
            get
            {
                return LastActivity - FirstActivity;
            }
        }

        public void UpdateMemoryCounter()
        {
            long totalMemory = GC.GetTotalMemory(false);
            if (bytesInAllHeapsPerformanceCounter != null)
            {
                var bytesInAllHeaps = bytesInAllHeapsPerformanceCounter.RawValue;
            }
        }

        public void InitStatistics()
        {
            lock (syncLock)
            {
                FirstActivity = new DateTime(0);

                RecordingStarted = DateTime.Now;
                Recording = new ActiveTime();
                Statistics = new Statistics(Recording);
                CurrentStatistics = Statistics;

                ApplicationUsage = new Dictionary<string, Statistics>();

                AddApplications();

                lastPoint = WindowHelper.GetCursorPos();
            }
        }

        private string CreateApplicationReport()
        {
            if (ApplicationUsage.Count == 0)
                return null;
            var sb = new StringBuilder();
            int longest = ApplicationUsage.Keys.Max(e => e.Length);
            foreach (var kvp in ApplicationUsage)
            {
                Statistics s = kvp.Value;
                if (s.Stats.Activity.TimeActive.TotalSeconds == 0)
                {
                    continue;
                }
                sb.AppendLine(String.Format("{0} {1}", kvp.Key.PadRight(longest + 2), s.Stats.Activity));
            }
            return sb.ToString();
        }

        [DataContract]
        class Stats
        {
            [DataMember]
            public KeyboardStatistics.KeyboardStats Keyboard;
            [DataMember]
            public MouseStatistics.MouseStats Mouse;
            [DataMember]
            public Statistics.GenericStats Global;
        }

        private bool PostJSON(string url, string body)
        {
            bool success = true;

            try
            {
                var httpWebRequest = (HttpWebRequest)WebRequest.Create(url);

                httpWebRequest.ContentType = "application/json";
                httpWebRequest.Method = "POST";
                httpWebRequest.Proxy.Credentials = System.Net.CredentialCache.DefaultNetworkCredentials;
                httpWebRequest.UseDefaultCredentials = true;

                using (var streamWriter = new StreamWriter(httpWebRequest.GetRequestStream()))
                {
                    streamWriter.Write(body);
                    streamWriter.Flush();
                    streamWriter.Close();
                }

                var httpResponse = (HttpWebResponse)httpWebRequest.GetResponse();
                using (var streamReader = new StreamReader(httpResponse.GetResponseStream()))
                {
                    var result = streamReader.ReadToEnd();
                }
            }
            catch (Exception e)
            {
                success = false;
            }

            return success;
        }

        private void PostReports(string path)
        {
            bool writeOnDisk = true;

            Stats stats = new Stats();
            stats.Mouse = MouseStatistics.Stats;
            stats.Keyboard = KeyboardStatistics.Stats;
            stats.Global = Statistics.Stats;

            MemoryStream stream1 = new MemoryStream();

            DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(Stats));
            ser.WriteObject(stream1, stats);

            stream1.Position = 0;
            StreamReader sr = new StreamReader(stream1);

            string json = sr.ReadToEnd();
            Console.WriteLine(json);

            if (Settings.PushUrl != string.Empty)
            {
                writeOnDisk = !PostJSON(Settings.PushUrl, json);
            }

            if (writeOnDisk)
            {
                path = String.Format("{0}Report_{1:yyyy-MM-dd_HHmm}.json", path, RecordingStarted);

                path = FindUniqueName(path);
                var w = new StreamWriter(path);

                w.Write(json);

                w.Close();
            }
        }

        public void SaveReports()
        {
            string path = Settings.ReportPath;

            if (!String.IsNullOrEmpty(path))
            {
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
                path += "/";
            }

            SaveReport(Statistics, path);
            foreach (var kvp in ApplicationUsage)
            {
                Statistics s = kvp.Value;
                SaveReport(s, Path.Combine(path, kvp.Key));
            }

            PostReports(path);
        }

        public void SaveReport(Statistics s, string prefix)
        {
            string path;
            switch (Settings.ReportInterval)
            {
                case ReportInterval.Hourly:
                    path = String.Format("{0}Report_{1:yyyy-MM-dd_HHmm}.txt", prefix, RecordingStarted);
                    break;
                default:
                    path = String.Format("{0}Report_{1:yyyy-MM-dd}.txt", prefix, RecordingStarted);
                    break;
            }

            path = FindUniqueName(path);
            var w = new StreamWriter(path);
            w.Write(CreateReport(s));
            w.Close();

            if (Settings.DisplayDate)
            {
                string txt = String.Format("{0:yyyy-MM-dd}", RecordingStarted);

                s.MouseStatistics.ClickMap.DrawDate(txt);
                s.MouseStatistics.DoubleClickMap.DrawDate(txt);
                s.MouseStatistics.TraceMap.DrawDate(txt);
                s.MouseStatistics.DragTraceMap.DrawDate(txt);
            }

            string imgPath = String.Format("{0}ClickMap_{1:yyyy-MM-dd}.png", prefix, RecordingStarted);
            SaveBitmap(s.MouseStatistics.ClickMap.Source, FindUniqueName(imgPath));

            imgPath = String.Format("{0}DoubleClickMap_{1:yyyy-MM-dd}.png", prefix, RecordingStarted);
            SaveBitmap(s.MouseStatistics.DoubleClickMap.Source, FindUniqueName(imgPath));

            imgPath = String.Format("{0}TraceMap_{1:yyyy-MM-dd}.png", prefix, RecordingStarted);
            SaveBitmap(s.MouseStatistics.TraceMap.Source, FindUniqueName(imgPath));

            imgPath = String.Format("{0}DragTraceMap_{1:yyyy-MM-dd}.png", prefix, RecordingStarted);
            SaveBitmap(s.MouseStatistics.DragTraceMap.Source, FindUniqueName(imgPath));

            string totalsPath = String.Format("{0}Totals.csv", prefix);
            AppendTotals(s, totalsPath);
        }

        private void AppendTotals(Statistics s, string path)
        {
            StreamWriter w;
            if (File.Exists(path))
            {
                w = new StreamWriter(path, true);
            }
            else
            {
                w = new StreamWriter(path);
                w.WriteLine("Date;FirstActivity;LastActivity;ActiveTime;MouseKeyboardRatio;Keystrokes;KeystrokesPerMinute;LeftClicks;MiddleClicks;RightClicks;DoubleClicks;MouseDistance;WheelDistance;ClicksPerMinute;");
            }
            w.Write("{0:yyyy-MM-dd};", RecordingStarted);
            w.Write("{0:HH:mm:ss};", FirstActivity);
            w.Write("{0:HH:mm:ss};", LastActivity);
            w.Write("{0};", s.Stats.Activity.TimeActive.ToShortString());
            w.Write("{0:0.0};", s.MouseKeyboardRatio);
            w.Write("{0};", s.KeyboardStatistics.Stats.KeyStrokes);
            w.Write("{0:0};", s.KeyboardStatistics.KeyStrokesPerMinute);
            w.Write("{0:0};", s.MouseStatistics.Stats.LeftMouseClicks);
            w.Write("{0:0};", s.MouseStatistics.Stats.MiddleMouseClicks);
            w.Write("{0:0};", s.MouseStatistics.Stats.RightMouseClicks);
            w.Write("{0:0};", s.MouseStatistics.Stats.MouseDoubleClicks);
            w.Write("{0:0.0};", s.MouseStatistics.MouseDistance);
            w.Write("{0:0};", s.MouseStatistics.Stats.MouseWheelDistance);
            w.Write("{0:0};", s.MouseStatistics.MouseClicksPerMinute);
            w.WriteLine();

            w.Close();
        }

        private static void SaveBitmap(BitmapSource bmp, string fileName)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using (FileStream stm = File.Create(fileName))
            {
                encoder.Save(stm);
            }
        }

        private string CreateReport(Statistics s)
        {
            var sb = new StringBuilder();
            if (s == Statistics)
            {
                sb.Append(Report);
                sb.AppendLine();
                sb.Append(CreateApplicationReport());
                sb.AppendLine();
            }

            sb.Append(s.Report());
            return sb.ToString();
        }

        private static string FindUniqueName(string path)
        {
            string newPath = path;
            string dir = Path.GetDirectoryName(path);
            string name = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);
            int i = 1;
            while (File.Exists(newPath))
            {
                string newName = name + String.Format("_{0:00}", i++);
                newPath = Path.Combine(dir, newName + ext);
            }
            return newPath;
        }

        public void Dispose()
        {
            if (mouse != null)
            {
                mouse.Dispose();
            }
            if (keys != null)
            {
                keys.Dispose();
            }
        }

        public void AddApplications(TextReader r)
        {
            while (true)
            {
                string app = r.ReadLine();
                if (app == null)
                    break;
                app = app.Trim();

                if (String.IsNullOrWhiteSpace(app))
                {
                    continue;
                }
                AddApplication(app);
            }
        }

        public void AddApplications()
        {
            using (var s = new StringReader(Settings.ApplicationList))
            {
                AddApplications(s);
            }
        }

        private void AddApplication(string appName)
        {
            if (!ApplicationUsage.ContainsKey(appName))
                ApplicationUsage.Add(appName, new Statistics(Statistics.Stats.Activity));
        }

        public IEnumerable<Statistics> GetCurrentStatistics()
        {
            if (CurrentApplication != null)
            {
                foreach (string app in ApplicationUsage.Keys)
                {
                    if (CurrentApplication.Contains(app))
                    {
                        yield return ApplicationUsage[app];
                    }
                }
            }
        }

        private void MouseHandler(IntPtr wparam, IntPtr lparam)
        {
            lock (syncLock)
            {
                switch ((MouseMessages)wparam)
                {
                    case MouseMessages.WM_LBUTTONDOWN:
                        MouseDown(MouseButton.Left);
                        break;
                    case MouseMessages.WM_LBUTTONUP:
                        MouseUp(MouseButton.Left);
                        break;
                    case MouseMessages.WM_MBUTTONDOWN:
                        MouseDown(MouseButton.Middle);
                        break;
                    case MouseMessages.WM_RBUTTONDOWN:
                        MouseDown(MouseButton.Right);
                        break;
                    case MouseMessages.WM_LBUTTONDBLCLK:
                    case MouseMessages.WM_MBUTTONDBLCLK:
                    case MouseMessages.WM_RBUTTONDBLCLK:
                        MouseDblClk();
                        break;
                    case MouseMessages.WM_MOUSEWHEEL:
                        // wparam: The high-order word indicates the distance the wheel is rotated, expressed in multiples or divisions of 
                        // WHEEL_DELTA, which is 120. A positive value indicates that the wheel was rotated forward, away from the user; 
                        // a negative value indicates that the wheel was rotated backward, toward the user.
                        MouseWheel();
                        break;
                }
            }
        }

        private void MouseUp(MouseButton mouseButton)
        {
            Statistics.MouseUp(mouseButton);
            foreach (Statistics s in GetCurrentStatistics())
            {
                s.MouseUp(mouseButton);
            }
        }

        private void MouseDown(MouseButton mouseButton)
        {
            Statistics.MouseDown(mouseButton);
            foreach (Statistics s in GetCurrentStatistics())
            {
                s.MouseDown(mouseButton);
            }
            UpdateFirstAndLast();
        }

        private void MouseDblClk()
        {
            Statistics.MouseDblClk();
            foreach (Statistics s in GetCurrentStatistics())
            {
                s.MouseDblClk();
            }
        }

        private void MouseWheel()
        {
            Statistics.MouseWheel();
            foreach (Statistics s in GetCurrentStatistics())
            {
                s.MouseWheel();
            }
            UpdateFirstAndLast();
        }

        public void KeyReader(IntPtr wParam, IntPtr lParam)
        {
            lock (syncLock)
            {
                int virtualKey = Marshal.ReadInt32(lParam);
                Key k = KeyInterop.KeyFromVirtualKey(virtualKey);
                var kc = new KeyConverter();
                string keyName = kc.ConvertToString(k);

                KeyDown(keyName);
            }
        }

        private void KeyDown(string keyName)
        {
            Statistics.KeyDown(keyName);
            foreach (Statistics s in GetCurrentStatistics())
            {
                s.KeyDown(keyName);
            }
            UpdateFirstAndLast();
        }

        private IntPtr CurrentWindowPtr;

        private void TimerTick(object sender, EventArgs e)
        {
            //double deltaTime = watch.ElapsedMilliseconds * 0.001;
            //watch.Reset(); watch.Start();

            Point pt = WindowHelper.GetCursorPos();
            double dx = pt.X - lastPoint.X;
            double dy = pt.Y - lastPoint.Y;
            double dl = Math.Sqrt(dx * dx + dy * dy);
            bool moved = dl > 0.3;
            if (moved)
            {
                MouseMove(pt);
            }
            lastPoint = pt;

            // Update current position
            CurrentPosition = pt;

            // Update current application (from window title)
            CurrentApplication = WindowHelper.GetForegroundWindowText();
            var ptr = WindowHelper.GetForegroundWindow();
            if (CurrentWindowPtr != ptr)
            {
                Statistics.RegisterWindowSwitch();
            }
            CurrentWindowPtr = ptr;

            // Update total recording time
            Recording.Update(double.MaxValue);

            RaisePropertyChanged("CurrentApplication");
            RaisePropertyChanged("CurrentPosition");
            RaisePropertyChanged("MouseDistanceText");

            RaisePropertyChanged("MouseActivity");
            RaisePropertyChanged("Activity");

            RaisePropertyChanged("ApplicationReport");

            RaisePropertyChanged("MouseStatistics");
            RaisePropertyChanged("KeyboardStatistics");
            RaisePropertyChanged("Statistics");
            RaisePropertyChanged("Report");

            bool saveReport;
            switch (Settings.ReportInterval)
            {
                case ReportInterval.Daily:
                    // Check if we passed midnight
                    saveReport = RecordingStarted.Day != DateTime.Now.Day;
                    break;
                case ReportInterval.Hourly:
                    saveReport = RecordingStarted.Hour != DateTime.Now.Hour;
                    break;
                default:
                    saveReport = false;
                    break;
            }

            if (saveReport)
            {
                // Save report for today and reset
                try
                {
                    SaveReports();
                }
                catch (Exception ex)
                {
                    do
                    {
                        MessageBox.Show(ex.Message);
                        ex = ex.InnerException;
                    } while (ex != null);
                }

                InitStatistics();
            }
        }

        private void MouseMove(Point pt)
        {
            Statistics.MouseMove(pt);
            foreach (Statistics s in GetCurrentStatistics())
            {
                s.MouseMove(pt);
            }
            UpdateFirstAndLast();
        }

        public void UpdateFirstAndLast()
        {
            if (FirstActivity.Ticks == 0)
            {
                FirstActivity = DateTime.Now;
            }
            LastActivity = DateTime.Now;
            RaisePropertyChanged("FirstActivity");
            RaisePropertyChanged("LastActivity");
        }

        public void OnClosed()
        {
            Settings.Save();
        }

        public void OnSettingsChanged()
        {
            Settings.Save();

            RaisePropertyChanged("AlwaysOnTop");
            AddApplications();
        }


        private bool _UsabilityModeDisabled;
        public bool UsabilityModeDisabled
        {
            get { return _UsabilityModeDisabled; }
            set
            {
                _UsabilityModeDisabled = value;
                RaisePropertyChanged(nameof(UsabilityModeDisabled));
            }
        }

    }
}