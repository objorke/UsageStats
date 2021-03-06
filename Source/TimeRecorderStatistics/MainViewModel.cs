namespace TimeRecorderStatistics
{
    using System;
    using System.Collections.ObjectModel;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Windows;
    using System.Windows.Input;
    using System.Windows.Media;

    using Microsoft.Win32;

    using TimeRecorderStatistics.Properties;

    public class MainViewModel : Observable
    {
        private DateTime firstDayOfWeek;

        private bool perMachine;

        private bool renderSelectedOnly;

        private int totalTime;

        private int selectedTime;

        public MainViewModel(Window w)
        {
            this.PreviousWeekCommand = new DelegateCommand(x => this.FirstDayOfWeek = this.FirstDayOfWeek.AddDays(-7));
            this.NextWeekCommand = new DelegateCommand(x => this.FirstDayOfWeek = this.FirstDayOfWeek.AddDays(7));
            this.CreateReportCommand = new DelegateCommand(x => this.CreateReport());
            this.ExitCommand = new DelegateCommand(x => w.Close());
            this.ClearCategoriesCommand = new DelegateCommand(
                x =>
                {
                    foreach (var c in this.Categories)
                    {
                        c.IsChecked = false;
                    }
                });
            this.Week = new ObservableCollection<Statistics>();
            this.Header = Statistics.RenderHeader();

            this.Machines = new ObservableCollection<CheckableItem>();
            this.Categories = new ObservableCollection<CheckableItem>();

            TimeRecorder.TimeRecorder.InitializeFolders(Settings.Default.DatabaseRootFolder);
            var folders = TimeRecorder.TimeRecorder.RootFolder;
            foreach (var folder in folders.Split(';'))
            {
                foreach (var dir in Directory.GetDirectories(folder))
                {
                    this.Machines.Add(new CheckableItem(this.MachineChanged) { Header = Path.GetFileName(dir) });
                }
            }

            this.Categories.Add(new CheckableItem(this.CategoryChanged) { Header = "Unknown" });
            foreach (var cat in TimeRecorder.TimeRecorder.LoadCategories())
            {
                this.Categories.Add(new CheckableItem(this.CategoryChanged) { Header = Path.GetFileName(cat) });
            }

            var today = DateTime.Now.Date;
            this.FirstDayOfWeek = today.FirstDayOfWeek(CultureInfo.CurrentCulture);
        }

        public string RootFolders
        {
            get
            {
                return Settings.Default.DatabaseRootFolder;
            }

            set
            {
                Settings.Default.DatabaseRootFolder = value;
                Settings.Default.Save();
            }
        }

        public DelegateCommand ExitCommand { get; set; }

        public bool PerMachine
        {
            get
            {
                return this.perMachine;
            }

            set
            {
                this.perMachine = value;
                this.Refresh();
            }
        }

        public bool RenderSelectedOnly
        {
            get
            {
                return this.renderSelectedOnly;
            }

            set
            {
                this.renderSelectedOnly = value;
                this.Refresh();
            }
        }

        public ObservableCollection<Statistics> Week { get; private set; }

        public ObservableCollection<CheckableItem> Machines { get; private set; }

        public ObservableCollection<CheckableItem> Categories { get; private set; }

        public ICommand ClearCategoriesCommand { get; private set; }

        public ICommand PreviousWeekCommand { get; private set; }

        public ICommand NextWeekCommand { get; private set; }

        public ICommand CreateReportCommand { get; private set; }

        public ImageSource Header { get; private set; }

        public DateTime FirstDayOfWeek
        {
            get
            {
                return this.firstDayOfWeek;
            }

            set
            {
                this.firstDayOfWeek = value;
                this.Refresh();
            }
        }

        public string TimeInfo
        {
            get
            {
                if (this.selectedTime == this.totalTime || this.selectedTime == 0)
                {
                    return Statistics.ToTimeString(this.totalTime);
                }

                return string.Format(
                    "{0} / {1} ({2:0.0} %)",
                    Statistics.ToTimeString(this.selectedTime),
                    Statistics.ToTimeString(this.totalTime),
                    100.0 * this.selectedTime / this.totalTime);
            }
        }

        private void Refresh()
        {
            this.totalTime = 0;
            this.selectedTime = 0;
            this.Week.Clear();
            for (int i = 0; i < 7; i++)
            {
                this.Week.Add(this.Load(this.FirstDayOfWeek.AddDays(i)));
            }

            this.RaisePropertyChanged("TimeInfo");
        }

        private void CategoryChanged(CheckableItem category)
        {
            this.Refresh();
        }

        private void MachineChanged(CheckableItem machine)
        {
            this.Refresh();
        }

        private Statistics Load(DateTime date)
        {
            var categories = this.Categories.Where(c => c.IsChecked).Select(c => c.Header).ToList();
            var s = new Statistics(date, categories)
                        {
                            RenderPerMachine = this.PerMachine,
                            RenderSelectedOnly = this.RenderSelectedOnly
                        };
            foreach (var m in this.Machines.Where(m => m.IsChecked))
            {
                s.Add(m.Header);
            }

            this.totalTime += s.TotalTime;
            this.selectedTime += s.SelectedTime;
            return s;
        }

        private void CreateReport()
        {
            var dialog = new SaveFileDialog
                             {
                                 Filter = "Report files (*.txt)|*.txt",
                                 FileName = "Report.txt",
                                 DefaultExt = ".txt"
                             };

            if (!dialog.ShowDialog().Value)
            {
                return;
            }

            var rootFolder = TimeRecorder.TimeRecorder.RootFolder;
            var categories = this.Categories.Where(c => c.IsChecked).Select(c => c.Header).ToList();

            var report = new Report(categories, 8.5, 16.5);
            
            // add logs for all machines
            report.AddAll(rootFolder);

            // export
            report.Export(dialog.FileName);
            
            // open the folder
            Process.Start("Explorer.exe", Path.GetDirectoryName(dialog.FileName));
        }
    }
}