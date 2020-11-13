using CliWrap;
using CliWrap.Buffered;
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
using System.Text.RegularExpressions;
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

        private string _ffmpgVersion;
        public string FfmpgVersion
        {
            get { return this._ffmpgVersion; }
            set
            {
                SetAndNotify(ref this._ffmpgVersion, value);
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
            Execute.OnUIThreadAsync(async () => await GetFfmpegVersion());
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
            var tempFile = Path.Combine(DirectoryName, "temptsfiles.txt");

            log.AppendLine("Создаю временные файлы...");
            await ConcatToTsFiles(filesInArray, tempFile);

            log.AppendLine($"Объединяю временные файлы в файл {OutputFile}... ");
            await ConcatToSingleFile(tempFile);
        }

        public bool CanConcatenateJob
        {
            get { return !String.IsNullOrEmpty(OutputFile) && FilesInFolder.Count > 0; }
        }

        /// <summary>
        /// Take all files from directory and concatenate them into single file. Delete all temp files after task is done.
        /// </summary>
        /// <param name="tempFile">temporary file with ts paths</param>
        /// <returns>Task</returns>
        private async Task ConcatToSingleFile(string tempFile)
        {
            var command = Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), "ffmpeg.exe");
            var arguments = $"-fflags +genpts -safe 0 -f concat -i \"{tempFile}\" -c copy \"{OutputFile}\"";
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
                        log.AppendLine("");
                        OutputLog = log.ToString();

                        if (exited.ExitCode == 0)
                        {
                            //log.AppendLine($"{new string('*', 56)}");
                            //log.AppendLine($"Удаляю временные файлы...");
                            //FilesInFolder = new DirectoryInfo(DirectoryName).GetFiles().Where(x => x.Extension == ".ts").ToList();
                            //foreach (var item in FilesInFolder)
                            //{
                            //    log.AppendLine($"Удаляю {item.FullName}...");
                            //    File.Delete($"{item.FullName}");
                            //}

                            var file = new FileInfo(OutputFile);
                            log.AppendLine($"{new string('*', 56)}");
                            log.AppendLine($"ВЫПОЛНЕНО!");
                            log.AppendLine($"Итоговый фаил: {file.FullName}, размер: {file.Length / (1024 * 1024)} Мб");

                            log.AppendLine($"{new string('*', 56)}");
                            OutputLog = log.ToString();
                        }
                        break;
                }
            }
        }

        private async Task ConcatToTsFiles(string[] filesInArray, string tempFile)
        {
            for (int i = 0; i < filesInArray.Length; i++)
            {
                var mp4FileName = filesInArray[i];
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
                        //case StartedCommandEvent started:
                        //    log.AppendLine($"Process started; ID: {started.ProcessId}");
                        //    ProgressBarVisibility = Visibility.Visible;
                        //    OutputLog = log.ToString();
                        //    break;
                        case ExitedCommandEvent exited:
                            ProgressBarVisibility = Visibility.Collapsed;
                            //log.AppendLine($"Process exited; Code: {exited.ExitCode}");
                            //log.AppendLine("");
                            //OutputLog = log.ToString();

                            if (exited.ExitCode == 0)
                            {
                                log.AppendLine($"Выполнено {i + 1} из {filesInArray.Length}");
                                log.AppendLine($"{new string('*', 56)}");
                                OutputLog = log.ToString();
                            }
                            break;
                    }
                }
            }

        }

        private async Task GetFfmpegVersion()
        {
            var command = Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), "ffmpeg.exe");
            var arguments = " -version";
            var result = await Cli.Wrap(command).WithArguments(arguments).ExecuteBufferedAsync();
            if (string.IsNullOrEmpty(result.StandardOutput))
            {
                FfmpgVersion = "Не могу найти ffmpeg!\n Скачайте ffmpeg.exe с сайта https://ffmpeg.org/ и положите в папку с программой";
            }
            else
            {
                var output = result.StandardOutput;
                var findNewLine = Regex.Match(output, @"(\r\n|\r|\n)");
                FfmpgVersion = $"Установлен!\n{output.Remove(findNewLine.Index)}";
            }
        }

        public void Dispose()
        {

        }
    }
}

