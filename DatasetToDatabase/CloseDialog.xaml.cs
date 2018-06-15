using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace DatasetToDatabase {
    /// <summary>
    /// Interaction logic for Close.xaml
    /// </summary>
    public partial class CloseDialog : Window {
        public static bool HasClosed {
            get;
            private set;
        } = false;

        private const int GWL_STYLE = -16;
        private const int WS_SYSMENU = 0x80000;
        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private static BackgroundWorker worker;
        private static int max, done;

        public CloseDialog() {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e) {
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLong(hwnd, GWL_STYLE, GetWindowLong(hwnd, GWL_STYLE) & ~WS_SYSMENU);

            DataManager.Abort();

            worker = new BackgroundWorker {
                WorkerReportsProgress = true
            };
            worker.DoWork += WorkerDoWork;
            worker.ProgressChanged += WorkerProgress;
            worker.RunWorkerCompleted += WorkerCompleted;
            worker.RunWorkerAsync();
        }

        private void WorkerProgress(object sender, ProgressChangedEventArgs e) {
            Dispatcher.Invoke(() => {
                PBar_Progress.Value++;
                Lbl_Progress.Content = "Saving items: " + done + "/" + max;
            });
        }

        private void WorkerCompleted(object sender, RunWorkerCompletedEventArgs e) {
            HasClosed = true;
            Thread.Sleep(100);
            Application.Current.Shutdown();
        }

        private void WorkerDoWork(object sender, DoWorkEventArgs e) {
            ConcurrentQueue<string> queue = DataManager.getLogQueue();
            max = queue.Count + 1;
            done = 0;
            Dispatcher.Invoke(() => {
                PBar_Progress.Maximum = max;
            });

            while (!queue.IsEmpty) {
                queue.TryDequeue(out string item);
                DataManager.SaveToLog(item);
                done++;
                worker.ReportProgress(0);
            }

            MainWindow.AddDateTimeLog(false, true);
            worker.ReportProgress(0);
        }
    }
}
