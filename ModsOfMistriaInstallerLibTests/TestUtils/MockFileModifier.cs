using Garethp.ModsOfMistriaInstallerLib.Utils;

namespace ModsOfMistriaInstallerLibTests.TestUtils;

public class MockFileModifier: IFileModifier
{
    private readonly Dictionary<string, string> _originalFiles;
    private readonly Dictionary<string, string> _resultingFiles;
    private readonly Dictionary<string, byte[]> _binaryFiles = new();

    public MockFileModifier(Dictionary<string, string> files)
    {
        _originalFiles = files.ToDictionary(x => x.Key.Replace("\\", "/"), x => x.Value);
        _resultingFiles = files.ToDictionary(x => x.Key.Replace("\\", "/"), x => x.Value);
    }

    public bool Exists(string file)
    {
        file = file.Replace("\\", "/");
        
        return _resultingFiles.ContainsKey(file)
               || _binaryFiles.ContainsKey(file)
               || _resultingFiles.Keys.Any(x => x.StartsWith(file + "/"))
               || _binaryFiles.Keys.Any(x => x.StartsWith(file + "/"));
    }

    public string[] FindFiles(string path, string pattern)
    {
        path = path.Replace("\\", "/");
        
        return _resultingFiles
            .Keys
            .Where(x => x.StartsWith(path) && x.Contains(pattern) && !x.EndsWith('/'))
            .ToArray()
        ;
    }

    public string Read(string file)
    {
        file = file.Replace("\\", "/");
        
        return _resultingFiles[file];
    }

    public Stream GetReadStream(string file)
    {
        file = file.Replace("\\", "/");
        
        var stream = new MemoryStream();
        var writer = new StreamWriter(stream);
        writer.Write(_resultingFiles[file]);
        writer.Flush();
        stream.Position = 0;

        return stream;
    }

    public void Write(string file, string contents)
    {
        file = file.Replace("\\", "/");
        
        _resultingFiles[file] = contents;
    }

    public void Write(string file, byte[] contents)
    {
        file = file.Replace("\\", "/");
        _binaryFiles[file] = contents.ToArray();
        _resultingFiles[file] = System.Text.Encoding.UTF8.GetString(contents);
    }

    public bool HasBinaryFile(string file) =>
        _binaryFiles.ContainsKey(file.Replace("\\", "/"));

    public byte[] GetBinaryFile(string file) =>
        _binaryFiles[file.Replace("\\", "/")];

    public Stream GetWriteStream(string file)
    {
        throw new NotImplementedException();
    }

    public bool ConditionalRestoreBackup(string file, Func<bool> condition)
    {
        file = file.Replace("\\", "/");
        
        if (condition())
        {
            _resultingFiles[file] = _originalFiles[file];
            return true;
        }

        return false;
    }

    public string GetFile(string file)
    {
        file = file.Replace("\\", "/");
        
        return _resultingFiles[file];
    }

}
