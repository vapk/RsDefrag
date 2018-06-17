using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace RomanDefrag
{
    class GapStore : IEnumerable<GapStore.Gap>
    {
        public struct Gap
        {
            public long StartLcn { get; private set; }
            public long Length { get; private set; }

            public override string ToString() => $"Length = {Length:#,0}, StartLcn = {StartLcn:#,0}";

            public Gap(long startLcn, long length)
            {
                StartLcn = startLcn;
                Length = length;
            }

            public class ComparerByLcn : IComparer<Gap> { public int Compare(Gap x, Gap y) => x.StartLcn.CompareTo(y.StartLcn); }
            public class ComparerByLengthDesc : IComparer<Gap> { public int Compare(Gap x, Gap y) => -x.Length.CompareTo(y.Length); }
        }

        private class comparerLongDesc : IComparer<long> { public int Compare(long x, long y) => -x.CompareTo(y); }

        private SortedDictionary<long, List<Gap>> _gapsByLength = new SortedDictionary<long, List<Gap>>(new comparerLongDesc());
        private List<Gap> _gapsByLcn = new List<Gap>();

        public GapStore(IEnumerable<Gap> gaps)
        {
            // Does not look for overlaps; assumes there aren't any
            _gapsByLcn = gaps.OrderBy(g => g.StartLcn).ToList();
            foreach (var grp in gaps.GroupBy(g => g.Length))
                _gapsByLength.Add(grp.Key, grp.ToList());
        }

        public int Count { get => _gapsByLcn.Count; }
        IEnumerator<Gap> IEnumerable<Gap>.GetEnumerator() => _gapsByLength.SelectMany(grp => grp.Value).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => (this as IEnumerable<Gap>).GetEnumerator();

        public void AddGap(long startLcn, int length)
        {
            // Mark Russinovich says that NTFS does not immediately make these clusters available again, so in case of exceptions this needs to be delayed by a few seconds
            
            // Case 1: does not touch any other gaps
            // Case 2: touches a gap at the start
            // Case 3: touches a gap at the end
            // Case 3: touches a gap at both ends

            // NOT IMPLEMENTED - meaning the defragmenter will not ever use gaps left behind by clusters moved elsewhere
        }

        public void DeleteGap(long startLcn, int length)
        {
            var nearest = _gapsByLcn.BinarySearch(new Gap(startLcn, length), new Gap.ComparerByLcn());
            // Case 1: exactly matches an entire gap
            if (nearest >= 0 && length == _gapsByLcn[nearest].Length)
            {
                var oldgap = _gapsByLcn[nearest];
                _gapsByLcn.RemoveAt(nearest);
                if (!_gapsByLength[oldgap.Length].Remove(oldgap))
                    throw new Exception("Inconsistent data structure");
            }
            // Case 2: at the start of an existing gap
            else if (nearest >= 0 && length < _gapsByLcn[nearest].Length)
            {
                var oldgap = _gapsByLcn[nearest];
                var newgap = new Gap(oldgap.StartLcn + length, oldgap.Length - length);
                if (!_gapsByLength[oldgap.Length].Remove(oldgap))
                    throw new Exception("Inconsistent data structure");
                if (!_gapsByLength.ContainsKey(newgap.Length))
                    _gapsByLength.Add(newgap.Length, new List<Gap>(capacity: 2));
                _gapsByLength[newgap.Length].Add(newgap);
                _gapsByLcn[nearest] = newgap;

            }
            // Case 3: at the end of an existing gap (cannot happen due to caller behaviour)
            // Case 4: in the middle of an existing gap (cannot happen due to caller behaviour)
            // Case 5: overlaps multiple gaps (cannot happen due to caller behaviour)
            else
                throw new NotImplementedException();
        }
    }

    class VolumeDefragmenter
    {
        private string _volumeName;
        private GapStore _gaps;

        public VolumeDefragmenter(string volumeName)
        {
            _volumeName = volumeName;
            RebuildVolumeMap();
        }

        public void RebuildVolumeMap()
        {
            var map = IOWrapper.GetVolumeMap(_volumeName);
            Console.WriteLine($"Total clusters in volume: {map.Length:#,0}");

            int freeClusters = 0;
            int i = 0;
            var gaps = new List<(long lcn, long length)>();
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
                    gaps.Add((lcn: gapStart, length: i - gapStart));
            }
            _gaps = new GapStore(gaps.Select(g => new GapStore.Gap(g.lcn, g.length)));
            Console.WriteLine($"Free clusters: {freeClusters:#,0} in {_gaps.Count:#,0} contiguous spans.");
            Console.WriteLine($"Largest gap: {_gaps.First().Length:#,0} clusters at LCN={_gaps.First().StartLcn:#,0}");
        }

        public bool DefragFile(string fileName)
        {
            Console.WriteLine($"File: {fileName}");
            var filemap = IOWrapper.GetFileMap(fileName);
            Console.WriteLine($"File fragmentation: {filemap.Length:#,0} fragments.");
            if (filemap.Length == 1)
                return false;
            var fileLength = filemap[filemap.Length - 1].Vcn;
            Console.WriteLine($"File length: {fileLength:#,0} clusters.");

            var bestGap = default(GapStore.Gap);
            foreach (var gap in _gaps)
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
                //Console.WriteLine($"Moving file VCN={curVcn} from LCN={filepart.Lcn} to LCN={curLcn} ({count} clusters)");
                IOWrapper.MoveFile(_volumeName, fileName, curVcn, curLcn, count);
                _gaps.DeleteGap(curLcn, count);
                _gaps.AddGap(filepart.Lcn, count);
                curVcn += count;
                curLcn += count;
            }

            Console.WriteLine($"Finished!");
            return bestGap.Length == fileLength;
        }
    }
}
