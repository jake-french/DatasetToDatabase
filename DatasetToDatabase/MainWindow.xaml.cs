using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace DatasetToDatabase {
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window {
        public static readonly bool autoscrollLogFlag = true;      //severly slows down performance    

        public static MainWindow window { get; private set; }
        public static bool displayLineReads = false;     //halts program as UI updates (KEEP FALSE)
        public static bool displaySQLQueries = false;
        public static bool isRunning {
            get;
            private set;
        }
        public static bool isClosing {
            get;
            private set;
        }

        private static string logFile;
        private static int logAutoScrollThreshold = 1;
        private static List<string> lines;
        private static List<Rainfall> records;

        public MainWindow() {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e) {
            if (window == null) {
                window = this;
            } else {
                this.Close();
            }

            Console.WriteLine(System.IO.Path.GetDirectoryName(Assembly.GetEntryAssembly().Location));
        }

        private void Window_Closing(object sender, CancelEventArgs e) {
            if (!CloseDialog.HasClosed) {
                MessageBoxResult confirmation = MessageBox.Show("Are you sure you want to close the application?", "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (confirmation == MessageBoxResult.Yes) {
                    Console.WriteLine("Stopping application...");
                    window.Hide();

                    if (!DataManager.isQueueEmpty()) {
                        e.Cancel = true;
                        CloseDialog close = new CloseDialog();
                        close.ShowDialog();
                    } else {
                        DataManager.Abort();
                        Console.WriteLine("Shutdown");
                    }
                } else {
                    e.Cancel = false;
                }
            }
        }

        private void Window_Closed(object sender, EventArgs e) {
            if (window == this) {
                window = null;
            }
        }

        private void Btn_Select_Click(object sender, RoutedEventArgs e) {
            Microsoft.Win32.OpenFileDialog fileDialog = new Microsoft.Win32.OpenFileDialog();

            fileDialog.DefaultExt = ".pre";
            fileDialog.Filter = "Precipitation datasets (.pre)|*.pre";
            fileDialog.InitialDirectory = System.IO.Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            Nullable<bool> result = fileDialog.ShowDialog();

            if (result == true) {
                string fileName = fileDialog.FileName;
                Txt_InputFile.Text = fileName;
                Txt_OutputFile.Text = fileName.Replace(System.IO.Path.GetFileName(fileName), "") + System.IO.Path.GetFileNameWithoutExtension(fileName) + ".db";
                logFile = fileName.Replace(System.IO.Path.GetFileName(fileName), "") + System.IO.Path.GetFileNameWithoutExtension(fileName) + ".log";
            }
        }

        private void Btn_Convert_Click(object sender, RoutedEventArgs e) {
            bool validated = ValidateInput();
            if (validated) {
                MessageBoxResult result = MessageBox.Show("Do you want to convert: " + Txt_InputFile.Text + " to " + Txt_OutputFile.Text + "?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes) {
                    ConvertData();
                }
            }
        }

        private bool ValidateInput() {
            if (string.IsNullOrEmpty(Txt_InputFile.Text) || string.IsNullOrEmpty(Txt_OutputFile.Text)) {
                if (string.IsNullOrEmpty(Txt_InputFile.Text) && string.IsNullOrEmpty(Txt_OutputFile.Text)) {
                    MessageBox.Show("Both the input file and output file values are empty!", "Error!", MessageBoxButton.OK, MessageBoxImage.Error);
                } else if (string.IsNullOrEmpty(Txt_InputFile.Text) && !string.IsNullOrEmpty(Txt_OutputFile.Text)) {
                    MessageBox.Show("The input file value is empty!", "Error!", MessageBoxButton.OK, MessageBoxImage.Error);
                } else if (!string.IsNullOrEmpty(Txt_InputFile.Text) && string.IsNullOrEmpty(Txt_OutputFile.Text)) {
                    MessageBox.Show("The output file value is empty!", "Error!", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                return false;
            } else {
                if (!DataManager.DoesFileExist(Txt_InputFile.Text)) {
                    MessageBox.Show("The input file does not exist!", "Error!", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }
            }

            return true;
        }

        private void ConvertData() {
            //Setup Screen 
            Lst_Log.ItemsSource = null;
            Lst_Log.Items.Refresh();

            //Disable controls
            if (displaySQLQueries && displayLineReads) {
                logAutoScrollThreshold = 10;
            } else if (displayLineReads ^ displaySQLQueries) {
                logAutoScrollThreshold = 5;
            } else {
                logAutoScrollThreshold = 1;
            }

            SetControlsEnabled(false);
            DataManager.StartLogThread(logFile);

            AddDateTimeLog();
            isRunning = true;
            AddToLog("Preparing conversion of " + Txt_InputFile.Text + " to " + Txt_OutputFile.Text);
            PBar_Progress.IsIndeterminate = true;
            SetProgressBarText("Preparing files...");

            bool createFile = true;
            if (DataManager.DoesFileExist(Txt_OutputFile.Text)) {
                MessageBoxResult confirm = MessageBox.Show("Do you want to overwrite or merge with the existing database: " + Txt_OutputFile.Text, "Confirmation", MessageBoxButton.YesNoCancel);
                if (confirm == MessageBoxResult.No) {
                    createFile = false;
                } else if (confirm == MessageBoxResult.Yes) {
                    File.Delete(Txt_OutputFile.Text);
                } else {
                    return;
                }
            }

            if (createFile) {
                if (DataManager.CreateDatabase(Txt_OutputFile.Text)) {
                    AddToLog("File created: " + Txt_OutputFile.Text);
                }
            }

            DataManager.CreateDatabaseTable(Txt_OutputFile.Text, "Rainfall", "ID integer primary key, XRef integer, YRef integer, Date text, Value integer");

            SetProgressBarText("Reading dataset...");
            lines = DataManager.ReadDataFromFile(Txt_InputFile.Text);
            AddToLog("Reading " + lines.Count + " lines...");
            AddToLog("");       //Add seperator
            records = new List<Rainfall>();

            BackgroundWorker worker = new BackgroundWorker();
            worker.DoWork += WorkerDoWork;
            worker.RunWorkerCompleted += WorkerFinished;

            worker.RunWorkerAsync();
        }

        private void WorkerDoWork(object sender, DoWorkEventArgs e) {
            try {
                Rainfall temp = null;
                int startYear = -1, endYear = -1, curYear = -1, convert = -1, XRef = -1, YRef = -1;
                for (int x = 0; x < lines.Count; x++) {
                    //AddToLog(x + " | " + lines[x]);

                    if (lines[x].Contains("[Years=")) {
                        //Get Year Range
                        string[] parts = lines[x].Split('[', ']').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToArray();
                        parts = parts[1].Split(new string[] { "Years=", "-" }, StringSplitOptions.RemoveEmptyEntries);
                        startYear = Int32.Parse(parts[0]);
                        endYear = Int32.Parse(parts[1]);
                        curYear = startYear;
                        window.Dispatcher.Invoke(() => {
                            AddToLog("Year Range: " + startYear + "-" + endYear);
                        });
                    } else if (lines[x].Contains("Grid-ref")) {
                        temp = new Rainfall();
                        curYear = startYear;
                        string[] parts = lines[x].Split(' ');
                        parts = parts.Where(s => !string.IsNullOrEmpty(s)).ToArray();       //remove empty elements
                        parts[1] = parts[1].Replace(",", string.Empty);     //replace comma seperators to empty space (to allow Int parsing)
                        XRef = Int32.Parse(parts[1]);
                        YRef = Int32.Parse(parts[2]);
                        //Console.WriteLine("Evaluating grid-ref: " + temp.XRef + ", " + temp.YRef);
                        //if (displayLineReads) Dispatcher.Invoke(() => { AddToLog("Line: " + x + " | Grid Ref (x, y): " + temp.XRef + ", " + temp.YRef, false); });
                    } else {
                        if (temp != null) {
                            temp = new Rainfall {
                                XRef = XRef,
                                YRef = YRef,
                                year = curYear
                            };
                            //Console.WriteLine("Values checking: " + lines[x]);
                            string[] parts = lines[x].Split(' ');
                            parts = parts.Where(s => !string.IsNullOrEmpty(s)).ToArray();
                            //Validate (max int char length = 5, check for greater and from right
                            if (parts.Length < 12) {
                                Dispatcher.Invoke(() => {
                                    AddToLog("Analysing line: " + x + " as less than 12 values were recognised!");
                                    AddToLog("Line: " + x + " | " + lines[x]);
                                });
                                Console.WriteLine("Values found on line: " + x + " does not represent 12 months, validating further...");
                                foreach (string part in parts) {
                                    DataManager.GetSeperatedValue(part);
                                }

                                List<string> newParts = DataManager.GetValidatedStrings();
                                if (newParts.Count > 0) {
                                    parts = newParts.ToArray();
                                } else {
                                    throw new Exception("Error occured!");
                                }
                            }

                            //if (showEachLineFlag) {
                            //    string combo = string.Join(" ", parts);
                            //    Dispatcher.Invoke(() => { AddToLog("Line: " + x + " | " + combo, true, true); });
                            //}

                            //Console.WriteLine("Adding new values array, potential values: " + parts.Length + " out of " + temp.values.Length + " slots!");
                            for (int y = 0; y < temp.values.Length; y++) {
                                convert = Int32.Parse(parts[y]);
                                //Console.WriteLine("Assigning at index: " + y + " value: " + convert);
                                temp.values[y] = convert;
                            }
                            records.Add(temp);
                            Dispatcher.Invoke(() => {
                                Rainfall last = records[records.Count - 1];
                                //AddToLog("A| (" + temp.XRef + ", " + temp.YRef + ") - " + temp.year + " | " + String.Join(",", temp.values));     //Testing purposes
                                AddToLog("(" + last.XRef + ", " + last.YRef + ") - " + last.year + " | " + String.Join(",", last.values), false, !displayLineReads);
                            });

                            //Console.WriteLine("Grid: " + temp.XRef + ", " + temp.YRef + " year: " + temp.year + " values assigned: " + String.Join(", ", temp.values));
                            curYear++;
                            if (curYear > endYear) {
                                curYear = startYear;
                            }
                            //Console.WriteLine("Added Rainfall record: " + temp);
                        }
                    }
                }
            } catch (Exception ex) {
                Console.WriteLine(ex.Message);
            }

            Dispatcher.Invoke(() => {
                if (displayLineReads) AddToLog("");
                AddToLog("Data Processed!");
                AddToLog("Created " + records.Count + " temporary data objects!");
                AddToLog("");
            });

            //Console.WriteLine("\n\n\n\nData:");
            //foreach (Rainfall r in records) {
            //    Console.WriteLine(r.XRef + ", " + r.YRef + " - " + r.year + " | " + String.Join(", ", r.values));
            //    Dispatcher.Invoke(() => {
            //        AddToLog("(" + r.XRef + ", " + r.YRef + ") - " + r.year + " | " + String.Join(",", r.values));
            //    });
            //}
            Console.WriteLine("\nAll data processed!");
        }

        private void WorkerFinished(object sender, RunWorkerCompletedEventArgs e) {
            Dispatcher.Invoke(() => {
                PBar_Progress.IsIndeterminate = false;
                PBar_Progress.Maximum = records.Count * 12;     //foreach record, 12 inserts
                PBar_Progress.Value = 0;
                SetProgressBarText("Inserting records into table...");
                AddToLog("");
                AddToLog("Inserting records...");
                Console.WriteLine("Commencing auto population of SQL database...");
                DataManager.AutoPopulateTable(Txt_OutputFile.Text, records);
            });
        }

        public static void AddDateTimeLog(bool isStart = true, bool force = false) {
            Console.WriteLine("Adding " + (isStart ? "start" : "finish") + " time to log!");
            string item = ((isStart ? "Start Time: " : "\nFinish Time: ") + DateTime.Now.ToString("dddd") + " " + DateTime.Now.ToString("dd") +
                    ((DateTime.Now.Day % 10 == 1 && DateTime.Now.Day != 11) ? "st" : (DateTime.Now.Day % 10 == 2 && DateTime.Now.Day == 2 && DateTime.Now.Day != 12) ? "nd" :
                    (DateTime.Now.Day % 10 == 3 && DateTime.Now.Day != 13) ? "rd" : "th") + " " +        //Add suffix to date
                    DateTime.Now.ToString("MMMM yyyy") + " at: " + DateTime.Now.ToString("hh:mm:ss tt"));

            if (force) {
                DataManager.SaveToLog(item);
                if (!isStart) DataManager.SaveToLog("");
            } else {
                AddToLog(item);
                if (!isStart) AddToLog("");
            }
        }

        public static void AddToLog(string text, bool ignoreInFile = false, bool ignoreOnScreen = false) {
            if (window != null) {
                if (!ignoreInFile) DataManager.AddToLogQueue(text);

                if (!ignoreOnScreen) {
                    window.Lst_Log.Items.Add(text);

                    if (autoscrollLogFlag && window.Lst_Log.Items.Count % logAutoScrollThreshold == 0) {
                        window.Lst_Log.Items.MoveCurrentTo(window.Lst_Log.Items[window.Lst_Log.Items.Count - 1]);
                        window.Lst_Log.ScrollIntoView(window.Lst_Log.Items[window.Lst_Log.Items.Count - 1]);
                    }

                    if (window.Lst_Log.Items.Count > 500) {
                        window.Lst_Log.Items.Remove(window.Lst_Log.Items[0]);
                        window.Lst_Log.Items.Refresh();
                    }
                }
            }
        }

        public static void RemoveFromLog(string text) {
            if (window != null) {
                window.Lst_Log.Items.Remove(text);
            }
        }

        public static void SetProgressBarText(string text) {
            if (window != null) {
                window.Lbl_ProgressTxt.Content = text;
            }
        }

        public void Finish(int queryCnt = -1) {
            AddDateTimeLog(false);
            isRunning = false;
            SetControlsEnabled(true);
            //DataManager.Abort();
            MessageBox.Show("Conversion Completed! " + (queryCnt != -1 ? queryCnt + " records added to database!" : "") + " ");
        }

        public void SetControlsEnabled(bool flag) {
            Txt_OutputFile.IsEnabled = flag;
            Txt_InputFile.IsEnabled = flag;
            Btn_Convert.IsEnabled = flag;
            Btn_Select.IsEnabled = flag;
        }
    }
}
