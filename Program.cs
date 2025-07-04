using System.Security.Cryptography;

public class HnswIndex
{
    private readonly List<float[]> _vectors = new();
    private readonly object _lock = new();

    public void AddVector(float[] vector)
    {
        lock (_lock)
        {
            _vectors.Add(vector);
        }
    }

    public List<int> SearchNearest(float[] query, int topK)
    {
        lock (_lock)
        {
            return _vectors
                .Select((v, i) => new { Index = i, Distance = CalculateDistance(v, query) })
                .OrderBy(x => x.Distance)
                .Take(topK)
                .Select(x => x.Index)
                .ToList();
        }
    }

    private float CalculateDistance(float[] a, float[] b)
    {
        float sum = 0;
        for (int i = 0; i < a.Length; i++)
            sum += (a[i] - b[i]) * (a[i] - b[i]);
        return (float)Math.Sqrt(sum);
    }
}

public class VectorRecord
{
    public Guid Id { get; }
    public float[] OriginalVector { get; }
    public short[] QuantizedVector { get; }
    private readonly bool _isCompressed;
    public string Text { get; }

    public VectorRecord(Guid id, float[] vector, bool compress = false, string text = null)
    {
        Id = id;
        _isCompressed = compress;
        OriginalVector = vector;
        Text = text;
        QuantizedVector = compress
            ? Quantize(vector)
            : vector.Select(f => (short)Math.Round(f)).ToArray();
    }

    public VectorRecord(Guid id, short[] quantizedVector, bool isCompressed, string text)
    {
        Id = id;
        _isCompressed = isCompressed;
        QuantizedVector = quantizedVector;
        Text = text;
        OriginalVector = _isCompressed 
            ? Dequantize(quantizedVector) 
            : quantizedVector.Select(s => (float)s).ToArray();
    }

    private short[] Quantize(float[] vector)
    {
        return vector.Select(f => (short)(f * 100)).ToArray();
    }

    private float[] Dequantize(short[] quantizedVector)
    {
        return quantizedVector.Select(s => s / 100.0f).ToArray();
    }

    public float[] Dequantize()
    {
        return _isCompressed
            ? Dequantize(QuantizedVector)
            : QuantizedVector.Select(s => (float)s).ToArray();
    }
}

public class VectorDatabase : IDisposable
{
    private readonly string _filePath;
    private readonly string _walPath;
    private readonly bool _compress;
    private readonly ReaderWriterLockSlim _lock = new();
    private readonly HnswIndex _index = new();
    private readonly List<VectorRecord> _vectors = new();
    private bool _disposed;

    public VectorDatabase(string filePath, bool compress = false)
    {
        _filePath = filePath;
        _walPath = filePath + ".wal";
        _compress = compress;
        LoadFromFile();
    }

    public Guid AddVector(float[] vector, string text = null)
    {
        var record = new VectorRecord(Guid.NewGuid(), vector, _compress, text);
        bool lockTaken = false;

        try
        {
            _lock.EnterWriteLock();
            lockTaken = true;
            
            _vectors.Add(record);
            _index.AddVector(record.Dequantize());
            WriteToWAL(record);
        }
        finally
        {
            if (lockTaken) _lock.ExitWriteLock();
        }

        return record.Id;
    }

    private void WriteToWAL(VectorRecord record)
    {
        using (var wal = new BinaryWriter(File.Open(_walPath, FileMode.Append, FileAccess.Write)))
        {
            wal.Write(record.Id.ToByteArray());
            
            wal.Write(record.Text != null);
            if (record.Text != null)
            {
                wal.Write(record.Text);
            }
            
            wal.Write(record.QuantizedVector.Length);
            foreach (var value in record.QuantizedVector)
                wal.Write(value);
        }
    }

    public void SaveToFile()
    {
        bool lockTaken = false;
        try
        {
            _lock.EnterWriteLock();
            lockTaken = true;
            
            using (var writer = new BinaryWriter(File.Create(_filePath)))
            {
                writer.Write(CalculateCrc32Header());
                writer.Write(_vectors.Count);

                foreach (var record in _vectors)
                {
                    writer.Write(record.Id.ToByteArray());
                    
                    writer.Write(record.Text != null);
                    if (record.Text != null)
                    {
                        writer.Write(record.Text);
                    }
                    
                    writer.Write(record.QuantizedVector.Length);
                    foreach (var value in record.QuantizedVector)
                        writer.Write(value);
                    writer.Write(CalculateCrc32Vector(record));
                }
            }
            File.Delete(_walPath);
        }
        finally
        {
            if (lockTaken) _lock.ExitWriteLock();
        }
    }

    private uint CalculateCrc32Header()
    {
        byte[] countBytes = BitConverter.GetBytes(_vectors.Count);
        return Crc32.Compute(countBytes);
    }

    private uint CalculateCrc32Vector(VectorRecord record)
    {
        byte[] idBytes = record.Id.ToByteArray();
        byte[] textFlagBytes = BitConverter.GetBytes(record.Text != null);
        byte[] textBytes = record.Text != null ? System.Text.Encoding.UTF8.GetBytes(record.Text) : Array.Empty<byte>();
        byte[] lengthBytes = BitConverter.GetBytes(record.QuantizedVector.Length);
        byte[] vectorBytes = new byte[record.QuantizedVector.Length * 2];
        Buffer.BlockCopy(record.QuantizedVector, 0, vectorBytes, 0, vectorBytes.Length);

        byte[] allData = new byte[
            idBytes.Length + 
            textFlagBytes.Length + 
            textBytes.Length + 
            lengthBytes.Length + 
            vectorBytes.Length
        ];

        int offset = 0;
        Buffer.BlockCopy(idBytes, 0, allData, offset, idBytes.Length);
        offset += idBytes.Length;
        Buffer.BlockCopy(textFlagBytes, 0, allData, offset, textFlagBytes.Length);
        offset += textFlagBytes.Length;
        Buffer.BlockCopy(textBytes, 0, allData, offset, textBytes.Length);
        offset += textBytes.Length;
        Buffer.BlockCopy(lengthBytes, 0, allData, offset, lengthBytes.Length);
        offset += lengthBytes.Length;
        Buffer.BlockCopy(vectorBytes, 0, allData, offset, vectorBytes.Length);

        return Crc32.Compute(allData);
    }

    private void LoadFromFile()
    {
        try
        {
            if (File.Exists(_walPath))
                RecoverFromWAL();

            if (!File.Exists(_filePath)) return;

            using (var reader = new BinaryReader(File.OpenRead(_filePath)))
            {
                uint storedHeaderCrc = reader.ReadUInt32();
                int count = reader.ReadInt32();

                uint calculatedHeaderCrc = CalculateCrc32ForInt(count);
                if (storedHeaderCrc != calculatedHeaderCrc)
                    throw new InvalidDataException("Header CRC32 mismatch");

                for (int i = 0; i < count; i++)
                {
                    var idBytes = reader.ReadBytes(16);
                    var id = new Guid(idBytes);
                    
                    bool hasText = reader.ReadBoolean();
                    string text = hasText ? reader.ReadString() : null;
                    
                    int length = reader.ReadInt32();
                    var vector = new short[length];
                    for (int j = 0; j < length; j++)
                        vector[j] = reader.ReadInt16();

                    uint storedVectorCrc = reader.ReadUInt32();
                    uint calculatedVectorCrc = CalculateCrc32ForVector(id, vector, text);

                    if (storedVectorCrc != calculatedVectorCrc)
                        throw new InvalidDataException($"Vector CRC32 mismatch at position {i}");

                    var record = new VectorRecord(id, vector, _compress, text);
                    _vectors.Add(record);
                    _index.AddVector(record.Dequantize());
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading database: {ex.Message}");
        }
    }

    private uint CalculateCrc32ForInt(int value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        return Crc32.Compute(bytes);
    }

    private uint CalculateCrc32ForVector(Guid id, short[] vector, string text)
    {
        byte[] idBytes = id.ToByteArray();
        byte[] textFlagBytes = BitConverter.GetBytes(text != null);
        byte[] textBytes = text != null ? System.Text.Encoding.UTF8.GetBytes(text) : Array.Empty<byte>();
        byte[] lengthBytes = BitConverter.GetBytes(vector.Length);
        byte[] vectorBytes = new byte[vector.Length * 2];
        Buffer.BlockCopy(vector, 0, vectorBytes, 0, vectorBytes.Length);

        byte[] allData = new byte[
            idBytes.Length + 
            textFlagBytes.Length + 
            textBytes.Length + 
            lengthBytes.Length + 
            vectorBytes.Length
        ];

        int offset = 0;
        Buffer.BlockCopy(idBytes, 0, allData, offset, idBytes.Length);
        offset += idBytes.Length;
        Buffer.BlockCopy(textFlagBytes, 0, allData, offset, textFlagBytes.Length);
        offset += textFlagBytes.Length;
        Buffer.BlockCopy(textBytes, 0, allData, offset, textBytes.Length);
        offset += textBytes.Length;
        Buffer.BlockCopy(lengthBytes, 0, allData, offset, lengthBytes.Length);
        offset += lengthBytes.Length;
        Buffer.BlockCopy(vectorBytes, 0, allData, offset, vectorBytes.Length);

        return Crc32.Compute(allData);
    }

    private void RecoverFromWAL()
    {
        try
        {
            var tempVectors = new List<VectorRecord>();
            using (var reader = new BinaryReader(File.OpenRead(_walPath)))
            {
                while (reader.BaseStream.Position < reader.BaseStream.Length)
                {
                    var idBytes = reader.ReadBytes(16);
                    var id = new Guid(idBytes);
                    
                    bool hasText = reader.ReadBoolean();
                    string text = hasText ? reader.ReadString() : null;
                    
                    int length = reader.ReadInt32();
                    var vector = new short[length];
                    for (int j = 0; j < length; j++)
                        vector[j] = reader.ReadInt16();

                    tempVectors.Add(new VectorRecord(id, vector, _compress, text));
                }
            }

            _vectors.AddRange(tempVectors);
            Console.WriteLine($"Recovered {tempVectors.Count} records from WAL");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WAL recovery failed: {ex.Message}");
            File.Delete(_walPath);
        }
    }

    public Task<List<Guid>> SearchNearestAsync(float[] queryVector, int topK = 5)
    {
        return Task.Run(() =>
        {
            bool lockTaken = false;
            try
            {
                _lock.EnterReadLock();
                lockTaken = true;
                
                var indices = _index.SearchNearest(queryVector, topK);
                return indices.Select(i => _vectors[i].Id).ToList();
            }
            finally
            {
                if (lockTaken) _lock.ExitReadLock();
            }
        });
    }

    public Task<List<(Guid Id, string Text)>> SearchNearestWithTextAsync(float[] queryVector, int topK = 5)
    {
        return Task.Run(() =>
        {
            bool lockTaken = false;
            try
            {
                _lock.EnterReadLock();
                lockTaken = true;
                
                var indices = _index.SearchNearest(queryVector, topK);
                return indices.Select(i => (_vectors[i].Id, _vectors[i].Text)).ToList();
            }
            finally
            {
                if (lockTaken) _lock.ExitReadLock();
            }
        });
    }

    public int VectorCount
    {
        get 
        {
            bool lockTaken = false;
            try
            {
                _lock.EnterReadLock();
                lockTaken = true;
                return _vectors.Count;
            }
            finally
            {
                if (lockTaken) _lock.ExitReadLock();
            }
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _lock.Dispose();
            }
            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}

public class Crc32 : HashAlgorithm
{
    private const uint InitialValue = 0xFFFFFFFF;
    private const uint Polynomial = 0xEDB88320;
    private static readonly uint[] Table = InitializeTable();
    private uint _hash = InitialValue;

    public override int HashSize => 32;

    public override void Initialize()
    {
        _hash = InitialValue;
    }

    protected override void HashCore(byte[] array, int ibStart, int cbSize)
    {
        for (int i = ibStart; i < ibStart + cbSize; i++)
            _hash = (_hash >> 8) ^ Table[(_hash ^ array[i]) & 0xFF];
    }

    protected override byte[] HashFinal()
    {
        uint finalHash = _hash ^ InitialValue;
        return BitConverter.GetBytes(finalHash);
    }

    public static uint Compute(byte[] data)
    {
        using var crc = new Crc32();
        crc.HashCore(data, 0, data.Length);
        return BitConverter.ToUInt32(crc.HashFinal(), 0);
    }

    private static uint[] InitializeTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            uint value = i;
            for (int j = 0; j < 8; j++)
                value = (value & 1) == 1 ? (value >> 1) ^ Polynomial : (value >> 1);
            table[i] = value;
        }
        return table;
    }
}

class Program
{
    static async Task Main(string[] args)
    {
        const string dbPath = "text_vectors.db";
        
        // Создаем и заполняем базу данных
        using (var db = new VectorDatabase(dbPath, compress: true))
        {
            Console.WriteLine("Adding documents to database...");
            
            db.AddVector(GetFakeEmbedding("The quick brown fox jumps over the lazy dog"), 
                         "The quick brown fox jumps over the lazy dog");
            
            db.AddVector(GetFakeEmbedding("Programming in C# is fun and efficient"), 
                         "Programming in C# is fun and efficient");
            
            db.AddVector(GetFakeEmbedding("Machine learning transforms modern applications"), 
                         "Machine learning transforms modern applications");
            
            db.AddVector(GetFakeEmbedding("Paris is the capital city of France"), 
                         "Paris is the capital city of France");
            
            db.AddVector(GetFakeEmbedding("Canine companions are man's best friends"), 
                         "Canine companions are man's best friends");
            
            db.SaveToFile();
            Console.WriteLine($"Database saved with {db.VectorCount} records");
        }
        
        // Загружаем базу данных
        using (var db = new VectorDatabase(dbPath, compress: true))
        {
            Console.WriteLine($"Database loaded with {db.VectorCount} records");
            
            // Выполняем семантический поиск
            var query1 = "Dogs resting lazily";
            Console.WriteLine($"\nSearch: '{query1}'");
            await SearchAndPrintResults(db, query1);
            
            var query2 = "Software development";
            Console.WriteLine($"\nSearch: '{query2}'");
            await SearchAndPrintResults(db, query2);
            
            var query3 = "European capitals";
            Console.WriteLine($"\nSearch: '{query3}'");
            await SearchAndPrintResults(db, query3);
        }
    }

    static float[] GetFakeEmbedding(string text)
    {
        var random = new Random(text.GetHashCode());
        return Enumerable.Range(0, 128)
            .Select(_ => (float)random.NextDouble())
            .ToArray();
    }

    static async Task SearchAndPrintResults(VectorDatabase db, string query)
    {
        float[] queryVector = GetFakeEmbedding(query);
        var results = await db.SearchNearestWithTextAsync(queryVector, topK: 2);
        
        foreach (var (id, text) in results)
        {
            Console.WriteLine($"- {text}");
        }
    }
}