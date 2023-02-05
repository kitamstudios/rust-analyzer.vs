using System;
using System.Diagnostics;
using EnsureThat;

namespace KS.RustAnalyzer.TestAdapter.Common;

[DebuggerDisplay("{_path}")]
public sealed class PathEx : IEquatable<PathEx>
{
    private const StringComparison Comparison = StringComparison.OrdinalIgnoreCase;

    private readonly string _path;

    public PathEx(string path)
    {
        EnsureArg.IsNotNull(path, nameof(path));

        _path = path.Replace("/", @"\");
    }

    public static implicit operator string(PathEx p) => p._path;

    // NOTE: No implicit cast as it can throw.
    public static explicit operator PathEx(string p) => new (p);

    public static bool operator ==(PathEx lhs, PathEx rhs)
    {
        if (lhs is null)
        {
            if (rhs is null)
            {
                return true;
            }

            return false;
        }

        return lhs.Equals(rhs);
    }

    public static bool operator !=(PathEx lhs, PathEx rhs) => !(lhs == rhs);

    public override bool Equals(object obj) => Equals(obj as PathEx);

    public override string ToString() => _path.ToUpperInvariant();

    public override int GetHashCode() => _path.GetHashCode();

    public bool Equals(PathEx p)
    {
        if (p is null)
        {
            return false;
        }

        if (ReferenceEquals(this, p))
        {
            return true;
        }

        if (GetType() != p.GetType())
        {
            return false;
        }

        return string.Equals(_path, p._path, Comparison);
    }
}
