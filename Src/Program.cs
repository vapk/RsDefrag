using System;
using System.IO;

namespace RomanDefrag
{
    class Program
    {
        static int Main(string[] args)
        {
            //AnalyzeDirectory("C:", @"C:\Temp");
            //DefragFile("C:", @"C:\Temp\foo.txt"););
            DefragDirectory("C:", @"C:\Temp");
            return 0;
        }

        static void AnalyzeDirectory(string volumeName, string path)
        {
            var files = new DirectoryInfo(path).GetFiles();
            int totalFragments = 0;
            int totalFiles = 0;
            foreach (var file in files)
            {
                if (file.Length == 0)
                    continue;
                totalFiles++;
                totalFragments += AnalyzeFile(volumeName, file.FullName);
            }
            Console.WriteLine($"TOTAL: {files.Length} files, {totalFragments} fragments ({totalFragments - files.Length} extra fragments)");
        }

        static int AnalyzeFile(string volumeName, string fileName)
        {
            var filemap = IOWrapper.GetFileMap(fileName);
            Console.WriteLine($"{fileName} --- {filemap.Length:#,0} fragments");
            return filemap.Length;
        }

        static void DefragDirectory(string volumeName, string path)
        {
            var defragger = new VolumeDefragmenter(volumeName);

            var files = new DirectoryInfo(path).GetFiles();
            int totalDone = 0;
            int perfectGaps = 0;
            foreach (var file in files)
            {
                if (file.Length == 0)
                    continue;
                if (defragger.DefragFile(file.FullName))
                    perfectGaps++;
                totalDone++;
                Console.Title = $"Total done: {totalDone:#,0}, perfect gaps: {perfectGaps:#,0}";
            }
        }

        static bool DefragFile(string volumeName, string fileName)
        {
            var defragger = new VolumeDefragmenter(volumeName);
            return defragger.DefragFile(fileName);
        }
    }
}
