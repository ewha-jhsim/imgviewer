using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ImgViewer.Services;

/// <summary>
/// Tracks the list of supported images in a folder and provides previous/next
/// navigation. Files are ordered with a natural (numeric-aware) comparison so that
/// "img2.png" comes before "img10.png".
/// </summary>
public sealed class FolderNavigator
{
    private List<string> _files = new();
    private int _index = -1;

    public string? CurrentPath => _index >= 0 && _index < _files.Count ? _files[_index] : null;
    public int Count => _files.Count;
    public int Position => _index; // zero-based index of the current file, or -1.

    /// <summary>Scans the folder of <paramref name="currentFile"/> and selects it.</summary>
    public void Load(string currentFile)
    {
        string? dir = Path.GetDirectoryName(Path.GetFullPath(currentFile));
        if (dir is null)
        {
            _files = new List<string> { currentFile };
            _index = 0;
            return;
        }

        IEnumerable<string> found;
        try
        {
            found = Directory.EnumerateFiles(dir).Where(ImageLoader.IsSupported);
        }
        catch (Exception)
        {
            found = new[] { currentFile };
        }

        _files = found.OrderBy(Path.GetFileName, NaturalComparer.Instance).ToList();

        string full = Path.GetFullPath(currentFile);
        _index = _files.FindIndex(f => string.Equals(
            Path.GetFullPath(f), full, StringComparison.OrdinalIgnoreCase));

        // If the file wasn't found (e.g. just created), insert it so navigation works.
        if (_index < 0)
        {
            _files.Insert(0, currentFile);
            _index = 0;
        }
    }

    /// <summary>Re-scans the current folder, keeping the current file selected if possible.</summary>
    public void Refresh()
    {
        string? current = CurrentPath;
        if (current is not null)
            Load(current);
    }

    public string? MoveNext() => Move(+1);
    public string? MovePrevious() => Move(-1);

    private string? Move(int delta)
    {
        if (_files.Count == 0)
            return null;

        // Wrap around so navigation is continuous at both ends of the folder.
        _index = ((_index + delta) % _files.Count + _files.Count) % _files.Count;
        return CurrentPath;
    }

    /// <summary>Removes the current entry (e.g. after a delete) and points at a neighbour.</summary>
    public string? RemoveCurrent()
    {
        if (_index < 0 || _index >= _files.Count)
            return null;

        _files.RemoveAt(_index);
        if (_files.Count == 0)
        {
            _index = -1;
            return null;
        }

        if (_index >= _files.Count)
            _index = _files.Count - 1;

        return CurrentPath;
    }
}

/// <summary>Compares file names treating embedded digit runs as numbers.</summary>
internal sealed class NaturalComparer : IComparer<string?>
{
    public static readonly NaturalComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (x is null) return y is null ? 0 : -1;
        if (y is null) return 1;

        int ix = 0, iy = 0;
        while (ix < x.Length && iy < y.Length)
        {
            char cx = x[ix], cy = y[iy];
            if (char.IsDigit(cx) && char.IsDigit(cy))
            {
                int sx = ix, sy = iy;
                while (ix < x.Length && char.IsDigit(x[ix])) ix++;
                while (iy < y.Length && char.IsDigit(y[iy])) iy++;

                ReadOnlySpan<char> nx = x.AsSpan(sx, ix - sx).TrimStart('0');
                ReadOnlySpan<char> ny = y.AsSpan(sy, iy - sy).TrimStart('0');

                if (nx.Length != ny.Length)
                    return nx.Length - ny.Length;
                int cmp = nx.CompareTo(ny, StringComparison.Ordinal);
                if (cmp != 0)
                    return cmp;
            }
            else
            {
                int cmp = char.ToLowerInvariant(cx).CompareTo(char.ToLowerInvariant(cy));
                if (cmp != 0)
                    return cmp;
                ix++;
                iy++;
            }
        }

        return (x.Length - ix) - (y.Length - iy);
    }
}
