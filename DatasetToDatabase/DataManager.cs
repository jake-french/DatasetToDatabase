using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DatasetToDatabase {
    public static class DataManager {
        public const bool RECORD_LOG = false;

        private static string databaseConn;
        private static Queue<string> validatedFields;
        private static List<Rainfall> data;
        private static Thread logThread;
        private static string logFileName;
        public static ConcurrentQueue<string> itemsForLog;
        private static BackgroundWorker worker;
        private static bool displayProgress = true;
        private static SQLiteTransaction transaction;

        public static bool DoesFileExist(string file) {
            return File.Exists(file);
        }

        public static string GetMonth(int value) {
            return (new DateTime(DateTime.Now.Year, value, DateTime.Now.Day)).ToString("MMMM");
        }

        public static bool isQueueEmpty() {
            if (itemsForLog != null) {
                return itemsForLog.IsEmpty;
            } else {
                return true;
            }
        }

        public static ConcurrentQueue<string> getLogQueue() {
            return itemsForLog;
        }

        public static void GetSeperatedValue(string s) {
            if (validatedFields == null) validatedFields = new Queue<string>();

            Console.WriteLine("Analysing string: " + s + " for overflow character length (max length == 5)");
            if (s.Length > 4) {
                Console.WriteLine("String: " + s + " exceeds character limit!");
                int length = s.Length;
                int splitPoint = length - 5;
                string last = s.Substring(splitPoint, length - splitPoint);
                s = s.Remove(splitPoint, length - splitPoint);

                MainWindow.window.Dispatcher.Invoke(() => {
                    MainWindow.AddToLog("Split into: " + s + " | " + last);
                });
                Console.WriteLine("Split into: " + s + " | " + last);
                validatedFields.Enqueue(last);

                if (s.Length > 4) {
                    GetSeperatedValue(s);
                    MainWindow.window.Dispatcher.Invoke(() => {
                        MainWindow.AddToLog("Additional splitting on " + s + " required!");
                    });
                } else {
                    validatedFields.Enqueue(s);
                }
            } else {
                validatedFields.Enqueue(s);
            }
        }

        public static List<string> GetValidatedStrings() {
            if (validatedFields != null) {
                List<string> values = new List<string>();

                while (validatedFields.Count > 0) {
                    values.Add(validatedFields.Dequeue());
                }

                return values;
            } else {
                return new List<string>();
            }
        }

        /// <summary>
        /// Momentially open connection to database, if no errors and no file will create database.
        /// </summary>
        /// <param name="database"></param>
        /// <returns></returns>
        public static bool CreateDatabase(string database) {
            databaseConn = "Data Source=" + database + ";Version=3;";

            using (SQLiteConnection conn = new SQLiteConnection(databaseConn)) {
                try {
                    conn.Open();
                } catch (Exception e) {
                    Console.WriteLine(e.Message);
                } finally {
                    conn.Close();
                }
            }
            return DoesFileExist(database);
        }

        public static void CreateDatabaseTable(string database, string tableName, string columns) {
            databaseConn = "Data Source=" + database + ";Version=3;";

            using (SQLiteConnection conn = new SQLiteConnection(databaseConn)) {
                try {
                    conn.Open();

                    SQLiteCommand command = conn.CreateCommand();
                    command.CommandText = "CREATE Table " + tableName + "(" + columns + ");";

                    command.ExecuteNonQuery();
                } catch (Exception e) {
                    Console.WriteLine(e.Message);
                } finally {
                    conn.Close();
                }
            }
        }

        public static void Abort() {
            if (transaction != null) {
                transaction.Commit();
                transaction = null;
            }
            if (worker != null) {
                worker.CancelAsync();

            }
            StopLogThread();

            //if (!itemsForLog.IsEmpty) {
            //    Console.WriteLine("Log cache is not empty! Saving " + itemsForLog.Count + " items to log before quitting...");
            //    MainWindow.window.Dispatcher.Invoke(() => {
            //        Console.WriteLine("Setting progress bar");
            //        MainWindow.window.PBar_Progress.Maximum = itemsForLog.Count;
            //        MainWindow.window.PBar_Progress.Value = 0;
            //        MainWindow.window.Lbl_ProgressTxt.Content = "Saving to log... (0/" + itemsForLog.Count + ")";
            //    });
            //    int cnt = 0;
            //    while (!itemsForLog.IsEmpty) {
            //        itemsForLog.TryDequeue(out string item);
            //        SaveToLog(item);
            //        cnt++;
            //        MainWindow.window.Dispatcher.Invoke(() => {
            //            Console.WriteLine("Saving to log... (" + cnt + "/" + itemsForLog.Count + ")");
            //            MainWindow.window.Lbl_ProgressTxt.Content = "Saving to log... (" + cnt + "/" + itemsForLog.Count + ")";
            //            MainWindow.window.PBar_Progress.Value++;
            //        });
            //    }

            //    Console.WriteLine("All items saved to log! Aborting...");
            //}
        }

        public static void AutoPopulateTable(string database, List<Rainfall> records) {
            data = records;

            //Console.WriteLine("\n\nValidating...");       //Confirmed by debug
            //foreach(Rainfall r in data) {
            //    Console.WriteLine(r.XRef + ", " + r.YRef + " - " + r.year + " | " + String.Join(", ", r.values));
            //}
            MainWindow window = MainWindow.window;
            window.Dispatcher.Invoke(() => {
                if (window.Lst_Log.Items.Count > 1) {
                    window.Lst_Log.Items.MoveCurrentTo(window.Lst_Log.Items[window.Lst_Log.Items.Count - 1]);
                    window.Lst_Log.ScrollIntoView(window.Lst_Log.Items[window.Lst_Log.Items.Count - 1]);
                }

                if (!displayProgress) {
                    window.PBar_Progress.IsIndeterminate = true;
                }
            });

            databaseConn = "Data Source=" + database + ";Version=3;";
            worker = new BackgroundWorker {
                WorkerSupportsCancellation = true,
                WorkerReportsProgress = true
            };
            worker.DoWork += WorkerAddRecords;
            worker.ProgressChanged += WorkerReportProgress;
            worker.RunWorkerCompleted += WorkerFinished;

            Console.WriteLine("Starting DataManager auto populate background worker...");
            worker.RunWorkerAsync();        //Runs worker without interfering with UI
        }

        private static void WorkerReportProgress(object sender, ProgressChangedEventArgs e) {
            if (displayProgress) {
                if (MainWindow.window != null) {
                    MainWindow.window.Dispatcher.Invoke(() => {
                        //MainWindow.AddToLog("Data added to database!");
                        MainWindow.window.PBar_Progress.Value++;
                        MainWindow.window.Lbl_ProgressTxt.Content = "Inserting records... (" + MainWindow.window.PBar_Progress.Value + "/" + data.Count * 12 + " - " +
                        (Math.Round((MainWindow.window.PBar_Progress.Value / (data.Count * 12)) * 100, 2)) + "%)";
                    });
                }
            }
        }

        private static void WorkerAddRecords(object sender, DoWorkEventArgs e) {
            int cnt = 0;
            int recordCnt = 0;

            using (SQLiteConnection conn = new SQLiteConnection(databaseConn)) {
                try {
                    conn.Open();

                    using (transaction = conn.BeginTransaction()) {
                        int affected;
                        SQLiteCommand command;
                        foreach (Rainfall record in data) {
                            for (int i = 0; i < record.values.Length; i++) {
                                MainWindow.window.Dispatcher.Invoke(() => {
                                    MainWindow.AddToLog("Record: " + recordCnt + "/" + data.Count + " - " + (i + 1).ToString() + "/" + record.values.Length + " \n" +
                                    "Adding record for (" + record.XRef + ", " + record.YRef + ") " + GetMonth(i + 1) + " " + record.year, false, !MainWindow.displaySQLQueries);
                                });
                                command = new SQLiteCommand(conn);
                                command.CommandText = "INSERT INTO Rainfall(XRef, YRef, Date, Value) Values(@XRef, @YRef, @Date, @Value)";
                                command.CommandType = System.Data.CommandType.Text;
                                command.Transaction = transaction;
                                //Console.WriteLine("Adding parameters to SQL query");

                                command.Parameters.Add(new SQLiteParameter("@XRef", record.XRef));
                                command.Parameters.Add(new SQLiteParameter("@YRef", record.YRef));
                                string date = GetMonth(i + 1) + " " + record.year;
                                command.Parameters.Add(new SQLiteParameter("@Date", date));
                                command.Parameters.Add(new SQLiteParameter("@Value", record.values[i]));
                                //Console.WriteLine("Parameters added to SQL query!");

                                string displayVal = command.CommandText.Replace("@XRef", record.XRef.ToString()).Replace("@YRef", record.YRef.ToString()).Replace("@Date", date.ToString()).Replace("@Value", record.values[i].ToString());
                                MainWindow.window.Dispatcher.Invoke(() => {
                                    MainWindow.AddToLog("Executing SQL Script:\n " + displayVal, true, !MainWindow.displaySQLQueries);
                                });


                                //Execute
                                affected = command.ExecuteNonQuery();
                                if (affected == 1) {
                                    MainWindow.window.Dispatcher.Invoke(() => {
                                        MainWindow.RemoveFromLog("Executing SQL Script:\n " + displayVal);
                                        MainWindow.AddToLog("Executing SQL Script:\n " + displayVal + "  - Success!", false, !MainWindow.displaySQLQueries);
                                    });

                                    worker.ReportProgress(0);
                                    cnt++;
                                } else {
                                    MainWindow.window.Dispatcher.Invoke(() => {
                                        MainWindow.RemoveFromLog("Executing SQL Script:\n " + displayVal);
                                        MainWindow.AddToLog("Executing SQL Script:\n " + displayVal + "  - Failed!", false, !MainWindow.displaySQLQueries);
                                    });
                                    throw new Exception("An Error Occured!");
                                }

                                //Reset
                                command.Parameters.Clear();
                            }
                            //Console.WriteLine(cnt + " queries succesful (Total: " + data.Count * 12 + ")");
                            recordCnt++;
                        }
                        Console.WriteLine("Finished inserting all records...");
                        transaction.Commit();
                    }
                } catch (Exception ex) {
                    Console.WriteLine(ex.Message);
                } finally {
                    transaction = null;
                    conn.Close();
                }
            }

            //MainWindow.window.Dispatcher.Invoke(() => {
            //    MainWindow.AddToLog("Performed " + cnt + " out of " + data.Count + " record inserts!");
            //});
        }

        private static void WorkerFinished(object sender, RunWorkerCompletedEventArgs e) {
            if (!e.Cancelled) {
                //Re-enable UI controls
                MainWindow.window.SetControlsEnabled(true);
                MainWindow.window.Dispatcher.Invoke(() => {
                    MainWindow.window.Finish();
                });
            }
        }

        public static List<string> ReadDataFromFile(string file) {
            List<string> lines = new List<string>();

            string line = "";
            using (FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None)) {
                try {
                    using (StreamReader sr = new StreamReader(fs)) {
                        while ((line = sr.ReadLine()) != null) {
                            lines.Add(line);
                        }
                    }
                } catch (Exception e) {
                    Console.WriteLine(e.Message);
                } finally {
                    fs.Close();
                }
            }

            return lines;
        }

        public static void StartLogThread(string fileName) {
            if (logThread == null) {
                logFileName = fileName;
                itemsForLog = new ConcurrentQueue<string>();
                logThread = new Thread(new ThreadStart(LogThread));
                logThread.Start();
            } else {
                //if (logThread.ThreadState != ThreadState.Running) {
                //    logThread.Resume();
                //}
            }
        }

        public static void StopLogThread() {
            if (logThread != null) {
                logThread.Abort();
            }
        }

        private static void LogThread() {
            try {
                do {
                    if (!itemsForLog.IsEmpty) {
                        string item = "";
                        if (itemsForLog.TryDequeue(out item)) {
                            //item.Replace("\n", Environment.NewLine);
                            //File.AppendAllText(logFileName, item, Encoding.UTF8);
                            SaveToLog(item);
                        } else {
                            throw new Exception("Unable to remove item from queue!");
                        }
                    } else {
                        Thread.Sleep(5);
                    }
                } while (true);
            } catch (ThreadAbortException) {
                Console.WriteLine("LogThread was aborted!");
            }
        }

        public static void AddToLogQueue(string item) {
            itemsForLog.Enqueue(item);
        }

        public static void SaveToLog(string item) {
            if (RECORD_LOG) {
                if (!File.Exists(logFileName)) {
                    File.Create(logFileName).Close();
                }

                try {
                    using (FileStream fs = new FileStream(logFileName, FileMode.Append, FileAccess.Write, FileShare.None)) {
                        try {
                            using (StreamWriter sw = new StreamWriter(fs)) {
                                sw.WriteLine(item);
                                sw.Flush();
                            }
                        } catch (Exception e) {
                            Console.WriteLine(e.Message);
                        } finally {
                            fs.Close();
                        }
                    }
                } catch (IOException ioe) {
                    Console.WriteLine(ioe.Message);
                }
            } else {
                Console.WriteLine("Log Disabled!");
            }
        }

        public static void SaveToLog(List<string> items) {
            Microsoft.Win32.SaveFileDialog fileDialog = new Microsoft.Win32.SaveFileDialog();
            fileDialog.Filter = "Text file |*.txt";
            fileDialog.Title = "Save Log";
            fileDialog.ShowDialog();

            if (!string.IsNullOrEmpty(fileDialog.FileName)) {
                File.Create(fileDialog.FileName);
                File.WriteAllLines(fileDialog.FileName, items.ToArray());
            }
        }
    }

    [Serializable]
    public class Rainfall {
        public int XRef, YRef;
        public int year;
        public int[] values = new int[12];  //each month value
    }
}
