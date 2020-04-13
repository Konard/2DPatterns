using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ImageMagick;
using Platform.Data;
using Platform.Data.Doublets;
using Platform.Data.Doublets.Decorators;
using Platform.Data.Doublets.Memory.United.Specific;
using Platform.Data.Doublets.Sequences.Converters;
using Platform.Data.Doublets.Sequences.Frequencies.Cache;
using Platform.Data.Doublets.Sequences.Frequencies.Counters;
using Platform.Data.Doublets.Sequences.Indexes;
using Platform.Data.Numbers.Raw;
using Platform.Data.Sequences;
using Platform.Disposables;
using Platform.Memory;
using Platform.Unsafe;

namespace _2DPatterns
{
    public class Patterns : DisposableBase
    {
        private readonly string _sourceImagePath;
        private readonly MagickImage _image;
        private readonly IPixelCollection _pixels;
        private readonly string _linksPath;
        private readonly ILinks<ulong> _links;
        private readonly AddressToRawNumberConverter<ulong> _addressToRawNumberConverter;
        private readonly RawNumberToAddressConverter<ulong> _rawNumberToAddressConverter;
        private readonly TotalSequenceSymbolFrequencyCounter<ulong> _totalSequenceSymbolFrequencyCounter;
        private readonly LinkFrequenciesCache<ulong> _linkFrequenciesCache;
        private readonly CachedFrequencyIncrementingSequenceIndex<ulong> _index;
        private readonly FrequenciesCacheBasedLinkToItsFrequencyNumberConverter<ulong> _linkToItsFrequencyNumberConverter;
        private readonly SequenceToItsLocalElementLevelsConverter<ulong> _sequenceToItsLocalElementLevelsConverter;
        private readonly OptimalVariantConverter<ulong> _optimalVariantConverter;

        public Patterns(string sourceImagePath)
        {
            _sourceImagePath = Path.GetFullPath(sourceImagePath);
            _image = new MagickImage(sourceImagePath);
            _pixels = _image.GetPixels();
            _linksPath = Path.Combine(Path.GetDirectoryName(_sourceImagePath), Path.GetFileNameWithoutExtension(_sourceImagePath) + ".links");
            var memory = new FileMappedResizableDirectMemory(_linksPath);
            var constants = new LinksConstants<ulong>(enableExternalReferencesSupport: true);
            _links = new UInt64Links(new UInt64UnitedMemoryLinks(memory, UInt64UnitedMemoryLinks.DefaultLinksSizeStep, constants, Platform.Data.Doublets.Memory.IndexTreeType.SizedAndThreadedAVLBalancedTree));
            _addressToRawNumberConverter = new AddressToRawNumberConverter<ulong>();
            _rawNumberToAddressConverter = new RawNumberToAddressConverter<ulong>();
            _totalSequenceSymbolFrequencyCounter = new TotalSequenceSymbolFrequencyCounter<ulong>(_links);
            _linkFrequenciesCache = new LinkFrequenciesCache<ulong>(_links, _totalSequenceSymbolFrequencyCounter);
            _index = new CachedFrequencyIncrementingSequenceIndex<ulong>(_linkFrequenciesCache);
            _linkToItsFrequencyNumberConverter = new FrequenciesCacheBasedLinkToItsFrequencyNumberConverter<ulong>(_linkFrequenciesCache);
            _sequenceToItsLocalElementLevelsConverter = new SequenceToItsLocalElementLevelsConverter<ulong>(_links, _linkToItsFrequencyNumberConverter);
            _optimalVariantConverter = new OptimalVariantConverter<ulong>(_links, _sequenceToItsLocalElementLevelsConverter);
        }

        public void Recognize()
        {
            var width = _image.Width;
            var height = _image.Height;

            KeyValuePair<ulong[], ulong[]>[,] matrix = new KeyValuePair<ulong[], ulong[]>[width, height];

            for (var y = 0; y < height; y++)
            {
                IndexRow(y, width);
            }
            for (int x = 0; x < width; x++)
            {
                IndexColumn(x, height);
            }
            for (var y = 0; y < height; y++)
            {
                if (y == 574)
                {
                    var roww = GetRow(y, width);
                    var levelss = _sequenceToItsLocalElementLevelsConverter.Convert(roww);
                    var l276 = levelss[276];
                    var l277 = levelss[277];
                    var l278 = levelss[278];
                }

                var row = SaveRow(y, width);

                var x = 0;
                var stack = new Stack<ulong>();
                StopableSequenceWalker.WalkRight(row, _links.GetSource, _links.GetTarget, _links.IsPartialPoint,
                    enteredElement =>
                    {
                        stack.Push(enteredElement);
                    },
                    exitedElement =>
                    {
                        stack.Pop();
                    },
                    checkedElement => true,
                    element =>
                    {
                        stack.Push(element);
                        var pair = new KeyValuePair<ulong[], ulong[]>(stack.ToArray(), default);
                        stack.Pop();
                        matrix[x++, y] = pair;
                        return true;
                    });
            }
            for (int x = 0; x < width; x++)
            {
                var column = SaveColumn(x, height);

                var y = 0;
                var stack = new Stack<ulong>();
                StopableSequenceWalker.WalkRight(column, _links.GetSource, _links.GetTarget, _links.IsPartialPoint,
                    enteredElement =>
                    {
                        stack.Push(enteredElement);
                    },
                    exitedElement =>
                    {
                        stack.Pop();
                    },
                    checkedElement => true,
                    element =>
                    {
                        var pair = matrix[x, y];
                        stack.Push(element);
                        pair = new KeyValuePair<ulong[], ulong[]>(pair.Key, stack.ToArray());
                        stack.Pop();
                        matrix[x, y++] = pair;
                        return true;
                    });
            }

            // Sort sequences by usages and frequency

            var linksByUsages = new SortedDictionary<ulong, List<ulong>>();
            var linksByFrequency = new SortedDictionary<ulong, List<ulong>>();

            var any = _links.Constants.Any;
            var @continue = _links.Constants.Continue;
            var query = new Link<ulong>(any, any, any);
            _links.Each(link =>
            {
                var linkIndex = _links.GetIndex(link);
                var usages = _links.Count(new ulong[] { any, linkIndex });
                if (!linksByUsages.TryGetValue(usages, out List<ulong> linksByUsageList))
                {
                    linksByUsageList = new List<ulong>();
                    linksByUsages.Add(usages, linksByUsageList);
                }
                linksByUsageList.Add(linkIndex);
                ulong frequency = GetFrequency(link);
                if (!linksByFrequency.TryGetValue(frequency, out List<ulong> linksByFrequencyList))
                {
                    linksByFrequencyList = new List<ulong>();
                    linksByFrequency.Add(frequency, linksByFrequencyList);
                }
                linksByFrequencyList.Add(linkIndex);
                return @continue;
            }, query);

            // Build matrix of levels on 2D plane (as in optimal variant algorithm)

            var levels = new ulong[width, height];

            var topBottom = 0UL;
            var leftRight = 0UL;
            var bottomTop = 0UL;
            var rightLeft = 0UL;

            var lastX = width - 1;
            var lastY = height - 1;

            for (int x = 1; x < lastX; x++)
            {
                for (int y = 1; y < lastY; y++)
                {
                    topBottom = GetFrequency(matrix[x - 1, y].Value[0], matrix[x, y].Value[0]);
                    leftRight = GetFrequency(matrix[x, y - 1].Key[0], matrix[x, y].Key[0]);
                    bottomTop = GetFrequency(matrix[x, y].Value[0], matrix[x + 1, y].Value[0]);
                    rightLeft = GetFrequency(matrix[x, y].Key[0], matrix[x, y + 1].Key[0]);

                    levels[x, y] = Math.Max(Math.Max(topBottom, leftRight), Math.Max(bottomTop, rightLeft));
                }
            }


            for (int x = 1; x < lastX; x++)
            {
                for (int y = 1; y < lastY; y++)
                {
                    Console.Write("{0:0000}", levels[x, y]);
                    Console.Write(' ');
                }
                Console.WriteLine();
            }

            // Get the largest repeated patterns with lowest frequency (level)


        }

        private ulong GetFrequency(ulong link) => GetFrequency(_links.GetLink(link));

        private ulong GetFrequency(ulong source, ulong target)
        {
            return GetFrequency(_links.SearchOrDefault(source, target), source, target);
        }

        private ulong GetFrequency(ulong linkIndex, ulong source, ulong target)
        {
            var frequency = (_linkFrequenciesCache.GetFrequency(source, target) ?? new LinkFrequency<ulong>(0, 0)).Frequency;
            if (frequency == default && linkIndex > 0)
            {
                frequency = _totalSequenceSymbolFrequencyCounter.Count(linkIndex);
            }
            return frequency;
        }

        private ulong GetFrequency(IList<ulong> link) => GetFrequency(link[0], link[1], link[2]);

        private void IndexRow(int y, int width)
        {
            var row = GetRow(y, width);
            _index.Add(row);
        }

        private void IndexColumn(int x, int height)
        {
            var column = GetColumn(x, height);
            _index.Add(column);
        }

        private ulong SaveRow(int y, int width)
        {
            var row = GetRow(y, width);
            return _optimalVariantConverter.Convert(row);
        }

        private ulong SaveColumn(int x, int height)
        {
            var column = GetColumn(x, height);
            return _optimalVariantConverter.Convert(column);
        }

        private ulong[] GetRow(int y, int width)
        {
            var row = new ulong[width];
            for (int x = 0; x < width; x++)
            {
                row[x] = ConvertPixelToRawNumber(_pixels.GetPixel(x, y));
            }
            return row;
        }

        private ulong[] GetColumn(int x, int height)
        {
            var column = new ulong[height];
            for (int y = 0; y < height; y++)
            {
                column[y] = ConvertPixelToRawNumber(_pixels.GetPixel(x, y));
            }
            return column;
        }

        private ulong ConvertPixelToRawNumber(Pixel sourcePixel)
        {
            var color = sourcePixel.ToColor();
            byte[] bytes = new byte[] { color.R, color.G, color.B, color.A };
            return _addressToRawNumberConverter.Convert(bytes.ToStructure<uint>());
        }

        private byte[] ConvertRawNumberToPixelValues(ulong sourcePixel)
        {
            uint pixel = (uint)_rawNumberToAddressConverter.Convert(sourcePixel);
            return pixel.ToBytes();
        }

        protected override void Dispose(bool manual, bool wasDisposed)
        {
            if (!wasDisposed)
            {
                _links.TryDispose();
                _image.TryDispose();
            }
        }
    }
}
