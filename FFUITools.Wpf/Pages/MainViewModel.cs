using CliWrap;
using CliWrap.Buffered;
using CliWrap.EventStream;
using Stylet;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;

namespace FFUITools.Wpf.Pages
{
    public class MainViewModel : Stylet.Screen
    {
        private StringBuilder log = new StringBuilder();
        private CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

        private bool _canCancelJob;
        public bool CanCancelJob
        {
            get { return this._canCancelJob; }
            set
            {
                SetAndNotify(ref this._canCancelJob, value);
            }
        }

        private bool _IsFfmpegInstalled;
        public bool IsFfmpegInstalled
        {
            get { return this._IsFfmpegInstalled;  }
            set
            {
                SetAndNotify(ref this._IsFfmpegInstalled, value);
                this.NotifyOfPropertyChange(() => this.CanDownloadFfmpeg);
            }
        }

        private double _progressPercentage;
        public double ProgressPercentage
        {
            get { return this._progressPercentage; }
            set
            {
                SetAndNotify(ref this._progressPercentage, value);
            }
        }

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

        private string _ffmpegVersion;
        public string FfmpegVersion
        {
            get { return this._ffmpegVersion; }
            set
            {
                SetAndNotify(ref this._ffmpegVersion, value);
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

        private Visibility _progressBarVisibilityPercentage;
        public Visibility ProgressBarVisibilityPercentage
        {
            get { return _progressBarVisibilityPercentage; }
            set { SetAndNotify(ref _progressBarVisibilityPercentage, value); }
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
            ProgressBarVisibilityPercentage = Visibility.Collapsed;

            Execute.OnUIThreadAsync(async () => await GetFfmpegVersion());
        }

        public void SelectDirectoryDialog()
        {
            OutputLog = String.Empty;
            log = log.Clear();

            var folderDialog = new FolderBrowserDialog();
            folderDialog.ShowDialog();

            DirectoryName = folderDialog.SelectedPath;

            if (String.IsNullOrEmpty(DirectoryName))
            {
                DirectoryName = @"C:\";
                log.AppendLine($"Внимание! Путь не найден! Установлена следующая директория: {DirectoryName}");
            }

            FilesInFolder = new DirectoryInfo(DirectoryName).GetFiles().Where(x => x.Extension == ".mp4").ToList();
            var filesName = GetFileNames(FilesInFolder);
            for (int i = 0; i < filesName.Length; i++)
            {
                log.AppendLine(filesName.Span[i]);
            }
            log.AppendLine($"Найдено {filesName.Length} файлов");
            OutputLog = log.ToString();
        }

        public void SetOutputFile()
        {
            var fileSaveDialog = new SaveFileDialog();

            fileSaveDialog.Filter = "Video Files (*.mp4)|*.mp4";
            fileSaveDialog.DefaultExt = "mp4";
            fileSaveDialog.AddExtension = true;
            fileSaveDialog.ShowDialog();

            var file = fileSaveDialog.FileName;

            if (File.Exists(file))
            {
                File.Delete(file);
            }

            OutputFile = file;
            log.AppendLine($"\nФаил для хранения: {file}");
            OutputLog = log.ToString();
        }

        public void CancelJob()
        {
            cancellationTokenSource.Cancel();

            log.AppendLine("\nЗадание отменено!");
            OutputLog = log.ToString();
            ProgressBarVisibility = Visibility.Collapsed;
            CanCancelJob = false;
        }

        public void Clear()
        {
            cancellationTokenSource = new CancellationTokenSource();
            OutputLog = String.Empty;
            DirectoryName = String.Empty;
            FilesInFolder = new List<FileInfo>();
            OutputFile = String.Empty;
            log = log.Clear();
            ProgressBarVisibility = Visibility.Collapsed;
        }

        public bool CanConcatenateJob
        {
            get { return !String.IsNullOrEmpty(OutputFile) && FilesInFolder.Count > 0 && IsFfmpegInstalled; }
        }

        public async Task ConcatenateJob()
        {
            try
            {
                CanCancelJob = true;
                var filesInArray = GetFileNames(FilesInFolder);
                var tempFile = Path.Combine(DirectoryName, "temptsfiles.txt");

                log.AppendLine("Создаю временные файлы...");
                await ConcatToTsFiles(filesInArray, tempFile);

                log.AppendLine($"Объединяю временные файлы в файл {OutputFile}... ");
                await ConcatToSingleFile(tempFile);
                CanCancelJob = false;
            }
            catch (Exception ex)
            {
                log.AppendLine("Операция завершена с ошибкой:");
                log.Append(ex.Message);
                OutputLog = log.ToString();
            }
        }

        public TimeSpan Timeout { get; private set; }

        /// <summary>
        /// Take all files from directory and concatenate them into single file. Delete all temp files after task is done.
        /// </summary>
        /// <param name="tempFile">temporary file with ts paths</param>
        /// <returns>Task</returns>
        private async Task ConcatToSingleFile(string tempFile)
        {
            try
            {
                ProgressBarVisibility = Visibility.Visible;

                var command = Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), "ffmpeg.exe");
                var arguments = $"-fflags +genpts -safe 0 -f concat -i \"{tempFile}\" -c copy \"{OutputFile}\"";
                var cmd = Cli.Wrap(command).WithArguments(arguments);
                await foreach (var cmdEvent in cmd.ListenAsync(cancellationTokenSource.Token))
                {
                    switch (cmdEvent)
                    {
                        case StartedCommandEvent started:
                            log.AppendLine($"Process started; ID: {started.ProcessId}");
                            OutputLog = log.ToString();
                            break;
                        case ExitedCommandEvent exited:
                            log.AppendLine($"Process exited; Code: {exited.ExitCode}\n");
                            OutputLog = log.ToString();

                            if (exited.ExitCode == 0)
                            {
                                log.AppendLine($"{new string('*', 64)}");
                                log.AppendLine($"Удаляю временные файлы...");
                                FilesInFolder = new DirectoryInfo(DirectoryName).GetFiles().Where(x => x.Extension == ".ts").ToList();
                                var fileNames = GetFileNames(FilesInFolder);
                                for (int i = 0; i < fileNames.Length; i++)
                                {
                                    File.Delete($"{fileNames.Span[i]}");
                                    log.AppendLine($"Удаляю {fileNames.Span[i]} ...");
                                }

                                File.Delete(tempFile);
                                log.AppendLine($"Удаляю {tempFile} ...");

                                var outputFile = new FileInfo(OutputFile);
                                log.AppendLine($"{new string('*', 64)}");
                                log.AppendLine($"ВЫПОЛНЕНО!");
                                log.AppendLine($"Итоговый фаил: {outputFile.FullName}, размер: {outputFile.Length / (1024 * 1024)} Мб");
                                log.AppendLine($"{new string('*', 64)}");
                                OutputLog = log.ToString();
                            }
                            break;
                    }
                }
                ProgressBarVisibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                log.AppendLine("Операция завершена с ошибкой:");
                log.Append(ex.Message);
                OutputLog = log.ToString();
            }
        }

        private async Task ConcatToTsFiles(ReadOnlyMemory<string> filesInArray, string tempFile)
        {
            try
            {
                ProgressBarVisibilityPercentage = Visibility.Visible;
                log.AppendLine($"{new string('*', 64)}");

                var onePercent = 100 / (double)filesInArray.Length;
                for (int i = 0; i < filesInArray.Length; i++)
                {
                    var mp4FileName = filesInArray.Span[i];
                    var fileWithoutExtenton = mp4FileName.Remove(mp4FileName.Length - 4);
                    var tsFileName = $"{fileWithoutExtenton}.ts";

                    await File.AppendAllTextAsync(tempFile, $"file \'{tsFileName}\'\n");

                    var command = Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), "ffmpeg.exe");
                    var arguments = $"-i \"{mp4FileName}\" -c copy -bsf:v h264_mp4toannexb -f mpegts \"{tsFileName}\"";
                    var cmd = Cli.Wrap(command).WithArguments(arguments);


                    await foreach (var cmdEvent in cmd.ListenAsync(cancellationTokenSource.Token))
                    {
                        switch (cmdEvent)
                        {
                            case StartedCommandEvent started:
                                //    log.AppendLine($"Process started; ID: {started.ProcessId}");
                                //   ProgressBarVisibility = Visibility.Visible;
                                //    OutputLog = log.ToString();
                                break;
                            case ExitedCommandEvent exited:
                                //ProgressBarVisibility = Visibility.Collapsed;
                                //log.AppendLine($"Process exited; Code: {exited.ExitCode}");
                                //log.AppendLine("");
                                //OutputLog = log.ToString();

                                if (exited.ExitCode == 0)
                                {
                                    ProgressPercentage += onePercent;
                                    log.AppendLine($"Выполнено {i + 1} из {filesInArray.Length}");
                                    OutputLog = log.ToString();
                                }
                                break;
                        }
                    }

                }
                log.AppendLine($"{new string('*', 64)}");
                OutputLog = log.ToString();
                ProgressBarVisibilityPercentage = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                log.AppendLine("Операция завершена с ошибкой:");
                log.Append(ex.Message);
                OutputLog = log.ToString();
            }
        }

        private async Task GetFfmpegVersion()
        {
            try
            {
                if (CheckIfFfmpegExists())
                {
                    var command = Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), "ffmpeg.exe");
                    var arguments = " -version";
                    var result = await Cli.Wrap(command).WithArguments(arguments).ExecuteBufferedAsync();
                    var output = result.StandardOutput;
                    var findNewLine = Regex.Match(output, @"(\r\n|\r|\n)");
                    FfmpegVersion = $"{output.Remove(findNewLine.Index)}";
                    IsFfmpegInstalled = true;
                }
                else
                {
                    FfmpegVersion = "Не могу найти ffmpeg!\nНажмите кнопку скачать и дождитесь установки ffmpeg";
                    IsFfmpegInstalled= false;
                }
            }
            catch (Exception ex)
            {
                log.AppendLine("Операция завершена с ошибкой:");
                log.Append(ex.Message);
                OutputLog = log.ToString();
            }
        }

        public bool CanDownloadFfmpeg
        {
            get { return !CheckIfFfmpegExists(); }
        }

        public async Task DownloadFfmpeg()
        {
            try
            {
                ProgressBarVisibilityPercentage = Visibility.Visible;
                log.AppendLine("Начинаю загрузку ffmpeg ...");
                OutputLog = log.ToString();                
                var workingDir = Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location));
                var fileInfo = new FileInfo($"ffmpeg-release-essentials.zip");
                using (var webClient = new WebClient())
                {
                    webClient.DownloadFileCompleted += (s, e) =>
                    {
                        log.AppendLine("Загрузка завершена!");
                        OutputLog = log.ToString();
                        ExtractFfmpeg(fileInfo.FullName, workingDir);
                    };
                    webClient.DownloadProgressChanged += (s, e) => { ProgressPercentage = e.ProgressPercentage; };
                    await webClient.DownloadFileTaskAsync($"https://www.gyan.dev/ffmpeg/builds/{fileInfo.Name}", fileInfo.FullName);
                }                
                await GetFfmpegVersion();
                ProgressBarVisibilityPercentage = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                log.AppendLine("Операция завершена с ошибкой:");
                log.Append(ex.Message);
                OutputLog = log.ToString();
            }
        }

        private void ExtractFfmpeg(string zipPath, string extractPath)
        {
            try
            {
                using (ZipArchive archive = ZipFile.OpenRead(zipPath))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        if (entry.FullName.Contains("ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            string destinationPath = Path.GetFullPath(Path.Combine(extractPath, entry.Name));
                            entry.ExtractToFile(destinationPath);
                        }
                    }
                }
                File.Delete(zipPath);
            }
            catch (Exception ex)
            {
                log.AppendLine("Операция завершена с ошибкой:");
                log.Append(ex.Message);
                OutputLog = log.ToString();
            }
        }

        private static ReadOnlyMemory<string> GetFileNames(List<FileInfo> filesInFolder) =>
            filesInFolder.Select(x => x.FullName).ToArray();

        private static bool CheckIfFfmpegExists() => 
            File.Exists(Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), "ffmpeg.exe"));

    }
}

