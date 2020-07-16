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
using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;

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

        private string _fileName;
        public string FileName
        {
            get { return this._fileName; }
            set
            {
                SetAndNotify(ref this._fileName, value);
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

        private List<FileInfo> _filesInFolder;
        public List<FileInfo> FilesInFolder
        {
            get { return _filesInFolder; }
            set { SetAndNotify(ref _filesInFolder, value); }
        }

        private Visibility _progressBarVisibility;
        public Visibility ProgressBarVisibility
        {
            get { return _progressBarVisibility; }
            set { SetAndNotify(ref _progressBarVisibility, value); }
        }

        public MainViewModel()
        {
            this.DisplayName = "Главная";
            ProgressBarVisibility = Visibility.Collapsed;
        }

        public async Task SetFFmpeg()
        {
            FFmpeg.SetExecutablesPath(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location));
            if (!File.Exists(Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), "ffmpeg.exe")))
            {
                log.AppendLine("ffmpeg не установлен. Скачиваю последнюю версию...");
                OutputLog = log.ToString();
                await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official);
                log.AppendLine("Готово!");
            }
        }

        public void FolderSelectDialog()
        {
            OutputLog = String.Empty;
            log = new StringBuilder();
            var folderDialog = new VistaFolderBrowserDialog();
            folderDialog.ShowDialog();
            DirectoryName = folderDialog.SelectedPath;

            FilesInFolder = new DirectoryInfo(DirectoryName).GetFiles().Where(x => x.Extension == ".mp4").ToList();
            foreach (var item in FilesInFolder)
            {
                log.AppendLine($"{item.FullName}");
            }

            log.AppendLine($"Добавлено {FilesInFolder.Count} файлов");
            OutputLog = log.ToString();

        }

        public void SetOutputFileName()
        {
            var fileSaveDialog = new VistaSaveFileDialog();
            fileSaveDialog.ShowDialog();
            var file = fileSaveDialog.FileName;
            if (File.Exists(file))
            {
                File.Delete(file);
            }

            FileName = file;
        }

        public void CancelJob()
        {
            log.AppendLine("\nЗадание отменено!");
            cancellationTokenSource.Cancel();
            OutputLog = log.ToString();
            ProgressBarVisibility = Visibility.Collapsed;
        }

        public async Task FFmpegConcatenate()
        {
            string[] filesInArray = FilesInFolder.Select(s => s.FullName).ToArray();
            await SetFFmpeg();
            ProgressBarVisibility = Visibility.Visible;
            log.AppendLine("Запускаю ffmpeg... \n");
            var conversion = await FFmpeg.Conversions.FromSnippet.Concatenate(FileName, filesInArray);
            //conversion.UseMultiThread(false);
            //conversion.OnDataReceived += Conversion_OnDataReceived;

            conversion.OnProgress += Conversion_OnProgress;

            log.AppendLine("Начинаю процесс объединения файлов...");

            var result = await conversion.UseMultiThread(false).Start(cancellationTokenSource.Token);
            OutputLog = log.ToString();

            if (File.Exists(FileName))
            {
                ProgressBarVisibility = Visibility.Collapsed;
                var file = new FileInfo(FileName);
                var total = result.EndTime - result.StartTime;

                log.AppendLine($"\n{new string('*', 50)}");
                log.AppendLine($"ВЫПОЛНЕНО!");
                log.AppendLine($"Фаил: {file.FullName}, размер: {file.Length / (1024 * 1024)} Мб");
                log.AppendLine($"Время: { total.ToString(@"hh\:mm\:ss")}");
                log.AppendLine($"{new string('*', 50)}");

                OutputLog = log.ToString();
            }
        }

        private void Conversion_OnProgress(object sender, Xabe.FFmpeg.Events.ConversionProgressEventArgs args)
        {

            var percent = (int)(args.Duration.TotalSeconds);
            
            OutputLog = log.Append($"{percent}...").ToString();
        }

        public void Dispose()
        {

        }
    }
}
