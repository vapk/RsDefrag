using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RomanDefrag
{
    class Program
    {
        static int Main(string[] args)
        {
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
            var files = new DirectoryInfo(path).GetFiles();
            int totalDone = 0;
            int perfectGaps = 0;
            foreach (var file in files)
            {
                if (file.Length == 0)
                    continue;
                if (DefragOne(volumeName, file.FullName))
                    perfectGaps++;
                totalDone++;
                Console.Title = $"Total done: {totalDone:#,0}, perfect gaps: {perfectGaps:#,0}";
            }
        }

        static bool DefragOne(string volumeName, string fileName)
        {
            Console.WriteLine($"File: {fileName}");
            var filemap = IOWrapper.GetFileMap(fileName);
            Console.WriteLine($"File fragmentation: {filemap.Length:#,0} fragments.");
            if (filemap.Length == 1)
                return false;
            var fileLength = filemap[filemap.Length - 1].Vcn;
            Console.WriteLine($"File length: {fileLength:#,0} clusters.");

            var map = IOWrapper.GetVolumeMap(volumeName);
            Console.WriteLine($"Total clusters in volume: {map.Length:#,0}");

            int freeClusters = 0;
            var gaps = new SortedSet<Gap>();
            int i = 0;
            while (i < map.Length)
            {
                while (i < map.Length && map[i])
                    i++;
                int gapStart = i;
                while (i < map.Length && !map[i])
                {
                    freeClusters++;
                    i++;
                }
                if (i != gapStart)
                    gaps.Add(new Gap { StartLcn = gapStart, Length = i - gapStart });
            }
            Console.WriteLine($"Free clusters: {freeClusters:#,0} in {gaps.Count:#,0} contiguous spans.");
            Console.WriteLine($"Largest gap: {gaps.First().Length:#,0} clusters at LCN={gaps.First().StartLcn:#,0}");

            Gap bestGap = default(Gap);
            foreach (var gap in gaps)
            {
                if (gap.Length >= fileLength * 2)
                    bestGap = gap;
                if (gap.Length == fileLength)
                {
                    bestGap = gap;
                    break;
                }
                if (gap.Length < fileLength)
                    break;
            }

            if (bestGap.Length == 0)
            {
                Console.WriteLine();
                Console.WriteLine("ERROR: no suitable gap found");
                throw new Exception();
            }
            Console.WriteLine($"Best gap: {bestGap.Length:#,0} clusters at LCN={bestGap.StartLcn:#,0}");

            var curVcn = 0;
            var curLcn = bestGap.StartLcn;
            foreach (var filepart in filemap)
            {
                int count = checked((int) (filepart.Vcn - curVcn));
                if (curLcn == filepart.Lcn + count)
                {
                    Console.WriteLine($"Skipping VCN={curVcn} because it's already where it should be");
                    curVcn += count;
                    continue;
                }
                Console.WriteLine($"Moving file VCN={curVcn} to LCN={curLcn} ({count} clusters)");
                IOWrapper.MoveFile(volumeName, fileName, curVcn, curLcn, count);
                curVcn += count;
                curLcn += count;
            }

            Console.WriteLine($"Finished!");
            return bestGap.Length == fileLength;
        }

        struct Gap : IComparable<Gap>
        {
            public long StartLcn;
            public long Length;

            public override string ToString() => $"Length = {Length:#,0}, StartLcn = {StartLcn:#,0}";

            int IComparable<Gap>.CompareTo(Gap other)
            {
                if (this.Length > other.Length)
                    return -1;
                if (this.Length < other.Length)
                    return 1;
                if (this.StartLcn > other.StartLcn)
                    return -1;
                if (this.StartLcn < other.StartLcn)
                    return 1;
                return 0;
            }
        }
    }
}
