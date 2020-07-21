using CliWrap;
using CliWrap.EventStream;
using FFUITools.Wpf.Extentions;
using Ookii.Dialogs.Wpf;
using Stylet;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;


namespace FFUITools.Wpf.Pages
{
    public class MainViewModel : Screen, IDisposable
    {
        StringBuilder log = new StringBuilder();
        CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

        private string _directoryName;
        public string DirectoryName
        {
            get { return this._directoryName; }
            set
            {
                SetAndNotify(ref this._directoryName, value);
            }
        }

        private string _outputFile;
        public string OutputFile
        {
            get { return this._outputFile; }
            set
            {
                SetAndNotify(ref this._outputFile, value);
                this.NotifyOfPropertyChange(() => this.CanConcatenateJob);
            }
        }

        private string _outputLog;
        public string OutputLog
        {
            get { return this._outputLog; }
            set
            {
                SetAndNotify(ref this._outputLog, value);
            }
        }

        private List<FileInfo> _filesInFolder = new List<FileInfo>();
        public List<FileInfo> FilesInFolder
        {
            get { return _filesInFolder; }
            set 
            { 
                SetAndNotify(ref _filesInFolder, value);
                this.NotifyOfPropertyChange(() => this.CanConcatenateJob);
            }
        }

        private Visibility _progressBarVisibility;
        public Visibility ProgressBarVisibility
        {
            get { return _progressBarVisibility; }
            set { SetAndNotify(ref _progressBarVisibility, value); }
        }

        public MainViewModel()
        {
            //this.DisplayName = "Главная";
            ProgressBarVisibility = Visibility.Collapsed;
        }

        public void SelectDirectoryDialog()
        {
            OutputLog = String.Empty;
            log = new StringBuilder();
            var folderDialog = new VistaFolderBrowserDialog();
            folderDialog.ShowDialog();
            DirectoryName = folderDialog.SelectedPath;

            if (String.IsNullOrEmpty(DirectoryName))
            {
                DirectoryName = @"C:\";
                log.AppendLine($"Внимание! Путь не найден! Установлена следующая директория: {DirectoryName}");
            }

            FilesInFolder = new DirectoryInfo(DirectoryName).GetFiles().Where(x => x.Extension == ".mp4").ToList();
            foreach (var item in FilesInFolder)
            {
                log.AppendLine($"{item.FullName}");
            }

            log.AppendLine($"Найдено {FilesInFolder.Count} файлов");
            OutputLog = log.ToString();
        }

        public void SetOutputFile()
        {
            var fileSaveDialog = new VistaSaveFileDialog();
            fileSaveDialog.ShowDialog();
            var file = fileSaveDialog.FileName;
            if (File.Exists(file))
            {
                File.Delete(file);
            }

            OutputFile = file;
        }

        public void CancelJob()
        {
            log.AppendLine("\nЗадание отменено!");
            cancellationTokenSource.Cancel();
            OutputLog = log.ToString();
            ProgressBarVisibility = Visibility.Collapsed;
        }

        public async Task ConcatenateJob()
        {
            string[] filesInArray = FilesInFolder.Select(s => s.FullName).ToArray();
            DirectoryInfo tempDir = Directory.CreateDirectory(Path.Combine(DirectoryName, "temp"));

            log.AppendLine("Создаю временные файлы...");
            await ConcatToTempFiles(filesInArray, tempDir);

            log.AppendLine($"Объединяю временные файлы в файл {OutputFile}... ");
            await ConcatToSingleFile(tempDir);
        }

        public bool CanConcatenateJob
        {
            get { return !String.IsNullOrEmpty(OutputFile) && FilesInFolder.Count > 0; }
        }

        /// <summary>
        /// Split the array into pieces and run the concatinate process for each piece to temporary directory
        /// </summary>
        /// <param name="filesInArray">files for concatinations</param>
        /// <param name="tempDir">temporary directory for output files</param>
        /// <returns>Task</returns>
        private async Task ConcatToTempFiles(string[] filesInArray, DirectoryInfo tempDir)
        {
            var splitFiles = filesInArray.Split(300).ToArray();

            for (int i = 0; i < splitFiles.Length; i++)
            {
                var fileName = Path.Combine(tempDir.FullName, $"{i + 1}.mp4");
                var command = Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), "ffmpeg.exe");
                var arguments = $"-i \"concat:{string.Join("|", splitFiles[i])}\" -c copy \"{fileName}\"";
                var cmd = Cli.Wrap(command).WithArguments(arguments);

                await foreach (var cmdEvent in cmd.ListenAsync(cancellationTokenSource.Token))
                {
                    switch (cmdEvent)
                    {
                        case StartedCommandEvent started:
                            log.AppendLine($"Process started; ID: {started.ProcessId}");
                            ProgressBarVisibility = Visibility.Visible;
                            OutputLog = log.ToString();
                            break;
                        case ExitedCommandEvent exited:
                            ProgressBarVisibility = Visibility.Collapsed;
                            log.AppendLine($"Process exited; Code: {exited.ExitCode}");
                            OutputLog = log.ToString();

                            if (exited.ExitCode == 0)
                            {
                                var file = new FileInfo(fileName);
                                log.AppendLine($"{new string('*', 56)}");
                                log.AppendLine($"Завершено {i + 1} из {splitFiles.Length}");
                                log.AppendLine($"Создан временный фаил: {file.FullName}, размер: {file.Length / (1024 * 1024)} Мб");
                                log.AppendLine($"{new string('*', 56)}");

                                OutputLog = log.ToString();
                            }
                            break;
                    }
                }
            }
        }

        /// <summary>
        /// Take all files from temporary directory and concatenate them into single file. Delete all temp files after task is done.
        /// </summary>
        /// <param name="tempDir">temporary directory</param>
        /// <returns>Task</returns>
        private async Task ConcatToSingleFile(DirectoryInfo tempDir)
        {
            var partsFiles = new DirectoryInfo(tempDir.FullName).GetFiles().Where(x => x.Extension == ".mp4").Select(s => s.FullName).ToArray();

            var tempFile = Path.Combine(DirectoryName, "temp.txt");
            if (!File.Exists(tempFile))
            {
                // Create a file to write to.
                using (StreamWriter sw = File.CreateText(tempFile))
                {
                    foreach (var file in partsFiles)
                    {
                        sw.WriteLine($"file \'{file}\'");
                    }
                }
            }

            var command = Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), "ffmpeg.exe");
            var arguments = $"-f concat -safe 0 -i \"{tempFile}\" -c copy \"{OutputFile}\"";
            var cmd = Cli.Wrap(command).WithArguments(arguments);

            await foreach (var cmdEvent in cmd.ListenAsync(cancellationTokenSource.Token))
            {
                switch (cmdEvent)
                {
                    case StartedCommandEvent started:
                        log.AppendLine($"Process started; ID: {started.ProcessId}");
                        ProgressBarVisibility = Visibility.Visible;
                        OutputLog = log.ToString();
                        break;
                    case StandardErrorCommandEvent stdErr:
                        log.Append($"Err> {stdErr.Text}");
                        OutputLog = log.ToString();
                        break;
                    case ExitedCommandEvent exited:
                        ProgressBarVisibility = Visibility.Collapsed;
                        log.AppendLine($"Process exited; Code: {exited.ExitCode}");
                        log.AppendLine("");
                        OutputLog = log.ToString();

                        if (exited.ExitCode == 0)
                        {
                            var file = new FileInfo(OutputFile);
                            log.AppendLine($"{new string('*', 56)}");
                            log.AppendLine($"ВЫПОЛНЕНО!");
                            log.AppendLine($"Итоговый фаил: {file.FullName}, размер: {file.Length / (1024 * 1024)} Мб");

                            if (Directory.Exists(tempDir.FullName))
                            {
                                log.AppendLine($"Удаляю временную директорию...");
                                Directory.Delete(tempDir.FullName, true);
                            }

                            if (File.Exists(tempFile))
                            {
                                log.AppendLine($"Удаляю временные файлы...");
                                File.Delete(tempFile);
                            }

                            log.AppendLine($"{new string('*', 56)}");
                            OutputLog = log.ToString();
                        }
                        break;
                }
            }
        }

        public void Dispose()
        {

        }
    }
}

