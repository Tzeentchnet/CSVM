using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace CSVM.Tooling;

/// <summary>The editable values of one <c>aiv.zrd</c> roster start.</summary>
public readonly record struct CampaignRosterStartState(
    float X,
    float Y,
    float Z,
    float YawDeg,
    int Team,
    int Group);

/// <summary>The exact-index typed views over one mission's <c>aiv.zrd</c> roster starts.</summary>
public sealed class CampaignRosterStarts
{
    internal const int MaxExactInteger = 16_777_216;

    private const string ReaderName = "aiv.json";

    private readonly List<CampaignRosterStart> _entries;

    private CampaignRosterStarts(
        List<CampaignRosterStart> entries)
    {
        _entries = entries;
    }

    /// <summary>Roster entries in raw file order.</summary>
    public IReadOnlyList<CampaignRosterStart> Entries =>
        _entries;

    /// <summary>Binds every roster entry when the reader has its positional header and every
    /// block carries the complete six-slot spawn prefix. Variable-width trailing fields are not
    /// interpreted.</summary>
    public static bool TryBind(
        CampaignMissionDocument document,
        [NotNullWhen(true)] out CampaignRosterStarts? binding,
        out string problem)
    {
        binding = null;
        int fileIndex =
            document.FindFileIndex(ReaderName);
        if (fileIndex < 0)
        {
            problem = $"'{ReaderName}' is absent";
            return false;
        }

        List<object?> root = document.Files[fileIndex].Root;
        if (root.Count == 0
            || root[0] is not List<object?>)
        {
            problem = $"'{ReaderName}' has no positional header";
            return false;
        }

        var entries = new List<CampaignRosterStart>(
            Math.Max(0, root.Count - 1));
        for (int rootIndex = 1; rootIndex < root.Count; rootIndex++)
        {
            if (root[rootIndex] is not List<object?> { Count: >= 2 } entry
                || entry[0] is not string { Length: > 0 } name
                || entry[1] is not List<object?> fields)
            {
                problem =
                    $"'{ReaderName}' entry [{rootIndex}] is not a named roster block";
                return false;
            }

            string? fieldProblem =
                SpawnPrefixProblem(fields);
            if (fieldProblem != null)
            {
                problem =
                    $"'{ReaderName}' entry [{rootIndex}] '{name}' {fieldProblem}";
                return false;
            }

            entries.Add(
                new CampaignRosterStart(
                    document,
                    fileIndex,
                    rootIndex,
                    name));
        }

        binding =
            new CampaignRosterStarts(entries);
        problem = "";
        return true;
    }

    private static string? SpawnPrefixProblem(
        List<object?> fields)
    {
        if (fields.Count < 6)
        {
            return "does not carry the complete six-slot spawn prefix";
        }

        if (fields[1] is not List<object?> position
            || position.Count < 3
            || position[0] is not float x
            || !float.IsFinite(x)
            || position[1] is not float y
            || !float.IsFinite(y)
            || position[2] is not float z
            || !float.IsFinite(z))
        {
            return "has no finite three-component spawn position";
        }

        if (fields[2] is not float yaw
            || !float.IsFinite(yaw))
        {
            return "has no finite spawn yaw";
        }

        if (fields[3] is not float team
            || !Integral(team)
            || team < -1f)
        {
            return "has a non-integral or unsupported team";
        }

        if (fields[4] is not float group
            || !Integral(group)
            || group < 0f)
        {
            return "has a non-integral or negative group";
        }

        if (fields[5] is not float enabled
            || (enabled != 0f && enabled != 1f))
        {
            return "has an enabled flag other than 0 or 1";
        }

        return null;
    }

    private static bool Integral(float value) =>
        float.IsFinite(value)
        && value == MathF.Truncate(value)
        && value >= -MaxExactInteger
        && value <= MaxExactInteger;
}

/// <summary>One roster block's editable spawn prefix. Unknown and trailing fields stay in the
/// mission document untouched.</summary>
public sealed class CampaignRosterStart
{
    private readonly CampaignMissionDocument _document;
    private readonly int _fileIndex;
    private readonly int _rootIndex;

    internal CampaignRosterStart(
        CampaignMissionDocument document,
        int fileIndex,
        int rootIndex,
        string name)
    {
        _document = document;
        _fileIndex = fileIndex;
        _rootIndex = rootIndex;
        Name = name;
    }

    /// <summary>The roster block's node name.</summary>
    public string Name { get; }

    /// <summary>The raw root index of this block, retained for diagnostics and selection.</summary>
    public int RawIndex =>
        _rootIndex;

    /// <summary>Whether this is the human player's roster block. Campaign placement reads
    /// <c>PLAYER_INIT</c> instead.</summary>
    public bool IsPlayer =>
        Name.Equals(
            "player",
            StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether this block is present in the initial roster. False marks an enemy-generator
    /// parameter template.</summary>
    public bool Enabled =>
        Scalar(5) == 1f;

    /// <summary>The current typed spawn prefix, read from the raw document on every access.</summary>
    public CampaignRosterStartState State =>
        new(
            Vector(0),
            Vector(1),
            Vector(2),
            Scalar(2),
            (int)Scalar(3),
            (int)Scalar(4));

    /// <summary>Writes every editable spawn field after validating them together. Returns whether
    /// any raw float changed.</summary>
    public bool Apply(CampaignRosterStartState state)
    {
        Validate(state);
        bool changed = false;
        changed |= SetVector(0, state.X);
        changed |= SetVector(1, state.Y);
        changed |= SetVector(2, state.Z);
        changed |= SetScalar(2, state.YawDeg);
        changed |= SetScalar(3, state.Team);
        changed |= SetScalar(4, state.Group);
        return changed;
    }

    private static void Validate(
        CampaignRosterStartState state)
    {
        if (!float.IsFinite(state.X)
            || !float.IsFinite(state.Y)
            || !float.IsFinite(state.Z)
            || !float.IsFinite(state.YawDeg))
        {
            throw new ArgumentOutOfRangeException(
                nameof(state),
                "roster position and yaw must be finite");
        }

        if (state.Team < -1
            || state.Team > CampaignRosterStarts.MaxExactInteger)
        {
            throw new ArgumentOutOfRangeException(
                nameof(state),
                $"roster team must be between -1 and {CampaignRosterStarts.MaxExactInteger}");
        }

        if (state.Group < 0
            || state.Group > CampaignRosterStarts.MaxExactInteger)
        {
            throw new ArgumentOutOfRangeException(
                nameof(state),
                $"roster group must be between 0 and {CampaignRosterStarts.MaxExactInteger}");
        }
    }

    private float Scalar(int slot) =>
        (float)_document.ValueAt(
            _fileIndex,
            new[] { _rootIndex, 1, slot })!;

    private float Vector(int component) =>
        (float)_document.ValueAt(
            _fileIndex,
            new[] { _rootIndex, 1, 1, component })!;

    private bool SetScalar(
        int slot,
        int value) =>
        SetScalar(
            slot,
            (float)value);

    private bool SetScalar(
        int slot,
        float value) =>
        _document.SetLeaf(
            _fileIndex,
            new[] { _rootIndex, 1, slot },
            value);

    private bool SetVector(
        int component,
        float value) =>
        _document.SetLeaf(
            _fileIndex,
            new[] { _rootIndex, 1, 1, component },
            value);
}
