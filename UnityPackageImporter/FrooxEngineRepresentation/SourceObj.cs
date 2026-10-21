#nullable disable
using System;
using System.Collections.Generic;
using System.Text;

namespace UnityPackageImporter.FrooxEngineRepresentation;

public class SourceObj
{
    public long fileID { get; set; }
    public string guid { get; set; }
    public int type { get; set; }
    public SourceObj() { }

    public SourceObj(long fileID, string guid, int type)
    {
        this.fileID = fileID;
        this.guid = guid;
        this.type = type;
    }

    public override string ToString()
    {
        StringBuilder result = new StringBuilder();
        try
        {
            result.AppendLine("fileID: " + fileID.ToString());
        }
        catch (NullReferenceException)
        {
            // Guard against uninitialized state in debug string formatting.
            result.AppendLine("fileID: null");
        }

        try
        {
            result.AppendLine("guid: " + guid.ToString());
        }
        catch (NullReferenceException)
        {
            // guid may be null when constructing debug output for incomplete source objects.
            result.AppendLine("guid: null");
        }

        try
        {
            result.AppendLine("type: " + type.ToString());
        }
        catch (NullReferenceException)
        {
            // Guard against uninitialized state in debug string formatting.
            result.AppendLine("type: null");
        }

        return result.ToString();
    }
}

public class SourceObjCompare : EqualityComparer<SourceObj>
{
    public override bool Equals(SourceObj x, SourceObj y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null) return false;
        return x.fileID == y.fileID && string.Equals(x.guid, y.guid, StringComparison.OrdinalIgnoreCase);
    }

    public override int GetHashCode(SourceObj obj)
    {
        if (obj is null) return 0;
        return (obj.fileID.ToString() + (obj.guid?.ToLowerInvariant() ?? "")).GetHashCode();
    }
}
