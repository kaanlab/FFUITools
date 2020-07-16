
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
//using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Xabe.FFmpeg;

namespace FFUITools.ConsoleApp
{
    class Program
    {
        static async Task Main(string[] args)
        {
            string outputFile = @"D:\new2\output.mp4";

            string[] filesInArray = GetFilesToConvert(@"D:\new2").Select(s => s.FullName).ToArray();
            FFmpeg.SetExecutablesPath(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location));
            var conversion = await FFmpeg.Conversions.FromSnippet.Concatenate(outputFile, filesInArray);

            conversion.UseMultiThread(false);
            conversion.OnProgress += Conversion_OnProgress;

            //conversion.OnDataReceived += Conversion_OnDataReceived;
            var result = await conversion.Start();
            
            var total = result.EndTime - result.StartTime;
            if(File.Exists(outputFile))
            {
                var file = new FileInfo(outputFile);
                Console.WriteLine($"Имя файла: {file.FullName}, размер файла: {file.Length/(1024*1024)} Мб, время: {total.ToString(@"hh\:mm\:ss")}");
                
            }





        }

        private static void Conversion_OnDataReceived(object sender, DataReceivedEventArgs e)
        {
            Console.WriteLine(e.Data);
        }

        private static void Conversion_OnProgress(object sender, Xabe.FFmpeg.Events.ConversionProgressEventArgs args)
        {
            Console.WriteLine($"время: {args.Duration}");
        }

        private static async Task Run()
        {
            //Queue<FileInfo> filesToConvert = new Queue<FileInfo>(GetFilesToConvert("D:\\new"));

            //filesToConvert.Count();
            //await Console.Out.WriteLineAsync($"Find {filesToConvert.Count()} files to convert.");
            //var arguments = "-version";
            //var dd = await FFmpeg.Conversions.New().Build(arguments);
            //Console.WriteLine(dd);
            await SetFFmpeg();

            var files = GetFilesToConvert(@"D:\new1");
            string[] a = files.Select(s => s.FullName).ToArray();
            //Run conversion
            await RunConcat(a);


        }

        private string CreateRandomNameDirectory()
        {
            var uniqueDir = Path.Combine("uploadfolder", Path.GetRandomFileName().Substring(0, 6));
            Directory.CreateDirectory(uniqueDir);
            return uniqueDir;
        }

        private static async Task RunConcat(string[] filesToConcat)
        {

            //Save file to the same location with changed extension
            Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location));
            string outputFileName = Path.ChangeExtension(Path.GetTempFileName(), ".mp4");



            await Console.Out.WriteLineAsync($"Finished converion file [{outputFileName}]");

        }

        private static async Task SetFFmpeg()
        {
            //Set directory where the app should look for FFmpeg executables.

        }

        private static IEnumerable<FileInfo> GetFilesToConvert(string directoryPath)
        {
            //Return all files excluding mp4 because I want convert it to mp4
            return new DirectoryInfo(directoryPath).GetFiles().Where(x => x.Extension == ".mp4");
        }
    }
}
