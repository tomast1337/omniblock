using System.Text;
using Microsoft.Extensions.Logging;

namespace OmniBlock.Client.Diagnostics;

internal sealed class LuauCommandHistory
{
    private readonly List<string> _entries = [];
    private readonly int _capacity;
    private readonly string? _path;
    private readonly ILogger _logger = Log.Instance.For<LuauCommandHistory>();
    private string _draft = string.Empty;
    private int _position;

    public LuauCommandHistory(string? path = null, int capacity = 100)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _capacity = capacity;
        _path = path;
        Load();
        ResetNavigation();
    }

    public void Add(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return;

        if (_entries.Count != 0 && _entries[^1] == source)
        {
            ResetNavigation();
            return;
        }

        _entries.Add(source);
        TrimToCapacity();

        ResetNavigation();
        Save();
    }

    public string Navigate(string current, bool older)
    {
        if (_entries.Count == 0)
            return current;

        if (_position == _entries.Count)
            _draft = current;

        if (older)
        {
            if (_position > 0)
                _position--;
        }
        else if (_position < _entries.Count)
        {
            _position++;
        }

        return _position == _entries.Count ? _draft : _entries[_position];
    }

    private void ResetNavigation()
    {
        _position = _entries.Count;
        _draft = string.Empty;
    }

    private void Load()
    {
        if (_path == null || !File.Exists(_path))
            return;

        try
        {
            foreach (string line in File.ReadLines(_path, Encoding.UTF8))
                _entries.Add(Unescape(line));
            TrimToCapacity();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Could not load Luau command history from {Path}", _path);
        }
    }

    private void Save()
    {
        if (_path == null)
            return;

        string temporaryPath = _path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllLines(temporaryPath, _entries.Select(Escape), Encoding.UTF8);
            File.Move(temporaryPath, _path, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Could not save Luau command history to {Path}", _path);
        }
        finally
        {
            try { File.Delete(temporaryPath); } catch (Exception) { }
        }
    }

    private void TrimToCapacity()
    {
        if (_entries.Count > _capacity)
            _entries.RemoveRange(0, _entries.Count - _capacity);
    }

    private static string Escape(string source) => source
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\r", "\\r", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal);

    private static string Unescape(string source)
    {
        StringBuilder result = new(source.Length);
        for (int index = 0; index < source.Length; index++)
        {
            if (source[index] != '\\' || index + 1 >= source.Length)
            {
                result.Append(source[index]);
                continue;
            }

            result.Append(source[++index] switch
            {
                'n' => '\n',
                'r' => '\r',
                '\\' => '\\',
                char value => value,
            });
        }
        return result.ToString();
    }
}
