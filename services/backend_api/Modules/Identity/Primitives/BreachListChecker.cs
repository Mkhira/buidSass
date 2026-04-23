using System.Collections;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace BackendApi.Modules.Identity.Primitives;

public sealed class BreachListChecker
{
    private const string ResourceName = "BackendApi.Modules.Identity.Primitives.Resources.hibp-top-100k.txt.gz";
    private readonly HashSet<string> _knownHashes;
    private readonly BloomFilter _bloomFilter;

    public BreachListChecker()
    {
        _knownHashes = LoadKnownHashes();
        _bloomFilter = new BloomFilter(Math.Max(_knownHashes.Count * 20, 2_000_000));

        foreach (var hash in _knownHashes)
        {
            _bloomFilter.Add(hash);
        }
    }

    public bool IsCompromised(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var sha1 = ComputeSha1Hex(password);
        if (!_bloomFilter.MightContain(sha1))
        {
            return false;
        }

        return _knownHashes.Contains(sha1);
    }

    private static HashSet<string> LoadKnownHashes()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Missing embedded resource '{ResourceName}'.");
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);

        var hashes = new HashSet<string>(StringComparer.Ordinal);
        while (!reader.EndOfStream)
        {
            var line = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var candidate = line.Split(':', 2)[0].Trim().ToUpperInvariant();
            if (candidate.Length == 40)
            {
                hashes.Add(candidate);
            }
        }

        return hashes;
    }

    private static string ComputeSha1Hex(string value)
    {
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes);
    }

    private sealed class BloomFilter
    {
        private readonly BitArray _bits;
        private readonly int _size;

        public BloomFilter(int size)
        {
            _size = size;
            _bits = new BitArray(size);
        }

        public void Add(string value)
        {
            foreach (var index in GetIndexes(value))
            {
                _bits[index] = true;
            }
        }

        public bool MightContain(string value)
        {
            foreach (var index in GetIndexes(value))
            {
                if (!_bits[index])
                {
                    return false;
                }
            }

            return true;
        }

        private IEnumerable<int> GetIndexes(string value)
        {
            var utf8 = Encoding.UTF8.GetBytes(value);
            var hashA = SHA256.HashData(utf8);
            var hashB = MD5.HashData(utf8);

            yield return (int)(BitConverter.ToUInt32(hashA, 0) % (uint)_size);
            yield return (int)(BitConverter.ToUInt32(hashA, 8) % (uint)_size);
            yield return (int)(BitConverter.ToUInt32(hashB, 0) % (uint)_size);
            yield return (int)(BitConverter.ToUInt32(hashB, 8) % (uint)_size);
        }
    }
}
