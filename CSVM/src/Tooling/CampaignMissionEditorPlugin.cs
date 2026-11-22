using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CSVM.Extraction;
using CSVM.Flight.Modes;
using CSVM.Mech3;
using CSVM.Utils;
using Godot;

namespace CSVM.Tooling;

/// <summary>
/// Registers the Campaign Missions dock in the Godot editor. The dock reads a user-owned source
/// through CSVM's ordinary readers and writes only to an external modding folder.
/// </summary>
[Tool]
public sealed partial class CampaignMissionEditorPlugin : EditorPlugin
{
    private CampaignMissionEditorDock? _dock;

    public override void _EnterTree()
    {
        _dock = new CampaignMissionEditorDock
        {
            Name = "Campaign Missions",
        };
        AddControlToDock(DockSlot.RightBl, _dock);
    }

    public override void _ExitTree()
    {
        if (_dock == null || !IsInstanceValid(_dock))
        {
            return;
        }

        RemoveControlFromDocks(_dock);
        _dock.QueueFree();
        _dock = null;
    }
}

[Tool]
internal sealed partial class CampaignMissionEditorDock : VBoxContainer
{
    private readonly List<CampaignMission> _missions = new();
    private readonly Dictionary<PlayerInitField, LineEdit> _playerInitFields = new();
    private readonly Dictionary<RosterStartField, LineEdit> _rosterStartFields = new();
    private readonly Dictionary<WeatherCloudBandField, LineEdit> _weatherCloudBandFields = new();
    private readonly Dictionary<WeatherWindField, LineEdit> _weatherWindFields = new();
    private readonly Dictionary<WeatherFogField, LineEdit> _weatherFogFields = new();

    private LineEdit _sourcePath = null!;
    private OptionButton _missionPicker = null!;
    private OptionButton _readerPicker = null!;
    private Tree _readerTree = null!;
    private LineEdit _modPath = null!;
    private Button _exportButton = null!;
    private Label _status = null!;
    private VBoxContainer _playerInitPanel = null!;
    private GridContainer _playerInitGrid = null!;
    private Label _playerInitPending = null!;
    private Label _playerInitStatus = null!;
    private Button _applyPlayerInit = null!;
    private TabContainer _typedTabs = null!;
    private VBoxContainer _rosterStartPanel = null!;
    private OptionButton _rosterStartPicker = null!;
    private GridContainer _rosterStartGrid = null!;
    private Label _rosterStartMeta = null!;
    private Label _rosterStartStatus = null!;
    private Button _applyRosterStart = null!;
    private VBoxContainer _weatherCloudBandPanel = null!;
    private GridContainer _weatherCloudBandGrid = null!;
    private Label _weatherCloudBandStatus = null!;
    private Button _applyWeatherCloudBand = null!;
    private VBoxContainer _weatherWindPanel = null!;
    private GridContainer _weatherWindGrid = null!;
    private Label _weatherWindStatus = null!;
    private Button _applyWeatherWind = null!;
    private VBoxContainer _weatherFogPanel = null!;
    private OptionButton _weatherFogPicker = null!;
    private GridContainer _weatherFogGrid = null!;
    private Label _weatherFogStatus = null!;
    private Button _applyWeatherFog = null!;
    private FileDialog _sourceDialog = null!;
    private FileDialog _modDialog = null!;
    private IGameFileSource? _source;
    private CampaignMissionDocument? _document;
    private CampaignPlayerInit? _playerInit;
    private CampaignRosterStarts? _rosterStarts;
    private CampaignWeatherCloudBand? _weatherCloudBand;
    private CampaignWeatherWind? _weatherWind;
    private CampaignWeatherFogZones? _weatherFog;
    private bool _rebuildingTree;

    private enum PlayerInitField
    {
        X,
        Y,
        Z,
        Pitch,
        Yaw,
        Roll,
        Throttle,
        Speed,
    }

    private enum RosterStartField
    {
        X,
        Y,
        Z,
        Yaw,
        Team,
        Group,
    }

    private enum WeatherCloudBandField
    {
        Top,
        Bottom,
        Thickness,
        TopColorR,
        TopColorG,
        TopColorB,
        BottomColorR,
        BottomColorG,
        BottomColorB,
    }

    private enum WeatherWindField
    {
        StaticX,
        StaticY,
        StaticZ,
        RandomMaxSpeed,
        RandomAccel,
        RandomAngVel,
    }

    private enum WeatherFogField
    {
        ColorR,
        ColorG,
        ColorB,
        Near,
        Far,
        Low,
        High,
    }

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(430f, 0f);
        AddThemeConstantOverride("separation", 6);

        AddChild(new Label { Text = "Campaign Mission Editor" });
        AddChild(new Label
        {
            Text = "Raw string and float leaves are writable. Lists, nulls, and wider scopes stay read-only.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });

        _sourcePath = new LineEdit
        {
            PlaceholderText = "Crimson Skies install folder, disc, .iso, .cue, .ccd, .bin, or .img",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _sourcePath.TextSubmitted += _ => LoadSource();
        var sourceRow = new HBoxContainer();
        sourceRow.AddChild(_sourcePath);
        sourceRow.AddChild(Button("Browse", OpenSourceDialog));
        sourceRow.AddChild(Button("Load", LoadSource));
        AddChild(sourceRow);

        _missionPicker = new OptionButton
        {
            Disabled = true,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _missionPicker.ItemSelected += index => LoadMission((int)index);
        AddChild(_missionPicker);

        _readerPicker = new OptionButton
        {
            Disabled = true,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _readerPicker.ItemSelected += index => RebuildReader((int)index);
        AddChild(_readerPicker);

        _typedTabs = new TabContainer
        {
            CustomMinimumSize = new Vector2(0f, 300f),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            Visible = false,
        };
        AddChild(_typedTabs);
        BuildPlayerInitPanel();
        BuildRosterStartPanel();
        BuildWeatherCloudBandPanel();
        BuildWeatherWindPanel();
        BuildWeatherFogPanel();

        _readerTree = new Tree
        {
            Columns = 3,
            ColumnTitlesVisible = true,
            HideRoot = false,
            SelectMode = Tree.SelectModeEnum.Row,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        _readerTree.SetColumnTitle(0, "index");
        _readerTree.SetColumnTitle(1, "type");
        _readerTree.SetColumnTitle(2, "value");
        _readerTree.SetColumnExpand(0, false);
        _readerTree.SetColumnCustomMinimumWidth(0, 56);
        _readerTree.SetColumnExpand(1, false);
        _readerTree.SetColumnCustomMinimumWidth(1, 62);
        _readerTree.ItemEdited += OnLeafEdited;
        AddChild(_readerTree);

        _modPath = new LineEdit
        {
            PlaceholderText = "External modding folder",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _modPath.TextChanged += _ => UpdateExportState();
        var modRow = new HBoxContainer();
        modRow.AddChild(_modPath);
        modRow.AddChild(Button("Browse", OpenModDialog));
        AddChild(modRow);

        _exportButton = Button("Export complete mission scope", Export);
        _exportButton.Disabled = true;
        AddChild(_exportButton);

        _status = new Label
        {
            Text = "Choose a user-owned game source.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        AddChild(_status);

        _sourceDialog = Picker(
            "Choose your Crimson Skies install folder, game disc, or disc image",
            foldersOnly: false);
        _sourceDialog.DirSelected += path => ChooseSource(path);
        _sourceDialog.FileSelected += path => ChooseSource(path);
        AddChild(_sourceDialog);

        _modDialog = Picker(
            "Choose an external modding folder",
            foldersOnly: true);
        _modDialog.DirSelected += path =>
        {
            _modPath.Text = Path.GetFullPath(path);
            UpdateExportState();
        };
        AddChild(_modDialog);
    }

    private void ChooseSource(string path)
    {
        _sourcePath.Text = Path.GetFullPath(path);
        LoadSource();
    }

    private void LoadSource()
    {
        string named = _sourcePath.Text.Trim();
        if (named.Length == 0)
        {
            Failure("Choose a Crimson Skies install, disc, or image first.");
            return;
        }

        try
        {
            string path = Path.GetFullPath(named);
            IGameFileSource? source = GameFileSource.Open(path);
            if (source == null)
            {
                Failure($"'{path}' is not a readable Crimson Skies game source.");
                return;
            }

            IReadOnlyList<CampaignMission> missions =
                CampaignMissionDocument.LoadMissions(source);
            _source = source;
            _sourcePath.Text = path;
            _missions.Clear();
            _missions.AddRange(missions);
            _missionPicker.Clear();
            for (int i = 0; i < _missions.Count; i++)
            {
                CampaignMission mission = _missions[i];
                string chapter =
                    mission.ChapterFolder.ToUpperInvariant();
                string folder =
                    mission.MissionFolder.ToUpperInvariant();
                _missionPicker.AddItem(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"CM{mission.Ordinal:00}  {chapter}/{folder}  {mission.Desc}"),
                    i);
            }

            _missionPicker.Disabled = false;
            _missionPicker.Select(0);
            LoadMission(0);
        }
        catch (InvalidDataException e)
        {
            Failure("The campaign sequence did not read.", e);
        }
        catch (IOException e)
        {
            Failure("The game source could not be read.", e);
        }
        catch (UnauthorizedAccessException e)
        {
            Failure("The game source is not accessible.", e);
        }
        catch (ArgumentException e)
        {
            Failure("The game source path is invalid.", e);
        }
        catch (NotSupportedException e)
        {
            Failure("The game source path is invalid.", e);
        }
    }

    private void LoadMission(int index)
    {
        if (_source == null || (uint)index >= (uint)_missions.Count)
        {
            return;
        }

        try
        {
            CampaignMission mission = _missions[index];
            _document = CampaignMissionDocument.Load(_source, mission);
            _readerPicker.Clear();
            foreach (ZrdrFile file in _document.Files)
            {
                _readerPicker.AddItem(file.Key);
            }

            _readerPicker.Disabled = _document.Files.Count == 0;
            BindPlayerInit();
            BindRosterStarts();
            BindWeatherCloudBand();
            BindWeatherWind();
            BindWeatherFog();
            _typedTabs.Visible = true;
            if (_document.Files.Count > 0)
            {
                _readerPicker.Select(0);
                RebuildReader(0);
            }
            else
            {
                _readerTree.Clear();
            }

            UpdateExportState();
            string chapter =
                mission.ChapterFolder.ToUpperInvariant();
            string folder =
                mission.MissionFolder.ToUpperInvariant();
            Success(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Loaded CM{mission.Ordinal:00} {chapter}/{folder}: {_document.Files.Count} readers."));
        }
        catch (InvalidDataException e)
        {
            ClearDocument();
            Failure("The mission reader archive did not decode.", e);
        }
        catch (IOException e)
        {
            ClearDocument();
            Failure("The mission reader archive could not be read.", e);
        }
        catch (UnauthorizedAccessException e)
        {
            ClearDocument();
            Failure("The mission reader archive is not accessible.", e);
        }
    }

    private void RebuildReader(int fileIndex)
    {
        if (_document == null
            || (uint)fileIndex >= (uint)_document.Files.Count)
        {
            return;
        }

        _rebuildingTree = true;
        try
        {
            _readerTree.Clear();
            ZrdrFile file = _document.Files[fileIndex];
            TreeItem root = _readerTree.CreateItem();
            root.SetText(0, file.Key);
            root.SetText(1, "reader");
            root.SetText(
                2,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{file.Root.Count} items"));

            var path = new List<int>();
            for (int i = 0; i < file.Root.Count; i++)
            {
                path.Add(i);
                AddValue(root, path, file.Root[i], depth: 0);
                path.RemoveAt(path.Count - 1);
            }
        }
        finally
        {
            _rebuildingTree = false;
        }
    }

    private void BuildPlayerInitPanel()
    {
        _playerInitPanel = new VBoxContainer
        {
            Name = "Player Start",
            Visible = false,
        };
        _playerInitPanel.AddChild(new Label
        {
            Text = "Player start (PLAYER_INIT)",
        });

        _playerInitPending = new Label();
        _playerInitPanel.AddChild(_playerInitPending);

        _playerInitGrid = new GridContainer
        {
            Columns = 2,
        };
        AddPlayerInitField(PlayerInitField.X, "X (m)");
        AddPlayerInitField(PlayerInitField.Y, "Y (m)");
        AddPlayerInitField(PlayerInitField.Z, "Z (m)");
        AddPlayerInitField(PlayerInitField.Pitch, "Pitch (deg)");
        AddPlayerInitField(PlayerInitField.Yaw, "Yaw (deg)");
        AddPlayerInitField(PlayerInitField.Roll, "Roll (deg)");
        AddPlayerInitField(PlayerInitField.Throttle, "Throttle (0-1)");
        AddPlayerInitField(PlayerInitField.Speed, "Speed (m/s)");
        _playerInitPanel.AddChild(_playerInitGrid);

        _applyPlayerInit = Button("Apply player start", ApplyPlayerInit);
        _playerInitPanel.AddChild(_applyPlayerInit);
        _playerInitPanel.AddChild(new Label
        {
            Text = "Pitch and roll are preserved; CSVM currently applies yaw only.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        _playerInitStatus = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _playerInitPanel.AddChild(_playerInitStatus);
        _typedTabs.AddChild(_playerInitPanel);
    }

    private void AddPlayerInitField(
        PlayerInitField field,
        string label)
    {
        _playerInitGrid.AddChild(new Label
        {
            Text = label,
        });
        LineEdit edit = new()
        {
            Name = field.ToString(),
        };
        _playerInitFields.Add(field, edit);
        _playerInitGrid.AddChild(edit);
    }

    private void BindPlayerInit()
    {
        if (_document == null)
        {
            HidePlayerInit();
            return;
        }

        _playerInitPanel.Visible = true;
        if (!CampaignPlayerInit.TryBind(
                _document,
                out CampaignPlayerInit? binding,
                out string problem))
        {
            _playerInit = null;
            SetPlayerInitEditable(false);
            _playerInitPending.Text = "Pending flag: unavailable";
            _playerInitStatus.Text = problem;
            return;
        }

        _playerInit = binding;
        SetPlayerInitEditable(true);
        CampaignPlayerInitState state = binding.State;
        _playerInitPending.Text =
            $"Pending flag (read-only): {FormatFloat(binding.Pending)}";
        SetPlayerInitField(PlayerInitField.X, state.X);
        SetPlayerInitField(PlayerInitField.Y, state.Y);
        SetPlayerInitField(PlayerInitField.Z, state.Z);
        SetPlayerInitField(PlayerInitField.Pitch, state.PitchDeg);
        SetPlayerInitField(PlayerInitField.Yaw, state.YawDeg);
        SetPlayerInitField(PlayerInitField.Roll, state.RollDeg);
        SetPlayerInitField(PlayerInitField.Throttle, state.ThrottleFrac);
        SetPlayerInitField(PlayerInitField.Speed, state.SpeedMps);
        _playerInitStatus.Text =
            $"Speed is stored as m/s divided by {FormatFloat(SpawnPoints.SpeedScale)}.";
    }

    private void HidePlayerInit()
    {
        _playerInit = null;
        if (_playerInitPanel != null)
        {
            _playerInitPanel.Visible = false;
        }
    }

    private void SetPlayerInitEditable(bool editable)
    {
        foreach (LineEdit field in _playerInitFields.Values)
        {
            field.Editable = editable;
        }

        _applyPlayerInit.Disabled = !editable;
    }

    private void SetPlayerInitField(
        PlayerInitField field,
        float value) =>
        _playerInitFields[field].Text = FormatFloat(value);

    private void ApplyPlayerInit()
    {
        if (_playerInit == null)
        {
            return;
        }

        try
        {
            CampaignPlayerInitState state = new(
                PlayerInitValue(PlayerInitField.X),
                PlayerInitValue(PlayerInitField.Y),
                PlayerInitValue(PlayerInitField.Z),
                PlayerInitValue(PlayerInitField.Pitch),
                PlayerInitValue(PlayerInitField.Yaw),
                PlayerInitValue(PlayerInitField.Roll),
                PlayerInitValue(PlayerInitField.Throttle),
                PlayerInitValue(PlayerInitField.Speed));
            bool changed = _playerInit.Apply(state);
            BindPlayerInit();
            RebuildSelectedReader();
            UpdateExportState();
            Success(changed
                ? "Updated PLAYER_INIT."
                : "PLAYER_INIT is unchanged.");
        }
        catch (FormatException e)
        {
            _playerInitStatus.Text = e.Message;
        }
        catch (ArgumentException e)
        {
            _playerInitStatus.Text = e.Message;
        }
        catch (InvalidOperationException e)
        {
            _playerInitStatus.Text = e.Message;
            Log.Warn("ui", $"campaign editor: {e.Message}");
        }
    }

    private float PlayerInitValue(
        PlayerInitField field)
    {
        string text = _playerInitFields[field].Text;
        if (!float.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float value)
            || !float.IsFinite(value))
        {
            throw new FormatException(
                $"{field} must be a finite number.");
        }

        return value;
    }

    private void RebuildSelectedReader()
    {
        int index = _readerPicker.Selected;
        if (index >= 0)
        {
            RebuildReader(index);
        }
    }

    private void BuildRosterStartPanel()
    {
        _rosterStartPanel = new VBoxContainer
        {
            Name = "Roster Starts",
            Visible = false,
        };

        _rosterStartPicker = new OptionButton
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _rosterStartPicker.ItemSelected += ShowRosterStart;
        _rosterStartPanel.AddChild(_rosterStartPicker);

        _rosterStartMeta = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _rosterStartPanel.AddChild(_rosterStartMeta);

        _rosterStartGrid = new GridContainer
        {
            Columns = 2,
        };
        AddRosterStartField(RosterStartField.X, "X (m)");
        AddRosterStartField(RosterStartField.Y, "Y (m)");
        AddRosterStartField(RosterStartField.Z, "Z (m)");
        AddRosterStartField(RosterStartField.Yaw, "Yaw (deg)");
        AddRosterStartField(RosterStartField.Team, "Team (-1 unset)");
        AddRosterStartField(RosterStartField.Group, "Group");
        _rosterStartPanel.AddChild(_rosterStartGrid);

        _applyRosterStart = Button(
            "Apply roster start",
            ApplyRosterStart);
        _rosterStartPanel.AddChild(_applyRosterStart);
        _rosterStartStatus = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _rosterStartPanel.AddChild(_rosterStartStatus);
        _typedTabs.AddChild(_rosterStartPanel);
    }

    private void AddRosterStartField(
        RosterStartField field,
        string label)
    {
        _rosterStartGrid.AddChild(new Label
        {
            Text = label,
        });
        LineEdit edit = new()
        {
            Name = field.ToString(),
        };
        _rosterStartFields.Add(field, edit);
        _rosterStartGrid.AddChild(edit);
    }

    private void BindRosterStarts()
    {
        if (_document == null)
        {
            HideRosterStarts();
            return;
        }

        int selected =
            Math.Max(0, _rosterStartPicker.Selected);
        _rosterStartPanel.Visible = true;
        _rosterStartPicker.Clear();
        if (!CampaignRosterStarts.TryBind(
                _document,
                out CampaignRosterStarts? binding,
                out string problem))
        {
            _rosterStarts = null;
            _rosterStartPicker.Disabled = true;
            SetRosterStartEditable(false);
            _rosterStartMeta.Text = "Roster start: unavailable";
            _rosterStartStatus.Text = problem;
            return;
        }

        _rosterStarts = binding;
        foreach (CampaignRosterStart entry in binding.Entries)
        {
            string suffix = entry.IsPlayer
                ? " (PLAYER_INIT owns spawn)"
                : entry.Enabled
                    ? ""
                    : " (generator template)";
            _rosterStartPicker.AddItem(
                entry.Name + suffix);
        }

        _rosterStartPicker.Disabled =
            binding.Entries.Count == 0;
        if (binding.Entries.Count == 0)
        {
            SetRosterStartEditable(false);
            _rosterStartMeta.Text = "Roster start: unavailable";
            _rosterStartStatus.Text =
                "'aiv.json' carries no roster entries";
            return;
        }

        selected =
            Math.Min(selected, binding.Entries.Count - 1);
        _rosterStartPicker.Select(selected);
        ShowRosterStart(selected);
    }

    private void HideRosterStarts()
    {
        _rosterStarts = null;
        if (_rosterStartPanel != null)
        {
            _rosterStartPanel.Visible = false;
        }
    }

    private void ShowRosterStart(long selected)
    {
        if (_rosterStarts == null
            || (ulong)selected >= (ulong)_rosterStarts.Entries.Count)
        {
            return;
        }

        CampaignRosterStart entry =
            _rosterStarts.Entries[(int)selected];
        CampaignRosterStartState state =
            entry.State;
        SetRosterStartField(RosterStartField.X, state.X);
        SetRosterStartField(RosterStartField.Y, state.Y);
        SetRosterStartField(RosterStartField.Z, state.Z);
        SetRosterStartField(RosterStartField.Yaw, state.YawDeg);
        SetRosterStartField(RosterStartField.Team, state.Team);
        SetRosterStartField(RosterStartField.Group, state.Group);

        if (entry.IsPlayer)
        {
            SetRosterStartEditable(false);
            _rosterStartMeta.Text =
                $"{entry.Name}: enabled={entry.Enabled.ToString().ToLowerInvariant()}";
            _rosterStartStatus.Text =
                "The human player is placed by PLAYER_INIT, so this roster pose is read-only.";
            return;
        }

        SetRosterStartEditable(true);
        _rosterStartMeta.Text = entry.Enabled
            ? $"{entry.Name}: initial roster aircraft"
            : $"{entry.Name}: enemy-generator parameter template";
        _rosterStartStatus.Text =
            "Position, yaw, team, and mission-logic group write to the raw roster block.";
    }

    private void SetRosterStartEditable(bool editable)
    {
        foreach (LineEdit field in _rosterStartFields.Values)
        {
            field.Editable = editable;
        }

        _applyRosterStart.Disabled = !editable;
    }

    private void SetRosterStartField(
        RosterStartField field,
        float value) =>
        _rosterStartFields[field].Text =
            FormatFloat(value);

    private void SetRosterStartField(
        RosterStartField field,
        int value) =>
        _rosterStartFields[field].Text =
            value.ToString(CultureInfo.InvariantCulture);

    private void ApplyRosterStart()
    {
        CampaignRosterStart? entry =
            SelectedRosterStart();
        if (entry == null
            || entry.IsPlayer)
        {
            return;
        }

        try
        {
            CampaignRosterStartState state = new(
                RosterStartFloat(RosterStartField.X),
                RosterStartFloat(RosterStartField.Y),
                RosterStartFloat(RosterStartField.Z),
                RosterStartFloat(RosterStartField.Yaw),
                RosterStartInteger(RosterStartField.Team),
                RosterStartInteger(RosterStartField.Group));
            bool changed =
                entry.Apply(state);
            BindRosterStarts();
            RebuildSelectedReader();
            UpdateExportState();
            Success(changed
                ? $"Updated roster start '{entry.Name}'."
                : $"Roster start '{entry.Name}' is unchanged.");
        }
        catch (FormatException e)
        {
            _rosterStartStatus.Text = e.Message;
        }
        catch (ArgumentException e)
        {
            _rosterStartStatus.Text = e.Message;
        }
        catch (InvalidOperationException e)
        {
            _rosterStartStatus.Text = e.Message;
            Log.Warn("ui", $"campaign editor: {e.Message}");
        }
    }

    private CampaignRosterStart? SelectedRosterStart()
    {
        int index =
            _rosterStartPicker.Selected;
        return _rosterStarts != null
            && (uint)index < (uint)_rosterStarts.Entries.Count
            ? _rosterStarts.Entries[index]
            : null;
    }

    private float RosterStartFloat(
        RosterStartField field)
    {
        string text =
            _rosterStartFields[field].Text;
        if (!float.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float value)
            || !float.IsFinite(value))
        {
            throw new FormatException(
                $"{field} must be a finite number.");
        }

        return value;
    }

    private int RosterStartInteger(
        RosterStartField field)
    {
        string text =
            _rosterStartFields[field].Text;
        if (!int.TryParse(
                text,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int value))
        {
            throw new FormatException(
                $"{field} must be an integer.");
        }

        return value;
    }

    private void BuildWeatherCloudBandPanel()
    {
        _weatherCloudBandPanel = new VBoxContainer
        {
            Name = "Cloud Band",
            Visible = false,
        };
        _weatherCloudBandPanel.AddChild(new Label
        {
            Text = "Weather cloud band (CLOUD_COVER)",
        });

        _weatherCloudBandGrid = new GridContainer
        {
            Columns = 2,
        };
        AddWeatherCloudBandField(
            WeatherCloudBandField.Top,
            "Clear top altitude (m)");
        AddWeatherCloudBandField(
            WeatherCloudBandField.Bottom,
            "Clear bottom altitude (m)");
        AddWeatherCloudBandField(
            WeatherCloudBandField.Thickness,
            "Opaque core depth (m)");
        AddWeatherCloudBandField(
            WeatherCloudBandField.TopColorR,
            "Top color red (raw)");
        AddWeatherCloudBandField(
            WeatherCloudBandField.TopColorG,
            "Top color green (raw)");
        AddWeatherCloudBandField(
            WeatherCloudBandField.TopColorB,
            "Top color blue (raw)");
        AddWeatherCloudBandField(
            WeatherCloudBandField.BottomColorR,
            "Bottom color red (raw)");
        AddWeatherCloudBandField(
            WeatherCloudBandField.BottomColorG,
            "Bottom color green (raw)");
        AddWeatherCloudBandField(
            WeatherCloudBandField.BottomColorB,
            "Bottom color blue (raw)");
        _weatherCloudBandPanel.AddChild(_weatherCloudBandGrid);

        _applyWeatherCloudBand = Button(
            "Apply cloud band",
            ApplyWeatherCloudBand);
        _weatherCloudBandPanel.AddChild(_applyWeatherCloudBand);
        _weatherCloudBandStatus = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _weatherCloudBandPanel.AddChild(_weatherCloudBandStatus);
        _typedTabs.AddChild(_weatherCloudBandPanel);
    }

    private void AddWeatherCloudBandField(
        WeatherCloudBandField field,
        string label)
    {
        _weatherCloudBandGrid.AddChild(new Label
        {
            Text = label,
        });
        LineEdit edit = new()
        {
            Name = field.ToString(),
        };
        _weatherCloudBandFields.Add(field, edit);
        _weatherCloudBandGrid.AddChild(edit);
    }

    private void BindWeatherCloudBand()
    {
        if (_document == null)
        {
            HideWeatherCloudBand();
            return;
        }

        _weatherCloudBandPanel.Visible = true;
        if (!CampaignWeatherCloudBand.TryBind(
                _document,
                out CampaignWeatherCloudBand? binding,
                out string problem))
        {
            _weatherCloudBand = null;
            SetWeatherCloudBandEditable(false);
            _weatherCloudBandStatus.Text = problem;
            return;
        }

        _weatherCloudBand = binding;
        CampaignWeatherCloudBandState state =
            binding.State;
        SetWeatherCloudBandField(
            WeatherCloudBandField.Top,
            state.Top);
        SetWeatherCloudBandField(
            WeatherCloudBandField.Bottom,
            state.Bottom);
        SetWeatherCloudBandField(
            WeatherCloudBandField.Thickness,
            state.Thickness);
        SetWeatherCloudBandColor(
            state.TopColor,
            WeatherCloudBandField.TopColorR,
            WeatherCloudBandField.TopColorG,
            WeatherCloudBandField.TopColorB);
        SetWeatherCloudBandColor(
            state.BottomColor,
            WeatherCloudBandField.BottomColorR,
            WeatherCloudBandField.BottomColorG,
            WeatherCloudBandField.BottomColorB);
        SetWeatherCloudBandEditable(
            true,
            state.TopColor.HasValue,
            state.BottomColor.HasValue);
        string topColor =
            CloudColorStatus(
                "TOP_COLOR",
                state.TopColor,
                binding.TopColorUsesByteEncoding);
        string bottomColor =
            CloudColorStatus(
                "BOTTOM_COLOR",
                state.BottomColor,
                binding.BottomColorUsesByteEncoding);
        _weatherCloudBandStatus.Text =
            "The band midpoint also switches the cloud deck between ceiling and floor geometry. "
            + $"{topColor} {bottomColor}";
    }

    private void HideWeatherCloudBand()
    {
        _weatherCloudBand = null;
        if (_weatherCloudBandPanel != null)
        {
            _weatherCloudBandPanel.Visible = false;
        }
    }

    private void SetWeatherCloudBandEditable(
        bool editable,
        bool topColorEditable = false,
        bool bottomColorEditable = false)
    {
        _weatherCloudBandFields[WeatherCloudBandField.Top].Editable =
            editable;
        _weatherCloudBandFields[WeatherCloudBandField.Bottom].Editable =
            editable;
        _weatherCloudBandFields[WeatherCloudBandField.Thickness].Editable =
            editable;
        _weatherCloudBandFields[WeatherCloudBandField.TopColorR].Editable =
            editable && topColorEditable;
        _weatherCloudBandFields[WeatherCloudBandField.TopColorG].Editable =
            editable && topColorEditable;
        _weatherCloudBandFields[WeatherCloudBandField.TopColorB].Editable =
            editable && topColorEditable;
        _weatherCloudBandFields[WeatherCloudBandField.BottomColorR].Editable =
            editable && bottomColorEditable;
        _weatherCloudBandFields[WeatherCloudBandField.BottomColorG].Editable =
            editable && bottomColorEditable;
        _weatherCloudBandFields[WeatherCloudBandField.BottomColorB].Editable =
            editable && bottomColorEditable;

        _applyWeatherCloudBand.Disabled = !editable;
    }

    private void SetWeatherCloudBandField(
        WeatherCloudBandField field,
        float value) =>
        _weatherCloudBandFields[field].Text =
            FormatFloat(value);

    private void SetWeatherCloudBandColor(
        CampaignWeatherColorState? color,
        WeatherCloudBandField red,
        WeatherCloudBandField green,
        WeatherCloudBandField blue)
    {
        if (color is { } value)
        {
            SetWeatherCloudBandField(red, value.R);
            SetWeatherCloudBandField(green, value.G);
            SetWeatherCloudBandField(blue, value.B);
            return;
        }

        _weatherCloudBandFields[red].Text = "";
        _weatherCloudBandFields[green].Text = "";
        _weatherCloudBandFields[blue].Text = "";
    }

    private string CloudColorStatus(
        string name,
        CampaignWeatherColorState? color,
        bool usesByteEncoding)
    {
        if (!color.HasValue)
        {
            return $"{name} is absent and will not be created.";
        }

        string encoding =
            usesByteEncoding
                ? "0-255"
                : "normalized 0-1";
        return $"{name} uses authored {encoding} values.";
    }

    private void ApplyWeatherCloudBand()
    {
        if (_weatherCloudBand == null)
        {
            return;
        }

        try
        {
            CampaignWeatherCloudBandState current =
                _weatherCloudBand.State;
            CampaignWeatherCloudBandState state = new(
                WeatherCloudBandValue(WeatherCloudBandField.Top),
                WeatherCloudBandValue(WeatherCloudBandField.Bottom),
                WeatherCloudBandValue(WeatherCloudBandField.Thickness),
                current.TopColor.HasValue
                    ? WeatherCloudBandColor(
                        WeatherCloudBandField.TopColorR,
                        WeatherCloudBandField.TopColorG,
                        WeatherCloudBandField.TopColorB)
                    : null,
                current.BottomColor.HasValue
                    ? WeatherCloudBandColor(
                        WeatherCloudBandField.BottomColorR,
                        WeatherCloudBandField.BottomColorG,
                        WeatherCloudBandField.BottomColorB)
                    : null);
            bool changed =
                _weatherCloudBand.Apply(state);
            BindWeatherCloudBand();
            RebuildSelectedReader();
            UpdateExportState();
            Success(changed
                ? "Updated weather CLOUD_COVER."
                : "Weather CLOUD_COVER is unchanged.");
        }
        catch (FormatException e)
        {
            _weatherCloudBandStatus.Text = e.Message;
        }
        catch (ArgumentException e)
        {
            _weatherCloudBandStatus.Text = e.Message;
        }
        catch (InvalidOperationException e)
        {
            _weatherCloudBandStatus.Text = e.Message;
            Log.Warn("ui", $"campaign editor: {e.Message}");
        }
    }

    private float WeatherCloudBandValue(
        WeatherCloudBandField field)
    {
        string text =
            _weatherCloudBandFields[field].Text;
        if (!float.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float value)
            || !float.IsFinite(value))
        {
            throw new FormatException(
                $"{field} must be a finite number.");
        }

        return value;
    }

    private CampaignWeatherColorState WeatherCloudBandColor(
        WeatherCloudBandField red,
        WeatherCloudBandField green,
        WeatherCloudBandField blue) =>
        new(
            WeatherCloudBandValue(red),
            WeatherCloudBandValue(green),
            WeatherCloudBandValue(blue));

    private void BuildWeatherWindPanel()
    {
        _weatherWindPanel = new VBoxContainer
        {
            Name = "Wind",
            Visible = false,
        };
        _weatherWindPanel.AddChild(new Label
        {
            Text = "Weather wind (WIND)",
        });

        _weatherWindGrid = new GridContainer
        {
            Columns = 2,
        };
        AddWeatherWindField(
            WeatherWindField.StaticX,
            "Static X (m/s)");
        AddWeatherWindField(
            WeatherWindField.StaticY,
            "Static Y (m/s)");
        AddWeatherWindField(
            WeatherWindField.StaticZ,
            "Static Z (m/s)");
        AddWeatherWindField(
            WeatherWindField.RandomMaxSpeed,
            "Random max speed (m/s)");
        AddWeatherWindField(
            WeatherWindField.RandomAccel,
            "Random acceleration (m/s/s)");
        AddWeatherWindField(
            WeatherWindField.RandomAngVel,
            "Random turn rate (deg/s)");
        _weatherWindPanel.AddChild(_weatherWindGrid);

        _applyWeatherWind = Button(
            "Apply weather wind",
            ApplyWeatherWind);
        _weatherWindPanel.AddChild(_applyWeatherWind);
        _weatherWindStatus = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _weatherWindPanel.AddChild(_weatherWindStatus);
        _typedTabs.AddChild(_weatherWindPanel);
    }

    private void AddWeatherWindField(
        WeatherWindField field,
        string label)
    {
        _weatherWindGrid.AddChild(new Label
        {
            Text = label,
        });
        LineEdit edit = new()
        {
            Name = field.ToString(),
        };
        _weatherWindFields.Add(field, edit);
        _weatherWindGrid.AddChild(edit);
    }

    private void BindWeatherWind()
    {
        if (_document == null)
        {
            HideWeatherWind();
            return;
        }

        _weatherWindPanel.Visible = true;
        if (!CampaignWeatherWind.TryBind(
                _document,
                out CampaignWeatherWind? binding,
                out string problem))
        {
            _weatherWind = null;
            SetWeatherWindEditable(false);
            _weatherWindStatus.Text = problem;
            return;
        }

        _weatherWind = binding;
        SetWeatherWindEditable(true);
        CampaignWeatherWindState state =
            binding.State;
        SetWeatherWindField(
            WeatherWindField.StaticX,
            state.StaticX);
        SetWeatherWindField(
            WeatherWindField.StaticY,
            state.StaticY);
        SetWeatherWindField(
            WeatherWindField.StaticZ,
            state.StaticZ);
        SetWeatherWindField(
            WeatherWindField.RandomMaxSpeed,
            state.RandomMaxSpeed);
        SetWeatherWindField(
            WeatherWindField.RandomAccel,
            state.RandomAccel);
        SetWeatherWindField(
            WeatherWindField.RandomAngVel,
            state.RandomAngVelDeg);
        _weatherWindStatus.Text =
            "Wind affects puffer particles; cloud clutter does not currently consume it.";
    }

    private void HideWeatherWind()
    {
        _weatherWind = null;
        if (_weatherWindPanel != null)
        {
            _weatherWindPanel.Visible = false;
        }
    }

    private void SetWeatherWindEditable(bool editable)
    {
        foreach (LineEdit field in _weatherWindFields.Values)
        {
            field.Editable = editable;
        }

        _applyWeatherWind.Disabled = !editable;
    }

    private void SetWeatherWindField(
        WeatherWindField field,
        float value) =>
        _weatherWindFields[field].Text =
            FormatFloat(value);

    private void ApplyWeatherWind()
    {
        if (_weatherWind == null)
        {
            return;
        }

        try
        {
            CampaignWeatherWindState state = new(
                WeatherWindValue(WeatherWindField.StaticX),
                WeatherWindValue(WeatherWindField.StaticY),
                WeatherWindValue(WeatherWindField.StaticZ),
                WeatherWindValue(WeatherWindField.RandomMaxSpeed),
                WeatherWindValue(WeatherWindField.RandomAccel),
                WeatherWindValue(WeatherWindField.RandomAngVel));
            bool changed =
                _weatherWind.Apply(state);
            BindWeatherWind();
            RebuildSelectedReader();
            UpdateExportState();
            Success(changed
                ? "Updated weather WIND."
                : "Weather WIND is unchanged.");
        }
        catch (FormatException e)
        {
            _weatherWindStatus.Text = e.Message;
        }
        catch (ArgumentException e)
        {
            _weatherWindStatus.Text = e.Message;
        }
        catch (InvalidOperationException e)
        {
            _weatherWindStatus.Text = e.Message;
            Log.Warn("ui", $"campaign editor: {e.Message}");
        }
    }

    private float WeatherWindValue(
        WeatherWindField field)
    {
        string text =
            _weatherWindFields[field].Text;
        if (!float.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float value)
            || !float.IsFinite(value))
        {
            throw new FormatException(
                $"{field} must be a finite number.");
        }

        return value;
    }

    private void BuildWeatherFogPanel()
    {
        _weatherFogPanel = new VBoxContainer
        {
            Name = "Fog",
            Visible = false,
        };
        _weatherFogPanel.AddChild(new Label
        {
            Text = "Weather fog zones",
        });

        _weatherFogPicker = new OptionButton
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _weatherFogPicker.ItemSelected += ShowWeatherFogZone;
        _weatherFogPanel.AddChild(_weatherFogPicker);

        _weatherFogGrid = new GridContainer
        {
            Columns = 2,
        };
        AddWeatherFogField(
            WeatherFogField.ColorR,
            "Fog red (raw)");
        AddWeatherFogField(
            WeatherFogField.ColorG,
            "Fog green (raw)");
        AddWeatherFogField(
            WeatherFogField.ColorB,
            "Fog blue (raw)");
        AddWeatherFogField(
            WeatherFogField.Near,
            "Distance near (m)");
        AddWeatherFogField(
            WeatherFogField.Far,
            "Distance far (m)");
        AddWeatherFogField(
            WeatherFogField.Low,
            "Altitude low (m)");
        AddWeatherFogField(
            WeatherFogField.High,
            "Altitude high (m)");
        _weatherFogPanel.AddChild(_weatherFogGrid);

        _applyWeatherFog = Button(
            "Apply weather fog",
            ApplyWeatherFog);
        _weatherFogPanel.AddChild(_applyWeatherFog);
        _weatherFogStatus = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _weatherFogPanel.AddChild(_weatherFogStatus);
        _typedTabs.AddChild(_weatherFogPanel);
    }

    private void AddWeatherFogField(
        WeatherFogField field,
        string label)
    {
        _weatherFogGrid.AddChild(new Label
        {
            Text = label,
        });
        LineEdit edit = new()
        {
            Name = field.ToString(),
        };
        _weatherFogFields.Add(field, edit);
        _weatherFogGrid.AddChild(edit);
    }

    private void BindWeatherFog()
    {
        if (_document == null)
        {
            HideWeatherFog();
            return;
        }

        int selected =
            Math.Max(0, _weatherFogPicker.Selected);
        _weatherFogPanel.Visible = true;
        _weatherFogPicker.Clear();
        if (!CampaignWeatherFogZones.TryBind(
                _document,
                out CampaignWeatherFogZones? binding,
                out string problem))
        {
            _weatherFog = null;
            _weatherFogPicker.Disabled = true;
            SetWeatherFogEditable(false);
            _weatherFogStatus.Text = problem;
            return;
        }

        _weatherFog = binding;
        foreach (CampaignWeatherFogZone zone in binding.Zones)
        {
            _weatherFogPicker.AddItem(zone.Name);
        }

        _weatherFogPicker.Disabled =
            binding.Zones.Count == 0;
        selected =
            Math.Min(selected, binding.Zones.Count - 1);
        _weatherFogPicker.Select(selected);
        ShowWeatherFogZone(selected);
    }

    private void HideWeatherFog()
    {
        _weatherFog = null;
        if (_weatherFogPanel != null)
        {
            _weatherFogPanel.Visible = false;
        }
    }

    private void ShowWeatherFogZone(long selected)
    {
        if (_weatherFog == null
            || (ulong)selected >= (ulong)_weatherFog.Zones.Count)
        {
            return;
        }

        CampaignWeatherFogZone zone =
            _weatherFog.Zones[(int)selected];
        CampaignWeatherFogZoneState state =
            zone.State;
        SetWeatherFogField(WeatherFogField.ColorR, state.ColorR);
        SetWeatherFogField(WeatherFogField.ColorG, state.ColorG);
        SetWeatherFogField(WeatherFogField.ColorB, state.ColorB);
        SetWeatherFogField(WeatherFogField.Near, state.Near);
        SetWeatherFogField(WeatherFogField.Far, state.Far);
        SetWeatherFogField(WeatherFogField.Low, state.Low);
        SetWeatherFogField(WeatherFogField.High, state.High);
        SetWeatherFogEditable(true);
        _weatherFogStatus.Text = zone.UsesByteColor
            ? "Color uses the authored 0-255 encoding. SW_* twins and other zone fields stay raw."
            : "Color uses the authored normalized 0-1 encoding. SW_* twins and other zone fields stay raw.";
    }

    private void SetWeatherFogEditable(bool editable)
    {
        foreach (LineEdit field in _weatherFogFields.Values)
        {
            field.Editable = editable;
        }

        _applyWeatherFog.Disabled = !editable;
    }

    private void SetWeatherFogField(
        WeatherFogField field,
        float value) =>
        _weatherFogFields[field].Text =
            FormatFloat(value);

    private void ApplyWeatherFog()
    {
        CampaignWeatherFogZone? zone =
            SelectedWeatherFogZone();
        if (zone == null)
        {
            return;
        }

        try
        {
            CampaignWeatherFogZoneState state = new(
                WeatherFogValue(WeatherFogField.ColorR),
                WeatherFogValue(WeatherFogField.ColorG),
                WeatherFogValue(WeatherFogField.ColorB),
                WeatherFogValue(WeatherFogField.Near),
                WeatherFogValue(WeatherFogField.Far),
                WeatherFogValue(WeatherFogField.Low),
                WeatherFogValue(WeatherFogField.High));
            bool changed =
                zone.Apply(state);
            BindWeatherFog();
            RebuildSelectedReader();
            UpdateExportState();
            Success(changed
                ? $"Updated weather fog '{zone.Name}'."
                : $"Weather fog '{zone.Name}' is unchanged.");
        }
        catch (FormatException e)
        {
            _weatherFogStatus.Text = e.Message;
        }
        catch (ArgumentException e)
        {
            _weatherFogStatus.Text = e.Message;
        }
        catch (InvalidOperationException e)
        {
            _weatherFogStatus.Text = e.Message;
            Log.Warn("ui", $"campaign editor: {e.Message}");
        }
    }

    private CampaignWeatherFogZone? SelectedWeatherFogZone()
    {
        int index =
            _weatherFogPicker.Selected;
        return _weatherFog != null
            && (uint)index < (uint)_weatherFog.Zones.Count
            ? _weatherFog.Zones[index]
            : null;
    }

    private float WeatherFogValue(
        WeatherFogField field)
    {
        string text =
            _weatherFogFields[field].Text;
        if (!float.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float value)
            || !float.IsFinite(value))
        {
            throw new FormatException(
                $"{field} must be a finite number.");
        }

        return value;
    }

    private void AddValue(
        TreeItem parent,
        List<int> path,
        object? value,
        int depth)
    {
        TreeItem item = _readerTree.CreateItem(parent);
        item.SetText(
            0,
            "[" + path[^1].ToString(CultureInfo.InvariantCulture) + "]");

        switch (value)
        {
            case List<object?> list:
                item.SetText(1, "list");
                item.SetText(
                    2,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{list.Count} items"));
                item.Collapsed = depth > 0;
                for (int i = 0; i < list.Count; i++)
                {
                    path.Add(i);
                    AddValue(item, path, list[i], depth + 1);
                    path.RemoveAt(path.Count - 1);
                }

                break;
            case string text:
                Editable(item, path, "string", text);
                break;
            case float number:
                Editable(
                    item,
                    path,
                    "float",
                    number.ToString("R", CultureInfo.InvariantCulture));
                break;
            case null:
                item.SetText(1, "null");
                item.SetText(2, "null");
                break;
            default:
                item.SetText(1, value.GetType().Name);
                item.SetText(
                    2,
                    Convert.ToString(
                        value,
                        CultureInfo.InvariantCulture) ?? "");
                break;
        }
    }

    private void Editable(
        TreeItem item,
        List<int> path,
        string type,
        string text)
    {
        item.SetText(1, type);
        item.SetText(2, text);
        item.SetEditable(2, true);
        item.SetMetadata(2, PathText(path));
    }

    private void OnLeafEdited()
    {
        if (_rebuildingTree
            || _document == null
            || _readerTree.GetEditedColumn() != 2)
        {
            return;
        }

        TreeItem? item = _readerTree.GetEdited();
        int fileIndex = _readerPicker.Selected;
        if (item == null
            || (uint)fileIndex >= (uint)_document.Files.Count)
        {
            return;
        }

        int[] path = ParsePath(item.GetMetadata(2).AsString());
        object? old = _document.ValueAt(fileIndex, path);
        string text = item.GetText(2);
        try
        {
            bool changed;
            switch (old)
            {
                case string:
                    changed = _document.SetLeaf(fileIndex, path, text);
                    break;
                case float:
                    if (!float.TryParse(
                            text,
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out float number)
                        || !float.IsFinite(number))
                    {
                        RestoreLeaf(item, old);
                        Failure($"'{text}' is not a finite invariant-culture float.");
                        return;
                    }

                    changed = _document.SetLeaf(fileIndex, path, number);
                    break;
                default:
                    RestoreLeaf(item, old);
                    Failure("Only existing string and float leaves are writable.");
                    return;
            }

            UpdateExportState();
            if (changed)
            {
                BindPlayerInit();
                BindRosterStarts();
                BindWeatherWind();
                BindWeatherFog();
                Success(
                    $"Edited {_document.Files[fileIndex].Key} [{PathText(path)}].");
            }
        }
        catch (InvalidOperationException e)
        {
            RestoreLeaf(item, old);
            Failure("The raw-tree edit was rejected.", e);
        }
        catch (ArgumentException e)
        {
            RestoreLeaf(item, old);
            Failure("The raw-tree edit was rejected.", e);
        }
    }

    private void Export()
    {
        if (_document == null)
        {
            Failure("Load a campaign mission before exporting.");
            return;
        }

        try
        {
            string projectRoot = ProjectSettings.GlobalizePath("res://");
            string output = _document.Export(
                _modPath.Text.Trim(),
                projectRoot);
            UpdateExportState();
            Success($"Exported the complete scope to '{output}'.");
        }
        catch (InvalidDataException e)
        {
            Failure("The complete mission scope did not round-trip.", e);
        }
        catch (IOException e)
        {
            Failure("The mission scope could not be written.", e);
        }
        catch (UnauthorizedAccessException e)
        {
            Failure("The modding folder is not writable.", e);
        }
        catch (InvalidOperationException e)
        {
            Failure("The selected modding folder is unsafe.", e);
        }
        catch (ArgumentException e)
        {
            Failure("The modding folder path is invalid.", e);
        }
        catch (NotSupportedException e)
        {
            Failure("The modding folder path is invalid.", e);
        }
    }

    private void ClearDocument()
    {
        _document = null;
        _typedTabs.Visible = false;
        HidePlayerInit();
        HideRosterStarts();
        HideWeatherCloudBand();
        HideWeatherWind();
        HideWeatherFog();
        _readerPicker.Clear();
        _readerPicker.Disabled = true;
        _readerTree.Clear();
        UpdateExportState();
    }

    private void UpdateExportState()
    {
        if (_exportButton != null)
        {
            _exportButton.Disabled = _document == null
                || string.IsNullOrWhiteSpace(_modPath.Text);
        }
    }

    private void OpenSourceDialog() =>
        OpenDialog(_sourceDialog, _sourcePath.Text);

    private void OpenModDialog() =>
        OpenDialog(_modDialog, _modPath.Text);

    private void OpenDialog(
        FileDialog dialog,
        string current)
    {
        string path = current.Trim();
        if (File.Exists(path))
        {
            path = Path.GetDirectoryName(path) ?? "";
        }

        if (Directory.Exists(path))
        {
            dialog.CurrentDir = Path.GetFullPath(path);
        }

        dialog.PopupCentered(new Vector2I(900, 650));
    }

    private FileDialog Picker(
        string title,
        bool foldersOnly) =>
        new()
        {
            FileMode = foldersOnly
                ? FileDialog.FileModeEnum.OpenDir
                : FileDialog.FileModeEnum.OpenAny,
            Filters = foldersOnly
                ? Array.Empty<string>()
                : new[]
                {
                    "*.iso, *.cue, *.ccd, *.bin, *.img ; Disc images",
                },
            Access = FileDialog.AccessEnum.Filesystem,
            UseNativeDialog = false,
            DisplayMode = FileDialog.DisplayModeEnum.List,
            Title = title,
            ModeOverridesTitle = false,
            FolderCreationEnabled = false,
            DeletingEnabled = false,
            ShowHiddenFiles = !OperatingSystem.IsWindows(),
        };

    private Button Button(
        string text,
        Action pressed)
    {
        var button = new Button
        {
            Text = text,
            FocusMode = FocusModeEnum.None,
        };
        button.Pressed += pressed;
        return button;
    }

    private int[] ParsePath(string text)
    {
        string[] parts = text.Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries);
        var path = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            path[i] = int.Parse(
                parts[i],
                NumberStyles.None,
                CultureInfo.InvariantCulture);
        }

        return path;
    }

    private string PathText(
        IReadOnlyList<int> path)
    {
        var parts = new string[path.Count];
        for (int i = 0; i < path.Count; i++)
        {
            parts[i] = path[i].ToString(CultureInfo.InvariantCulture);
        }

        return string.Join(",", parts);
    }

    private void RestoreLeaf(
        TreeItem item,
        object? value)
    {
        item.SetText(
            2,
            value is float number
                ? number.ToString("R", CultureInfo.InvariantCulture)
                : value?.ToString() ?? "null");
    }

    private string FormatFloat(float value) =>
        value.ToString("R", CultureInfo.InvariantCulture);

    private void Success(string message)
    {
        _status.Text = message;
        Log.Info("ui", $"campaign editor: {message}");
    }

    private void Failure(
        string message,
        Exception? error = null)
    {
        string detail = error == null
            ? message
            : $"{message} {error.Message}";
        _status.Text = detail;
        Log.Warn("ui", $"campaign editor: {detail}");
    }

}
