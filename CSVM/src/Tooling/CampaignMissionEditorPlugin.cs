using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using CSVM.Extraction;
using CSVM.Flight.Modes;
using CSVM.Mech3;
using CSVM.Spec;
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
            UndoManager = GetUndoRedo(),
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
    private readonly List<LineEdit> _rosterExtraFields = new();
    private readonly Dictionary<CampaignTargetLabel, LineEdit> _targetLabels = new();
    private readonly List<LineEdit> _zeppelinFields = new();
    private readonly List<LineEdit> _generatorFields = new();
    private readonly List<LineEdit> _objectiveFields = new();
    private readonly Dictionary<WeatherCloudBandField, LineEdit> _weatherCloudBandFields = new();
    private readonly Dictionary<WeatherWindField, LineEdit> _weatherWindFields = new();
    private readonly Dictionary<WeatherPrecipitationField, LineEdit> _weatherPrecipitationFields = new();
    private readonly Dictionary<WeatherFogField, LineEdit> _weatherFogFields = new();
    private readonly Dictionary<WeatherLightingField, LineEdit> _weatherLightingFields = new();

    private LineEdit _sourcePath = null!;
    private OptionButton _missionPicker = null!;
    private OptionButton _scopeMode = null!;
    private HBoxContainer _importRow = null!;
    private LineEdit _importPath = null!;
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
    private CampaignMissionPreviewPanel _preview = null!;
    private ScrollContainer _rosterTab = null!;
    private VBoxContainer _rosterStartPanel = null!;
    private OptionButton _rosterStartPicker = null!;
    private GridContainer _rosterStartGrid = null!;
    private Label _rosterStartMeta = null!;
    private Label _rosterStartStatus = null!;
    private Button _applyRosterStart = null!;
    private OptionButton _rosterGroupPicker = null!;
    private GridContainer _rosterGroupGrid = null!;
    private Label _rosterGroupNote = null!;
    private Label _rosterReferences = null!;
    private Label _rosterGroupStatus = null!;
    private Button _applyRosterGroup = null!;
    private ScrollContainer _targetTab = null!;
    private VBoxContainer _targetPanel = null!;
    private OptionButton _targetPicker = null!;
    private Label _targetScope = null!;
    private Label _targetReferences = null!;
    private Label _targetStatus = null!;
    private CheckBox _targetObjective = null!;
    private CheckBox _targetOther = null!;
    private Button _copyTargets = null!;
    private Button _applyTarget = null!;
    private ConfirmationDialog _targetCopyDialog = null!;
    private ScrollContainer _zeppelinTab = null!;
    private OptionButton _zeppelinPicker = null!;
    private OptionButton _zeppelinSectionPicker = null!;
    private OptionButton _zeppelinRowPicker = null!;
    private GridContainer _zeppelinGrid = null!;
    private Label _zeppelinReferences = null!;
    private Label _zeppelinStatus = null!;
    private Button _applyZeppelin = null!;
    private ScrollContainer _generatorTab = null!;
    private OptionButton _generatorPicker = null!;
    private OptionButton _generatorSectionPicker = null!;
    private OptionButton _generatorRowPicker = null!;
    private GridContainer _generatorGrid = null!;
    private Label _generatorContext = null!;
    private Label _generatorReferences = null!;
    private Label _generatorStatus = null!;
    private Button _applyGenerator = null!;
    private ScrollContainer _objectiveTab = null!;
    private OptionButton _objectivePicker = null!;
    private OptionButton _objectiveDirectivePicker = null!;
    private OptionButton _objectiveRowPicker = null!;
    private OptionButton _objectiveSwapPicker = null!;
    private GridContainer _objectiveGrid = null!;
    private Label _objectiveGraph = null!;
    private Label _objectiveReferences = null!;
    private Label _objectiveStatus = null!;
    private Button _applyObjective = null!;
    private Button _swapObjective = null!;
    private ConfirmationDialog _objectiveSwapDialog = null!;
    private ScrollContainer _dangerTab = null!;
    private OptionButton _dangerPicker = null!;
    private Label _dangerScope = null!;
    private Label _dangerDetails = null!;
    private Label _dangerReferences = null!;
    private Label _dangerStatus = null!;
    private CheckBox _dangerEnabled = null!;
    private CheckBox _dangerSnapshot = null!;
    private LineEdit _dangerSlot = null!;
    private Button _createDanger = null!;
    private Button _applyDanger = null!;
    private CampaignDangerRibbonPreview _dangerPreview = null!;
    private ScrollContainer _animationTab = null!;
    private OptionButton _startAnimationPicker = null!;
    private LineEdit _startAnimationName = null!;
    private Button _applyStartAnimation = null!;
    private Button _startAnimationUp = null!;
    private Button _startAnimationDown = null!;
    private Label _startAnimationStatus = null!;
    private Label _startAnimationReference = null!;
    private Label _readerSummaries = null!;
    private VBoxContainer _referencePanel = null!;
    private Label _referenceSummary = null!;
    private Label _referenceDetails = null!;
    private VBoxContainer _weatherSummaryPanel = null!;
    private OptionButton _weatherSummaryPicker = null!;
    private LineEdit _weatherSummaryAltitudeEdit = null!;
    private CampaignWeatherPreview _weatherSummaryPreview = null!;
    private Label _weatherSummaryCloud = null!;
    private Label _weatherSummaryProbe = null!;
    private Label _weatherSummaryZone = null!;
    private Label _weatherSummaryWind = null!;
    private Label _weatherSummaryPrecipitation = null!;
    private Label _weatherSummaryLighting = null!;
    private Label _weatherSummaryStatus = null!;
    private VBoxContainer _weatherCloudBandPanel = null!;
    private GridContainer _weatherCloudBandGrid = null!;
    private Label _weatherCloudBandStatus = null!;
    private Button _applyWeatherCloudBand = null!;
    private VBoxContainer _weatherWindPanel = null!;
    private GridContainer _weatherWindGrid = null!;
    private Label _weatherWindStatus = null!;
    private Button _applyWeatherWind = null!;
    private VBoxContainer _weatherPrecipitationPanel = null!;
    private OptionButton _weatherPrecipitationKind = null!;
    private GridContainer _weatherPrecipitationGrid = null!;
    private Label _weatherPrecipitationStatus = null!;
    private Button _applyWeatherPrecipitation = null!;
    private VBoxContainer _weatherFogPanel = null!;
    private OptionButton _weatherFogPicker = null!;
    private GridContainer _weatherFogGrid = null!;
    private Label _weatherFogStatus = null!;
    private Button _applyWeatherFog = null!;
    private VBoxContainer _weatherLightingPanel = null!;
    private OptionButton _weatherLightingPicker = null!;
    private GridContainer _weatherLightingGrid = null!;
    private CheckBox _weatherLightingBicolored = null!;
    private Label _weatherLightingStatus = null!;
    private Button _applyWeatherLighting = null!;
    private FileDialog _sourceDialog = null!;
    private FileDialog _importDialog = null!;
    private FileDialog _modDialog = null!;
    private IGameFileSource? _source;
    private GameData? _editorData;
    private CampaignMissionDocument? _document;
    private CampaignMissionUndoBridge? _undoBridge;
    private CampaignMissionDependencyBundle? _dependencyBundle;
    private CampaignReferenceDiagnostics? _referenceDiagnostics;
    private CampaignMissionImportResult? _importedScope;
    private CampaignPlayerInit? _playerInit;
    private CampaignRosterStarts? _rosterStarts;
    private CampaignMissionTargets? _targets;
    private CampaignMissionZeppelins? _zeppelins;
    private CampaignMissionGenerators? _generators;
    private CampaignMissionObjectives? _objectives;
    private CampaignMissionDangerZones? _dangerZones;
    private CampaignMissionStartAnimations? _startAnimations;
    private CampaignDangerZoneWorld? _dangerWorld;
    private CampaignMissionTargets? _pendingTargetCopy;
    private CampaignWeatherDiagnostics? _weatherDiagnostics;
    private CampaignWeatherCloudBand? _weatherCloudBand;
    private CampaignWeatherWind? _weatherWind;
    private CampaignWeatherPrecipitation? _weatherPrecipitation;
    private CampaignWeatherFogZones? _weatherFog;
    private CampaignWeatherLightingZones? _weatherLighting;
    private string? _weatherSummaryZoneName;
    private float _weatherSummaryAltitude;
    private bool _weatherSummaryAltitudeSet;
    private int? _importMissionIndex;
    private bool _rebuildingTree;

    private enum CampaignScopeMode
    {
        Retail,
        ExistingMod,
    }

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

    private enum WeatherPrecipitationField
    {
        ColorR,
        ColorG,
        ColorB,
        WindDir,
        WindVel,
        Gravity,
        Particles,
        AlphaGradientX,
        AlphaGradientY,
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

    private enum WeatherLightingField
    {
        ClipFar,
        SunDiffuse,
        SunAmbient,
        DiffuseColorR,
        DiffuseColorG,
        DiffuseColorB,
        AmbientColorR,
        AmbientColorG,
        AmbientColorB,
        SunPitch,
        SunYaw,
        SunRoll,
    }

    internal EditorUndoRedoManager? UndoManager { get; set; }

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(430f, 0f);
        AddThemeConstantOverride("separation", 6);

        AddChild(new Label { Text = "Campaign Mission Editor" });
        AddChild(new Label
        {
            Text = "Raw leaves are writable. Target flags and chapter copies use explicit typed actions; wider scopes stay read-only.",
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
        _missionPicker.ItemSelected += index => ChooseMission((int)index);
        AddChild(_missionPicker);

        _scopeMode = new OptionButton
        {
            Disabled = true,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _scopeMode.AddItem("Retail mission scope", 0);
        _scopeMode.AddItem("Existing complete mod scope", 1);
        _scopeMode.ItemSelected += index => ChangeScopeMode((int)index);
        AddChild(_scopeMode);

        _importPath = new LineEdit
        {
            PlaceholderText = "Existing zrdr.zip or unpacked zrdr folder",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _importPath.TextSubmitted += _ => ImportSelectedScope();
        _importRow = new HBoxContainer
        {
            Visible = false,
        };
        _importRow.AddChild(_importPath);
        _importRow.AddChild(Button("Browse", OpenImportDialog));
        _importRow.AddChild(Button("Import", ImportSelectedScope));
        AddChild(_importRow);

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
        BuildTargetPanel();
        BuildZeppelinPanel();
        BuildGeneratorPanel();
        BuildObjectivePanel();
        BuildDangerPanel();
        BuildAnimationPanel();
        BuildReferencePanel();
        BuildWeatherSummaryPanel();
        BuildWeatherCloudBandPanel();
        BuildWeatherWindPanel();
        BuildWeatherPrecipitationPanel();
        BuildWeatherFogPanel();
        BuildWeatherLightingPanel();
        BuildMissionPreviewPanel();

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

        _importDialog = Picker(
            "Choose an existing complete mission zrdr.zip or zrdr folder",
            foldersOnly: false,
            filters: new[] { "*.zip ; Reader scope archives" });
        _importDialog.DirSelected += path => ChooseImportScope(path);
        _importDialog.FileSelected += path => ChooseImportScope(path);
        AddChild(_importDialog);

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

    public override void _ExitTree()
    {
        _undoBridge?.Invalidate();
        _undoBridge = null;
        UndoManager = null;
        _preview?.Clear();
        _referenceDiagnostics?.Dispose();
        _referenceDiagnostics = null;
        _dependencyBundle = null;
        _document = null;
        _dangerZones = null;
        _startAnimations = null;
        _dangerWorld = null;
        _editorData = null;
        _source = null;
        _missions.Clear();
    }

    private static string DependencySummary(
        CampaignMissionDependencyBundle bundle)
    {
        IReadOnlyList<CampaignDependencyAvailability> states =
            bundle.Availabilities;
        int ready = 0;
        int requiredGaps = 0;
        int optionalGaps = 0;
        foreach (CampaignDependencyAvailability state in states)
        {
            if (state.State == CampaignDependencyState.Ready)
            {
                ready++;
            }
            else if (state.Optional)
            {
                optionalGaps++;
            }
            else
            {
                requiredGaps++;
            }
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"Dependencies: {ready}/{states.Count} ready; {requiredGaps} required, {optionalGaps} optional not ready.");
    }

    private static string ReferenceDisplay(ReferenceValue value)
    {
        if (!string.IsNullOrWhiteSpace(value.Name))
        {
            return $"'{value.Name}'";
        }

        if (value.Number is long number)
        {
            return number.ToString(CultureInfo.InvariantCulture);
        }

        return value.Segments.IsDefaultOrEmpty
            ? "(empty)"
            : $"'{string.Join("/", value.Segments)}'";
    }

    private static Label WeatherSummaryLabel() =>
        new()
        {
            AutowrapMode =
                TextServer.AutowrapMode.WordSmart,
        };

    private static string WeatherLightingEncoding(
        string name,
        bool? usesByteEncoding)
    {
        if (!usesByteEncoding.HasValue)
        {
            return
                $"{name} color is absent.";
        }

        string encoding =
            usesByteEncoding.Value
                ? "0-255"
                : "normalized 0-1";
        return
            $"{name} color uses authored {encoding} values.";
    }

    private void ChooseSource(string path)
    {
        _sourcePath.Text = path;
        LoadSource();
    }

    private void ChooseMission(int index)
    {
        if ((CampaignScopeMode)_scopeMode.Selected
            == CampaignScopeMode.ExistingMod
            && _importMissionIndex != index)
        {
            _importMissionIndex = null;
            _importPath.Text = "";
            ClearDocument();
            Failure(
                "Choose the complete mod scope that belongs to this mission.");
            return;
        }

        LoadMission(index);
    }

    private void ChangeScopeMode(int index)
    {
        var mode = (CampaignScopeMode)index;
        _importRow.Visible = mode == CampaignScopeMode.ExistingMod;
        _importMissionIndex = null;
        _importPath.Text = "";
        if (_source == null || _missionPicker.Selected < 0)
        {
            ClearDocument();
            return;
        }

        if (mode == CampaignScopeMode.Retail)
        {
            LoadMission(_missionPicker.Selected);
        }
        else
        {
            ClearDocument();
            Failure(
                "Choose an existing complete zrdr.zip or unpacked zrdr scope.");
        }
    }

    private void ChooseImportScope(string path)
    {
        _importPath.Text = path;
        ImportSelectedScope();
    }

    private void ImportSelectedScope()
    {
        if (_source == null || _missionPicker.Selected < 0)
        {
            Failure("Load a retail source and choose a mission first.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_importPath.Text))
        {
            Failure(
                "Choose an existing complete zrdr.zip or unpacked zrdr scope.");
            return;
        }

        _scopeMode.Select((int)CampaignScopeMode.ExistingMod);
        _importRow.Visible = true;
        _importMissionIndex = _missionPicker.Selected;
        LoadMission(_missionPicker.Selected);
    }

    private void LoadSource()
    {
        string named = _sourcePath.Text.Trim();
        ClearSource();
        if (named.Length == 0)
        {
            Failure("Choose a Crimson Skies install, disc, or image first.");
            return;
        }

        bool sourceReady = false;
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
            GameData data = GameData.LoadCampaignEditor(source);
            _source = source;
            _editorData = data;
            _scopeMode.Disabled = false;
            _sourcePath.Text = path;
            _missions.AddRange(missions);
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
            sourceReady = true;
            LoadMission(0);
        }
        catch (Exception e) when (
            e is InvalidDataException or IOException or UnauthorizedAccessException
                or ArgumentException or NotSupportedException)
        {
            Failure("The game source could not be loaded.", e);
        }
        finally
        {
            if (!sourceReady)
            {
                ClearSource();
            }
        }
    }

    private void LoadMission(int index)
    {
        ClearDocument();
        IGameFileSource? source = _source;
        GameData? data = _editorData;
        if (source == null
            || data == null
            || (uint)index >= (uint)_missions.Count)
        {
            return;
        }

        bool loaded = false;
        try
        {
            CampaignMission mission = _missions[index];
            CampaignMissionImportResult? imported = null;
            CampaignMissionDocument document;
            if ((CampaignScopeMode)_scopeMode.Selected
                == CampaignScopeMode.ExistingMod)
            {
                if (_importMissionIndex != index)
                {
                    throw new InvalidOperationException(
                        "The selected mod scope is not bound to this mission.");
                }

                imported = CampaignMissionImport.Load(
                    source,
                    mission,
                    _importPath.Text.Trim());
                document = imported.Document;
            }
            else
            {
                document = CampaignMissionDocument.Load(source, mission);
            }

            CampaignMissionDependencyBundle bundle =
                CampaignMissionDependencyBundle.Load(source, data, mission);
            var referenceDiagnostics =
                new CampaignReferenceDiagnostics(document, bundle);

            _document = document;
            EditorUndoRedoManager manager = UndoManager
                ?? throw new InvalidOperationException("The editor undo manager is unavailable.");
            _undoBridge = new CampaignMissionUndoBridge();
            _undoBridge.Bind(manager, document, RefreshAfterHistory, UpdateExportState, UndoRegistrationFailed);
            _dependencyBundle = bundle;
            _referenceDiagnostics = referenceDiagnostics;
            _dangerWorld = CampaignDangerZoneWorld.Read(bundle.World);
            _preview.Bind(document, bundle, _dangerWorld);
            _importedScope = imported;
            foreach (ZrdrFile file in document.Files)
            {
                _readerPicker.AddItem(file.Key);
            }

            _readerPicker.Disabled = document.Files.Count == 0;
            BindPlayerInit();
            BindRosterStarts();
            BindTargets();
            BindZeppelins();
            BindGenerators();
            BindObjectives();
            BindDangerZones();
            BindWeatherCloudBand();
            BindWeatherWind();
            BindWeatherPrecipitation();
            BindWeatherFog();
            BindWeatherLighting();
            BindWeatherSummary();
            BindReferenceDiagnostics();
            BindStartAnimations();
            _typedTabs.Visible = true;
            if (document.Files.Count > 0)
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
            string owner = imported == null
                ? "Retail scope."
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"Existing {imported.SourceKind} scope '{imported.SourcePath}', {imported.AdditionalReaders.Count} mod-only readers.");
            Success(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Loaded CM{mission.Ordinal:00} {chapter}/{folder}: {document.Files.Count} readers. {owner} {DependencySummary(bundle)}"));
            loaded = true;
        }
        catch (Exception e) when (
            e is InvalidDataException or IOException or UnauthorizedAccessException
                or ArgumentException or NotSupportedException
                or InvalidOperationException or JsonException)
        {
            Failure("The mission or its dependencies could not be loaded.", e);
        }
        finally
        {
            if (!loaded)
            {
                ClearDocument();
            }
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

    private void BuildMissionPreviewPanel()
    {
        var tab = new ScrollContainer
        {
            Name = "Preview",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _preview = new CampaignMissionPreviewPanel
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _preview.ShowSource += ShowPreviewSource;
        tab.AddChild(_preview);
        _typedTabs.AddChild(tab);
    }

    private void ShowPreviewSource(CampaignPreviewSource source)
    {
        if (_document == null || source.Scope != ReferenceScope.Mission
            || source.FileIndex is not int fileIndex
            || (uint)fileIndex >= (uint)_document.Files.Count
            || !string.Equals(_document.Files[fileIndex].Key, source.Container, StringComparison.Ordinal))
        {
            return;
        }

        _readerPicker.Select(fileIndex);
        RebuildReader(fileIndex);
        TreeItem? item = _readerTree.GetRoot();
        foreach (int index in source.Path)
        {
            if (item == null)
            {
                return;
            }

            item.Collapsed = false;
            TreeItem? child = item.GetFirstChild();
            for (int i = 0; i < index && child != null; i++)
            {
                child = child.GetNext();
            }

            item = child;
        }

        if (item != null)
        {
            item.Select(0);
            _readerTree.ScrollToItem(item);
            _readerTree.GrabFocus();
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
        _rosterTab = new ScrollContainer
        {
            Name = "Roster",
            Visible = false,
        };
        _rosterStartPanel = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _rosterTab.AddChild(_rosterStartPanel);

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

        _rosterStartPanel.AddChild(new Label
        {
            Text = "Existing AI fields",
        });
        _rosterGroupPicker = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _rosterGroupPicker.ItemSelected += _ => ShowRosterGroup();
        _rosterStartPanel.AddChild(_rosterGroupPicker);
        _rosterGroupGrid = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _rosterStartPanel.AddChild(_rosterGroupGrid);
        _applyRosterGroup = Button("Apply selected roster fields", ApplyRosterGroup);
        _rosterStartPanel.AddChild(_applyRosterGroup);
        _rosterGroupNote = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _rosterStartPanel.AddChild(_rosterGroupNote);
        _rosterReferences = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _rosterStartPanel.AddChild(_rosterReferences);
        _rosterGroupStatus = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _rosterStartPanel.AddChild(_rosterGroupStatus);
        _typedTabs.AddChild(_rosterTab);
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
        _rosterTab.Visible = true;
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
            ClearRosterGroups();
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
                string.Create(CultureInfo.InvariantCulture, $"{entry.Name} [row {entry.RawIndex}]{suffix}"));
        }

        _rosterStartPicker.Disabled =
            binding.Entries.Count == 0;
        if (binding.Entries.Count == 0)
        {
            SetRosterStartEditable(false);
            _rosterStartMeta.Text = "Roster start: unavailable";
            _rosterStartStatus.Text =
                "'aiv.json' carries no roster entries";
            ClearRosterGroups();
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
        if (_rosterTab != null)
        {
            _rosterTab.Visible = false;
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
            ShowRosterGroups(entry);
            return;
        }

        SetRosterStartEditable(true);
        _rosterStartMeta.Text = entry.Enabled
            ? $"{entry.Name}: initial roster aircraft"
            : entry.TemplateLabel is { } label
                ? $"{entry.Name}: generator template label '{label}'"
                : $"{entry.Name}: disabled block without a usable template label";
        _rosterStartStatus.Text =
            "Position, yaw, team, and mission-logic group write to the raw roster block. "
            + "A matching live world node can override the initial pose. "
            + "Generator launches supply their own position and patrol net.";
        ShowRosterGroups(entry);
    }

    private void ShowRosterGroups(CampaignRosterStart entry)
    {
        int selected = Math.Max(0, _rosterGroupPicker.Selected);
        _rosterGroupPicker.Clear();
        foreach (CampaignRosterFieldGroup group in entry.Groups)
        {
            _rosterGroupPicker.AddItem(group.Label);
        }

        _rosterReferences.Text = RosterReferences(entry);
        _rosterGroupPicker.Disabled = entry.Groups.Count == 0;
        if (entry.Groups.Count == 0)
        {
            ClearRosterFields();
            _applyRosterGroup.Disabled = true;
            _rosterGroupNote.Text = entry.IsPlayer
                ? "The player block is not used as an AI roster member."
                : "No supported existing AI fields are available; absent and malformed values remain in Raw.";
            _rosterGroupStatus.Text = "";
            return;
        }

        _rosterGroupPicker.Select(Math.Min(selected, entry.Groups.Count - 1));
        ShowRosterGroup();
    }

    private void ClearRosterGroups()
    {
        _rosterGroupPicker.Clear();
        _rosterGroupPicker.Disabled = true;
        ClearRosterFields();
        _applyRosterGroup.Disabled = true;
        _rosterGroupNote.Text = "";
        _rosterReferences.Text = "";
        _rosterGroupStatus.Text = "";
    }

    private void ClearRosterFields()
    {
        foreach (Node child in _rosterGroupGrid.GetChildren())
        {
            _rosterGroupGrid.RemoveChild(child);
            child.QueueFree();
        }

        _rosterExtraFields.Clear();
    }

    private CampaignRosterFieldGroup? SelectedRosterGroup()
    {
        CampaignRosterStart? entry = SelectedRosterStart();
        int index = _rosterGroupPicker.Selected;
        return entry != null && (uint)index < (uint)entry.Groups.Count
            ? entry.Groups[index]
            : null;
    }

    private void ShowRosterGroup()
    {
        ClearRosterFields();
        CampaignRosterStart? entry = SelectedRosterStart();
        CampaignRosterFieldGroup? group = SelectedRosterGroup();
        if (entry == null || group == null)
        {
            _applyRosterGroup.Disabled = true;
            return;
        }

        foreach (CampaignRosterField field in group.Fields)
        {
            _rosterGroupGrid.AddChild(new Label { Text = field.Label });
            object value = entry.Value(field);
            var edit = new LineEdit
            {
                Text = value is float number ? FormatFloat(number) : (string)value,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            _rosterExtraFields.Add(edit);
            _rosterGroupGrid.AddChild(edit);
        }

        _applyRosterGroup.Disabled = false;
        _rosterGroupNote.Text = group.Note
            + " Only supported existing leaves are shown; other values and tails remain in Raw.";
        _rosterGroupStatus.Text = "";
    }

    private string RosterReferences(CampaignRosterStart entry)
    {
        if (_rosterStarts == null || _referenceDiagnostics == null)
        {
            return "References unavailable.";
        }

        var text = new StringBuilder("Roster references:\n");
        if (entry.TemplateLabel != null)
        {
            text.AppendLine(
                "Template labels are read-only; completed planning keeps the last plannable duplicate.");
        }

        int count = 0;
        foreach (CheckedReference checkedReference in _referenceDiagnostics.Current.References)
        {
            MissionReference reference = checkedReference.Reference;
            ReferenceSource source = reference.Source;
            bool own = source.Scope == ReferenceScope.Mission
                && source.FileIndex == _rosterStarts.FileIndex
                && source.Path.Length > 0
                && source.Path[0] == entry.RawIndex;
            bool template = entry.TemplateLabel != null
                && reference.Rule == ReferenceRule.TemplateName
                && string.Equals(reference.Value.Name, entry.TemplateLabel, StringComparison.OrdinalIgnoreCase);
            if (!own && !template)
            {
                continue;
            }

            if (count == 30)
            {
                text.AppendLine("Additional references are available in Diagnostics.");
                break;
            }

            text.Append(template ? "Generator template " : reference.Kind + " ");
            text.Append(ReferenceDisplay(reference.Value));
            text.Append(": ");
            text.Append(checkedReference.State);
            if (!string.IsNullOrEmpty(checkedReference.Detail))
            {
                text.Append(" - ");
                text.Append(checkedReference.Detail);
            }

            text.AppendLine();
            count++;
        }

        return count == 0 ? text + "No static references for this block." : text.ToString();
    }

    private void ApplyRosterGroup()
    {
        CampaignRosterStart? entry = SelectedRosterStart();
        CampaignRosterFieldGroup? group = SelectedRosterGroup();
        if (entry == null || group == null || entry.IsPlayer)
        {
            return;
        }

        try
        {
            var values = new List<object>(group.Fields.Count);
            for (int i = 0; i < group.Fields.Count; i++)
            {
                CampaignRosterField field = group.Fields[i];
                string text = _rosterExtraFields[i].Text;
                if (entry.Value(field) is string)
                {
                    values.Add(text);
                }
                else if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
                    && float.IsFinite(value))
                {
                    values.Add(value);
                }
                else
                {
                    throw new FormatException($"{field.Label} must be a finite invariant-culture number.");
                }
            }

            bool changed = entry.Apply(group, values);
            BindRosterStarts();
            RebuildSelectedReader();
            UpdateExportState();
            Success(changed ? "Updated the selected roster fields." : "The selected roster fields are unchanged.");
        }
        catch (Exception error) when (error is FormatException or ArgumentException or InvalidOperationException)
        {
            _rosterGroupStatus.Text = error.Message;
        }
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

    private void BuildZeppelinPanel()
    {
        _zeppelinTab = new ScrollContainer
        {
            Name = "Zeppelins",
            Visible = false,
        };
        var panel = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _zeppelinTab.AddChild(panel);
        panel.AddChild(new Label
        {
            Text = "Mission-local zeppelins. Position is X/Y/Z in metres, Y is up, and yaw 0 faces -Z. "
                + "Only authored runtime fields are editable; absent, malformed, and unknown data stay raw.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        _zeppelinPicker = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _zeppelinPicker.ItemSelected += _ => ShowZeppelinSections();
        panel.AddChild(_zeppelinPicker);
        _zeppelinSectionPicker = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _zeppelinSectionPicker.ItemSelected += _ => ShowZeppelinRows();
        panel.AddChild(_zeppelinSectionPicker);
        _zeppelinRowPicker = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _zeppelinRowPicker.ItemSelected += _ => ShowZeppelinRow();
        panel.AddChild(_zeppelinRowPicker);
        _zeppelinGrid = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        panel.AddChild(_zeppelinGrid);
        _applyZeppelin = Button("Apply selected zeppelin fields", ApplyZeppelin);
        panel.AddChild(_applyZeppelin);
        _zeppelinReferences = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        panel.AddChild(_zeppelinReferences);
        _zeppelinStatus = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        panel.AddChild(_zeppelinStatus);
        _typedTabs.AddChild(_zeppelinTab);
    }

    private void BindZeppelins()
    {
        if (_document == null)
        {
            HideZeppelins();
            return;
        }

        int selected = Math.Max(0, _zeppelinPicker.Selected);
        _zeppelins = CampaignMissionZeppelins.Bind(_document);
        _zeppelinTab.Visible = true;
        _zeppelinPicker.Clear();
        foreach (CampaignZeppelinEntry entry in _zeppelins.Entries)
        {
            _zeppelinPicker.AddItem(entry.Label);
        }

        _zeppelinPicker.Disabled = _zeppelins.Entries.Count == 0;
        if (_zeppelins.Entries.Count == 0)
        {
            ClearZeppelinRows();
            _zeppelinSectionPicker.Clear();
            _zeppelinSectionPicker.Disabled = true;
            _zeppelinStatus.Text = _zeppelins.Problem;
            return;
        }

        selected = Math.Min(selected, _zeppelins.Entries.Count - 1);
        _zeppelinPicker.Select(selected);
        ShowZeppelinSections();
    }

    private void HideZeppelins()
    {
        _zeppelins = null;
        if (_zeppelinTab != null)
        {
            _zeppelinTab.Visible = false;
        }
    }

    private CampaignZeppelinEntry? SelectedZeppelin()
    {
        int index = _zeppelinPicker.Selected;
        return _zeppelins != null && (uint)index < (uint)_zeppelins.Entries.Count
            ? _zeppelins.Entries[index]
            : null;
    }

    private CampaignZeppelinSection? SelectedZeppelinSection()
    {
        CampaignZeppelinEntry? entry = SelectedZeppelin();
        int index = _zeppelinSectionPicker.Selected;
        return entry != null && (uint)index < (uint)entry.Sections.Count
            ? entry.Sections[index]
            : null;
    }

    private CampaignZeppelinRow? SelectedZeppelinRow()
    {
        CampaignZeppelinSection? section = SelectedZeppelinSection();
        int index = _zeppelinRowPicker.Selected;
        return section != null && (uint)index < (uint)section.Rows.Count
            ? section.Rows[index]
            : null;
    }

    private void ShowZeppelinSections()
    {
        CampaignZeppelinEntry? entry = SelectedZeppelin();
        int selected = Math.Max(0, _zeppelinSectionPicker.Selected);
        _zeppelinSectionPicker.Clear();
        if (entry == null || entry.Sections.Count == 0)
        {
            _zeppelinSectionPicker.Disabled = true;
            ClearZeppelinRows();
            _zeppelinStatus.Text = entry?.Problem ?? "No zeppelin selected.";
            return;
        }

        foreach (CampaignZeppelinSection section in entry.Sections)
        {
            _zeppelinSectionPicker.AddItem(section.Label);
        }

        _zeppelinSectionPicker.Disabled = false;
        _zeppelinSectionPicker.Select(Math.Min(selected, entry.Sections.Count - 1));
        ShowZeppelinRows();
    }

    private void ShowZeppelinRows()
    {
        CampaignZeppelinSection? section = SelectedZeppelinSection();
        int selected = Math.Max(0, _zeppelinRowPicker.Selected);
        _zeppelinRowPicker.Clear();
        if (section == null || section.Rows.Count == 0)
        {
            ClearZeppelinRows();
            _zeppelinStatus.Text = "No valid authored fields in this section. Missing and malformed values remain in the raw reader.";
            return;
        }

        foreach (CampaignZeppelinRow row in section.Rows)
        {
            _zeppelinRowPicker.AddItem(row.Label);
        }

        _zeppelinRowPicker.Disabled = false;
        _zeppelinRowPicker.Select(Math.Min(selected, section.Rows.Count - 1));
        ShowZeppelinRow();
    }

    private void ClearZeppelinRows()
    {
        _zeppelinRowPicker.Clear();
        _zeppelinRowPicker.Disabled = true;
        ClearZeppelinFields();
        _zeppelinReferences.Text = "";
        _applyZeppelin.Disabled = true;
    }

    private void ClearZeppelinFields()
    {
        foreach (Node child in _zeppelinGrid.GetChildren())
        {
            _zeppelinGrid.RemoveChild(child);
            child.QueueFree();
        }

        _zeppelinFields.Clear();
    }

    private void ShowZeppelinRow()
    {
        ClearZeppelinFields();
        CampaignZeppelinRow? row = SelectedZeppelinRow();
        if (row == null || _zeppelins == null)
        {
            _applyZeppelin.Disabled = true;
            _zeppelinReferences.Text = "";
            return;
        }

        foreach (CampaignZeppelinField field in row.Fields)
        {
            _zeppelinGrid.AddChild(new Label { Text = field.Label });
            object value = _zeppelins.Value(field);
            var edit = new LineEdit
            {
                Text = value is float number ? FormatFloat(number) : (string)value,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            _zeppelinFields.Add(edit);
            _zeppelinGrid.AddChild(edit);
        }

        _applyZeppelin.Disabled = false;
        _zeppelinReferences.Text = ZeppelinReferences(row);
        _zeppelinStatus.Text = (row.Note.Length == 0 ? "" : row.Note + " ")
            + "Apply validates this whole row before writing. "
            + "Pitch bounds are parsed but not applied by motion; broadside fire requires objective engagement.";
    }

    private string ZeppelinReferences(CampaignZeppelinRow row)
    {
        if (_zeppelins == null || _referenceDiagnostics == null)
        {
            return "References unavailable.";
        }

        var text = new StringBuilder();
        foreach (CampaignZeppelinField field in row.Fields)
        {
            foreach (CheckedReference reference in _referenceDiagnostics.Current.References)
            {
                ReferenceSource source = reference.Reference.Source;
                if (source.Scope == ReferenceScope.Mission
                    && source.FileIndex == _zeppelins.FileIndex
                    && source.Path.SequenceEqual(field.Path))
                {
                    text.Append(field.Label);
                    text.Append(": ");
                    text.AppendLine(reference.State.ToString());
                }
            }
        }

        return text.Length == 0 ? "No static references in this row." : text.ToString();
    }

    private void ApplyZeppelin()
    {
        CampaignZeppelinRow? row = SelectedZeppelinRow();
        if (row == null || _zeppelins == null)
        {
            return;
        }

        try
        {
            var values = new List<object>(row.Fields.Count);
            for (int i = 0; i < row.Fields.Count; i++)
            {
                CampaignZeppelinField field = row.Fields[i];
                string text = _zeppelinFields[i].Text;
                if (_zeppelins.Value(field) is string)
                {
                    values.Add(text);
                }
                else if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
                    && float.IsFinite(value))
                {
                    values.Add(value);
                }
                else
                {
                    throw new FormatException($"{field.Label} must be a finite invariant-culture number.");
                }
            }

            bool changed = _zeppelins.Apply(row, values);
            BindZeppelins();
            RebuildSelectedReader();
            UpdateExportState();
            Success(changed ? "Updated the selected zeppelin fields." : "The selected zeppelin fields are unchanged.");
        }
        catch (Exception error) when (error is FormatException or ArgumentException or InvalidOperationException)
        {
            _zeppelinStatus.Text = error.Message;
        }
    }

    private void BuildGeneratorPanel()
    {
        _generatorTab = new ScrollContainer
        {
            Name = "Generators",
            Visible = false,
        };
        var panel = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _generatorTab.AddChild(panel);
        panel.AddChild(new Label
        {
            Text = "Generators remain attached to their chapter-world hosts. "
                + "Only existing runtime-backed fields are editable; unknown and absent data stay raw.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        _generatorPicker = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _generatorPicker.ItemSelected += _ => ShowGeneratorSections();
        panel.AddChild(_generatorPicker);
        _generatorContext = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        panel.AddChild(_generatorContext);
        _generatorSectionPicker = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _generatorSectionPicker.ItemSelected += _ => ShowGeneratorRows();
        panel.AddChild(_generatorSectionPicker);
        _generatorRowPicker = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _generatorRowPicker.ItemSelected += _ => ShowGeneratorRow();
        panel.AddChild(_generatorRowPicker);
        _generatorGrid = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        panel.AddChild(_generatorGrid);
        _applyGenerator = Button("Apply selected generator fields", ApplyGenerator);
        panel.AddChild(_applyGenerator);
        _generatorReferences = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        panel.AddChild(_generatorReferences);
        _generatorStatus = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        panel.AddChild(_generatorStatus);
        _typedTabs.AddChild(_generatorTab);
    }

    private void BindGenerators()
    {
        if (_document == null)
        {
            HideGenerators();
            return;
        }

        int selected = Math.Max(0, _generatorPicker.Selected);
        _generators = CampaignMissionGenerators.Bind(_document);
        _generatorTab.Visible = true;
        _generatorPicker.Clear();
        foreach (CampaignGeneratorEntry entry in _generators.Entries)
        {
            _generatorPicker.AddItem(entry.Label);
        }

        _generatorPicker.Disabled = _generators.Entries.Count == 0;
        if (_generators.Entries.Count == 0)
        {
            ClearGeneratorFields();
            _generatorSectionPicker.Clear();
            _generatorSectionPicker.Disabled = true;
            _generatorRowPicker.Clear();
            _generatorRowPicker.Disabled = true;
            _generatorContext.Text = "";
            _generatorReferences.Text = "";
            _generatorStatus.Text = _generators.Problem;
            _applyGenerator.Disabled = true;
            return;
        }

        _generatorPicker.Select(Math.Min(selected, _generators.Entries.Count - 1));
        ShowGeneratorSections();
    }

    private void HideGenerators()
    {
        _generators = null;
        if (_generatorTab != null)
        {
            _generatorTab.Visible = false;
        }
    }

    private CampaignGeneratorEntry? SelectedGenerator()
    {
        int index = _generatorPicker.Selected;
        return _generators != null && (uint)index < (uint)_generators.Entries.Count
            ? _generators.Entries[index]
            : null;
    }

    private CampaignGeneratorSection? SelectedGeneratorSection()
    {
        CampaignGeneratorEntry? entry = SelectedGenerator();
        int index = _generatorSectionPicker.Selected;
        return entry != null && (uint)index < (uint)entry.Sections.Count
            ? entry.Sections[index]
            : null;
    }

    private CampaignGeneratorRow? SelectedGeneratorRow()
    {
        CampaignGeneratorSection? section = SelectedGeneratorSection();
        int index = _generatorRowPicker.Selected;
        return section != null && (uint)index < (uint)section.Rows.Count
            ? section.Rows[index]
            : null;
    }

    private void ShowGeneratorSections()
    {
        CampaignGeneratorEntry? entry = SelectedGenerator();
        int selected = Math.Max(0, _generatorSectionPicker.Selected);
        _generatorSectionPicker.Clear();
        _generatorContext.Text = entry == null ? "" : GeneratorContext(entry);
        _generatorReferences.Text = entry == null ? "" : GeneratorReferences(entry);
        if (entry == null || entry.Sections.Count == 0)
        {
            _generatorSectionPicker.Disabled = true;
            _generatorRowPicker.Clear();
            _generatorRowPicker.Disabled = true;
            ClearGeneratorFields();
            _applyGenerator.Disabled = true;
            _generatorStatus.Text = entry?.Problem ?? "No generator selected.";
            return;
        }

        foreach (CampaignGeneratorSection section in entry.Sections)
        {
            _generatorSectionPicker.AddItem(section.Label);
        }

        _generatorSectionPicker.Disabled = false;
        _generatorSectionPicker.Select(Math.Min(selected, entry.Sections.Count - 1));
        ShowGeneratorRows();
    }

    private void ShowGeneratorRows()
    {
        CampaignGeneratorSection? section = SelectedGeneratorSection();
        int selected = Math.Max(0, _generatorRowPicker.Selected);
        _generatorRowPicker.Clear();
        if (section == null || section.Rows.Count == 0)
        {
            _generatorRowPicker.Disabled = true;
            ClearGeneratorFields();
            _applyGenerator.Disabled = true;
            _generatorStatus.Text = "No valid authored fields in this section. Missing and malformed values remain raw.";
            return;
        }

        foreach (CampaignGeneratorRow row in section.Rows)
        {
            _generatorRowPicker.AddItem(row.Label);
        }

        _generatorRowPicker.Disabled = false;
        _generatorRowPicker.Select(Math.Min(selected, section.Rows.Count - 1));
        ShowGeneratorRow();
    }

    private void ClearGeneratorFields()
    {
        foreach (Node child in _generatorGrid.GetChildren())
        {
            _generatorGrid.RemoveChild(child);
            child.QueueFree();
        }

        _generatorFields.Clear();
    }

    private void ShowGeneratorRow()
    {
        ClearGeneratorFields();
        CampaignGeneratorRow? row = SelectedGeneratorRow();
        if (row == null || _generators == null)
        {
            _applyGenerator.Disabled = true;
            return;
        }

        foreach (CampaignGeneratorField field in row.Fields)
        {
            _generatorGrid.AddChild(new Label { Text = field.Label });
            object value = _generators.Value(field);
            var edit = new LineEdit
            {
                Text = value is float number ? FormatFloat(number) : (string)value,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            _generatorFields.Add(edit);
            _generatorGrid.AddChild(edit);
        }

        _applyGenerator.Disabled = false;
        _generatorStatus.Text = row.Note + " Apply validates the entire row before writing.";
    }

    private string GeneratorContext(CampaignGeneratorEntry entry)
    {
        if (entry.Problem.Length != 0)
        {
            return entry.Problem;
        }

        string host = $"Host: '{entry.Host}' (chapter-world node, read-only). ";
        string choice = $"Net selection: '{entry.ChooseNets ?? "default"}' (CSVM currently cycles for both modes).";
        if (!entry.IsZeppelin)
        {
            string first = EnemyGenerators.LaunchPathNode(entry.Host, 0);
            string second = EnemyGenerators.LaunchPathNode(entry.Host, 1);
            return host + $"Surface path: '{first}' and '{second}' under the host; "
                + $"moving_path {(entry.MovingPath ? "present" : "absent")}. " + choice;
        }

        GameZ? world = _dependencyBundle?.World.Availability.State == CampaignDependencyState.Ready
            ? _dependencyBundle.World.Value
            : null;
        CampaignGeneratorOrigin origin = CampaignGeneratorWorld.OriginOffset(world, entry.Host, entry.Origin);
        string offset = origin.Offset is Vector3 position
            ? $"X {FormatFloat(position.X)}, Y {FormatFloat(position.Y)}, Z {FormatFloat(position.Z)} m"
            : "unavailable";
        return host + $"Origin '{entry.Origin ?? "(host fallback)"}', host-relative offset: {offset}. "
            + origin.Detail + " " + choice;
    }

    private string GeneratorReferences(CampaignGeneratorEntry entry)
    {
        if (_generators == null || _referenceDiagnostics == null)
        {
            return "References unavailable.";
        }

        var text = new StringBuilder("Generator and objective references:\n");
        int count = 0;
        foreach (CheckedReference checkedReference in _referenceDiagnostics.Current.References)
        {
            MissionReference reference = checkedReference.Reference;
            ReferenceSource source = reference.Source;
            bool own = source.Scope == ReferenceScope.Mission
                && source.FileIndex == _generators.FileIndex
                && source.Path.Length >= 2
                && source.Path[0] == 0
                && source.Path[1] == entry.RawIndex;
            bool objective = reference.Kind == ReferenceKind.Generator
                && string.Equals(reference.Value.Name, entry.Host, StringComparison.OrdinalIgnoreCase);
            if (!own && !objective)
            {
                continue;
            }

            text.Append(source.Context ?? reference.Kind.ToString());
            text.Append(source.Derived ? " (derived): " : ": ");
            text.Append(ReferenceDisplay(reference.Value));
            text.Append(" - ");
            text.AppendLine(checkedReference.State.ToString());
            count++;
        }

        return count == 0 ? "No static generator references are available." : text.ToString();
    }

    private void ApplyGenerator()
    {
        CampaignGeneratorRow? row = SelectedGeneratorRow();
        if (row == null || _generators == null)
        {
            return;
        }

        try
        {
            var values = new List<object>(row.Fields.Count);
            for (int i = 0; i < row.Fields.Count; i++)
            {
                CampaignGeneratorField field = row.Fields[i];
                string text = _generatorFields[i].Text;
                if (_generators.Value(field) is string)
                {
                    values.Add(text);
                }
                else if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
                    && float.IsFinite(value))
                {
                    values.Add(value);
                }
                else
                {
                    throw new FormatException($"{field.Label} must be a finite invariant-culture number.");
                }
            }

            bool changed = _generators.Apply(row, values);
            BindGenerators();
            RebuildSelectedReader();
            UpdateExportState();
            Success(changed ? "Updated the selected generator fields." : "The selected generator fields are unchanged.");
        }
        catch (Exception error) when (error is FormatException or ArgumentException or InvalidOperationException)
        {
            _generatorStatus.Text = error.Message;
        }
    }

    private void BuildObjectivePanel()
    {
        _objectiveTab = new ScrollContainer { Name = "Objectives", Visible = false };
        var panel = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _objectiveTab.AddChild(panel);
        panel.AddChild(new Label
        {
            Text = "Graph numbers select contiguous runtime blocks; display priority is separate. "
                + "The list preserves directive order and duplicates. Unsupported data remain in Raw; "
                + "numbered block keys can only be changed by a validated swap.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        panel.AddChild(Button("Show objectives reader in Raw", ShowObjectivesRaw));
        _objectivePicker = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _objectivePicker.ItemSelected += _ => ShowObjectiveDirectives();
        panel.AddChild(_objectivePicker);
        _objectiveGraph = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        panel.AddChild(_objectiveGraph);
        _objectiveSwapPicker = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _objectiveSwapPicker.ItemSelected += _ => UpdateObjectiveSwap();
        panel.AddChild(_objectiveSwapPicker);
        _swapObjective = Button("Swap selected objective numbers", ConfirmObjectiveSwap);
        panel.AddChild(_swapObjective);
        _objectiveDirectivePicker = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _objectiveDirectivePicker.ItemSelected += _ => ShowObjectiveRows();
        panel.AddChild(_objectiveDirectivePicker);
        _objectiveRowPicker = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _objectiveRowPicker.ItemSelected += _ => ShowObjectiveRow();
        panel.AddChild(_objectiveRowPicker);
        _objectiveGrid = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        panel.AddChild(_objectiveGrid);
        _applyObjective = Button("Apply selected directive fields", ApplyObjective);
        panel.AddChild(_applyObjective);
        _objectiveReferences = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        panel.AddChild(_objectiveReferences);
        _objectiveStatus = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        panel.AddChild(_objectiveStatus);
        _typedTabs.AddChild(_objectiveTab);
        _objectiveSwapDialog = new ConfirmationDialog { Title = "Swap objective numbers" };
        _objectiveSwapDialog.Confirmed += SwapObjectiveNumbers;
        AddChild(_objectiveSwapDialog);
    }

    private void BindObjectives()
    {
        if (_document == null)
        {
            HideObjectives();
            return;
        }

        int selected = Math.Max(0, _objectivePicker.Selected);
        int swap = Math.Max(0, _objectiveSwapPicker.Selected);
        _objectives = CampaignMissionObjectives.Bind(_document);
        _objectiveTab.Visible = true;
        _objectivePicker.Clear();
        _objectiveSwapPicker.Clear();
        foreach (CampaignObjectiveEntry entry in _objectives.Entries)
        {
            _objectivePicker.AddItem($"OBJECTIVE{entry.Number}");
            _objectiveSwapPicker.AddItem($"Swap with OBJECTIVE{entry.Number}");
        }

        bool empty = _objectives.Entries.Count == 0;
        _objectivePicker.Disabled = empty;
        _objectiveSwapPicker.Disabled = empty;
        if (empty)
        {
            _objectiveDirectivePicker.Clear();
            _objectiveDirectivePicker.Disabled = true;
            _objectiveRowPicker.Clear();
            _objectiveRowPicker.Disabled = true;
            ClearObjectiveFields();
            _objectiveGraph.Text = "";
            _objectiveReferences.Text = "";
            _objectiveStatus.Text = _objectives.Problem;
            _applyObjective.Disabled = true;
            _swapObjective.Disabled = true;
            return;
        }

        _objectivePicker.Select(Math.Min(selected, _objectives.Entries.Count - 1));
        _objectiveSwapPicker.Select(Math.Min(swap, _objectives.Entries.Count - 1));
        ShowObjectiveDirectives();
    }

    private void HideObjectives()
    {
        _objectives = null;
        if (_objectiveTab != null)
        {
            _objectiveTab.Visible = false;
        }
    }

    private CampaignObjectiveEntry? SelectedObjective()
    {
        int index = _objectivePicker.Selected;
        return _objectives != null && (uint)index < (uint)_objectives.Entries.Count
            ? _objectives.Entries[index]
            : null;
    }

    private CampaignObjectiveDirective? SelectedObjectiveDirective()
    {
        CampaignObjectiveEntry? entry = SelectedObjective();
        int index = _objectiveDirectivePicker.Selected;
        return entry != null && (uint)index < (uint)entry.Directives.Count
            ? entry.Directives[index]
            : null;
    }

    private CampaignObjectiveRow? SelectedObjectiveRow()
    {
        CampaignObjectiveDirective? directive = SelectedObjectiveDirective();
        int index = _objectiveRowPicker.Selected;
        return directive != null && (uint)index < (uint)directive.Rows.Count
            ? directive.Rows[index]
            : null;
    }

    private void ShowObjectiveDirectives()
    {
        CampaignObjectiveEntry? entry = SelectedObjective();
        int selected = Math.Max(0, _objectiveDirectivePicker.Selected);
        _objectiveDirectivePicker.Clear();
        _objectiveGraph.Text = entry == null
            ? ""
            : $"Graph edges: {entry.Graph}\nShadowed blocks: {entry.DuplicateBlocks}. "
                + "Swapping numbers can change the runtime completion order.";
        _objectiveReferences.Text = entry == null ? "" : ObjectiveReferences(entry);
        UpdateObjectiveSwap();
        if (entry == null || entry.Directives.Count == 0)
        {
            _objectiveDirectivePicker.Disabled = true;
            _objectiveRowPicker.Clear();
            _objectiveRowPicker.Disabled = true;
            ClearObjectiveFields();
            _applyObjective.Disabled = true;
            _objectiveStatus.Text = "The selected block is empty or bare. Its original data remain in Raw.";
            return;
        }

        foreach (CampaignObjectiveDirective directive in entry.Directives)
        {
            _objectiveDirectivePicker.AddItem(
                $"[{directive.RawIndex}] {directive.Key}{(directive.Selected ? "" : " (shadowed)")}");
        }

        _objectiveDirectivePicker.Disabled = false;
        _objectiveDirectivePicker.Select(Math.Min(selected, entry.Directives.Count - 1));
        ShowObjectiveRows();
    }

    private void ShowObjectiveRows()
    {
        CampaignObjectiveDirective? directive = SelectedObjectiveDirective();
        int selected = Math.Max(0, _objectiveRowPicker.Selected);
        _objectiveRowPicker.Clear();
        if (directive == null || directive.Rows.Count == 0)
        {
            _objectiveRowPicker.Disabled = true;
            ClearObjectiveFields();
            _applyObjective.Disabled = true;
            _objectiveStatus.Text = directive?.Note ?? "No directive selected.";
            return;
        }

        foreach (CampaignObjectiveRow row in directive.Rows)
        {
            _objectiveRowPicker.AddItem(row.Label);
        }

        _objectiveRowPicker.Disabled = false;
        _objectiveRowPicker.Select(Math.Min(selected, directive.Rows.Count - 1));
        ShowObjectiveRow();
    }

    private void ClearObjectiveFields()
    {
        foreach (Node child in _objectiveGrid.GetChildren())
        {
            _objectiveGrid.RemoveChild(child);
            child.QueueFree();
        }

        _objectiveFields.Clear();
    }

    private void ShowObjectiveRow()
    {
        ClearObjectiveFields();
        CampaignObjectiveRow? row = SelectedObjectiveRow();
        if (row == null || _objectives == null)
        {
            _applyObjective.Disabled = true;
            return;
        }

        foreach (CampaignObjectiveField field in row.Fields)
        {
            _objectiveGrid.AddChild(new Label { Text = field.Label });
            object value = _objectives.Value(field);
            var edit = new LineEdit
            {
                Text = value is float number ? FormatFloat(number) : (string)value,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            _objectiveFields.Add(edit);
            _objectiveGrid.AddChild(edit);
        }

        _applyObjective.Disabled = false;
        _objectiveStatus.Text = row.Note + " The entire row is validated before any leaf is written.";
    }

    private string ObjectiveReferences(CampaignObjectiveEntry entry)
    {
        if (_objectives == null || _referenceDiagnostics == null)
        {
            return "References unavailable.";
        }

        var text = new StringBuilder("Objective references:\n");
        int count = 0;
        foreach (CheckedReference checkedReference in _referenceDiagnostics.Current.References)
        {
            MissionReference reference = checkedReference.Reference;
            ReferenceSource source = reference.Source;
            if (source.Scope != ReferenceScope.Mission || source.FileIndex != _objectives.FileIndex
                || source.Path.Length < 2 || source.Path[0] != 0 || source.Path[1] != entry.RawIndex + 1)
            {
                continue;
            }

            if (count++ == 40)
            {
                text.AppendLine("More references are available in Diagnostics.");
                break;
            }

            text.Append(reference.Kind);
            text.Append(' ');
            text.Append(ReferenceDisplay(reference.Value));
            text.Append(": ");
            text.Append(checkedReference.State);
            if (!string.IsNullOrWhiteSpace(checkedReference.Detail))
            {
                text.Append(" - ");
                text.Append(checkedReference.Detail);
            }

            text.AppendLine();
        }

        return count == 0 ? "No static references in this objective." : text.ToString();
    }

    private void ApplyObjective()
    {
        CampaignObjectiveRow? row = SelectedObjectiveRow();
        if (row == null || _objectives == null)
        {
            return;
        }

        try
        {
            var values = new List<object>(row.Fields.Count);
            for (int i = 0; i < row.Fields.Count; i++)
            {
                CampaignObjectiveField field = row.Fields[i];
                string text = _objectiveFields[i].Text;
                if (_objectives.Value(field) is string)
                {
                    values.Add(text);
                }
                else if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
                    && float.IsFinite(value))
                {
                    values.Add(value);
                }
                else
                {
                    throw new FormatException($"{field.Label} must be a finite invariant-culture number.");
                }
            }

            bool changed = _objectives.Apply(row, values);
            BindObjectives();
            RebuildSelectedReader();
            UpdateExportState();
            Success(changed ? "Updated the selected objective fields." : "The selected objective fields are unchanged.");
        }
        catch (Exception error) when (error is FormatException or ArgumentException or InvalidOperationException)
        {
            _objectiveStatus.Text = error.Message;
        }
    }

    private void UpdateObjectiveSwap()
    {
        _swapObjective.Disabled = SelectedObjective() == null
            || _objectiveSwapPicker.Selected < 0
            || _objectivePicker.Selected == _objectiveSwapPicker.Selected;
    }

    private void ConfirmObjectiveSwap()
    {
        CampaignObjectiveEntry? first = SelectedObjective();
        if (first == null || _objectives == null
            || (uint)_objectiveSwapPicker.Selected >= (uint)_objectives.Entries.Count)
        {
            return;
        }

        int second = _objectives.Entries[_objectiveSwapPicker.Selected].Number;
        _objectiveSwapDialog.DialogText =
            $"Swap OBJECTIVE{first.Number} and OBJECTIVE{second}, including known objective-number links? "
            + "Unknown directives, gaps or malformed links prevent this operation. "
            + "The graph's rotating completion order can change, and external consumers remain unverified.";
        _objectiveSwapDialog.PopupCentered();
    }

    private void SwapObjectiveNumbers()
    {
        CampaignObjectiveEntry? first = SelectedObjective();
        if (first == null || _objectives == null
            || (uint)_objectiveSwapPicker.Selected >= (uint)_objectives.Entries.Count)
        {
            return;
        }

        int second = _objectives.Entries[_objectiveSwapPicker.Selected].Number;
        try
        {
            bool changed = _objectives.SwapNumbers(first.Number, second);
            BindObjectives();
            _objectivePicker.Select(second - 1);
            ShowObjectiveDirectives();
            RebuildSelectedReader();
            UpdateExportState();
            Success(changed ? "Swapped objective numbers and links." : "Objective numbers are unchanged.");
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            _objectiveStatus.Text = error.Message;
        }
    }

    private void ShowObjectivesRaw()
    {
        if (_objectives == null || _objectives.FileIndex < 0)
        {
            return;
        }

        _readerPicker.Select(_objectives.FileIndex);
        RebuildReader(_objectives.FileIndex);
    }

    private void BuildAnimationPanel()
    {
        _animationTab = new ScrollContainer { Name = "Animation and Readers", Visible = false };
        var panel = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _animationTab.AddChild(panel);
        panel.AddChild(new Label
        {
            Text = "NEW_GAME_START runs in order after ON_STARTUP. Duplicate names are dispatched again and may reset; "
                + "a missing name is logged and skipped. Compiled definitions win collisions.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        var rawButtons = new HBoxContainer();
        rawButtons.AddChild(Button("Start Raw", () => ShowAnimationReader("startanims.json")));
        rawButtons.AddChild(Button("Map Raw", () => ShowAnimationReader("map.json")));
        rawButtons.AddChild(Button("Net Raw", () => ShowAnimationReader("net.json")));
        rawButtons.AddChild(Button("Index Raw", () => ShowAnimationReader("mis_anim.json")));
        panel.AddChild(rawButtons);
        _startAnimationPicker = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _startAnimationPicker.ItemSelected += _ => ShowStartAnimation();
        panel.AddChild(_startAnimationPicker);
        _startAnimationName = new LineEdit
        {
            PlaceholderText = "Existing animation name",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _startAnimationName.TextSubmitted += _ => RenameStartAnimation();
        panel.AddChild(_startAnimationName);
        var actions = new HBoxContainer();
        _applyStartAnimation = Button("Rename", RenameStartAnimation);
        _startAnimationUp = Button("Move up", () => MoveStartAnimation(-1));
        _startAnimationDown = Button("Move down", () => MoveStartAnimation(1));
        actions.AddChild(_applyStartAnimation);
        actions.AddChild(_startAnimationUp);
        actions.AddChild(_startAnimationDown);
        panel.AddChild(actions);
        _startAnimationReference = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _startAnimationStatus = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _readerSummaries = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        panel.AddChild(_startAnimationReference);
        panel.AddChild(_startAnimationStatus);
        panel.AddChild(new HSeparator());
        panel.AddChild(_readerSummaries);
        _typedTabs.AddChild(_animationTab);
    }

    private void BindStartAnimations(int? preferredIndex = null)
    {
        if (_document == null || _dependencyBundle == null || _referenceDiagnostics == null)
        {
            HideStartAnimations();
            return;
        }

        int selected = preferredIndex ?? Math.Max(0, _startAnimationPicker.Selected);
        _startAnimations = CampaignMissionStartAnimations.Bind(_document);
        _animationTab.Visible = true;
        _startAnimationPicker.Clear();
        for (int i = 0; i < _startAnimations.Entries.Length; i++)
        {
            string name = _startAnimations.Entries[i].Name;
            _startAnimationPicker.AddItem($"{i + 1}: {(name.Length == 0 ? "(empty)" : name)}");
        }

        _startAnimationPicker.Disabled = _startAnimations.Entries.IsEmpty;
        if (!_startAnimations.Entries.IsEmpty)
        {
            _startAnimationPicker.Select(Math.Clamp(selected, 0, _startAnimations.Entries.Length - 1));
        }

        _startAnimationStatus.Text = _startAnimations.Problem;
        _readerSummaries.Text = FormatReaderSummaries(CampaignMissionReaderSummaries.Read(
            _document, _dependencyBundle, _referenceDiagnostics.AnimationInspection));
        ShowStartAnimation();
    }

    private void HideStartAnimations()
    {
        _startAnimations = null;
        if (_animationTab != null)
        {
            _animationTab.Visible = false;
        }
    }

    private void ShowStartAnimation()
    {
        int index = _startAnimationPicker.Selected;
        bool selected = _startAnimations != null && (uint)index < (uint)_startAnimations.Entries.Length;
        _startAnimationName.Editable = selected;
        _applyStartAnimation.Disabled = !selected;
        _startAnimationUp.Disabled = !selected || index == 0;
        _startAnimationDown.Disabled = !selected || index == _startAnimations!.Entries.Length - 1;
        if (!selected)
        {
            _startAnimationName.Text = "";
            _startAnimationReference.Text = "";
            return;
        }

        CampaignMissionStartAnimations animations = _startAnimations!;
        CampaignStartAnimationEntry entry = animations.Entries[index];
        _startAnimationName.Text = entry.Name;
        CheckedReference? verdict = _referenceDiagnostics?.Current.References.FirstOrDefault(reference =>
            reference.Reference.Rule == ReferenceRule.StartAnimation
            && reference.Reference.Source.FileIndex == animations.FileIndex
            && reference.Reference.Source.Path.SequenceEqual(entry.NamePath));
        _startAnimationReference.Text = verdict == null
            ? "Reference check is unavailable."
            : $"{verdict.State}: {verdict.Detail ?? "Definition is available."}";
    }

    private void RenameStartAnimation()
    {
        if (_startAnimations == null || _startAnimationPicker.Selected < 0)
        {
            return;
        }

        int index = _startAnimationPicker.Selected;
        try
        {
            bool changed = _startAnimations.Rename(index, _startAnimationName.Text);
            BindStartAnimations(index);
            RebuildSelectedReader();
            UpdateExportState();
            Success(changed ? "Renamed the selected start animation." : "The start-animation name is unchanged.");
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            _startAnimationStatus.Text = error.Message;
        }
    }

    private void MoveStartAnimation(int direction)
    {
        if (_startAnimations == null || _startAnimationPicker.Selected < 0)
        {
            return;
        }

        int from = _startAnimationPicker.Selected;
        int to = from + direction;
        try
        {
            bool changed = _startAnimations.Move(from, to);
            BindStartAnimations(to);
            RebuildSelectedReader();
            UpdateExportState();
            Success(changed ? "Moved the selected start-animation entry." : "The start-animation order is unchanged.");
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            _startAnimationStatus.Text = error.Message;
        }
    }

    private void ShowAnimationReader(string name)
    {
        int index = _document?.FindFileIndex(name) ?? -1;
        if (index < 0)
        {
            _startAnimationStatus.Text = $"The mission has no {name} reader.";
            return;
        }

        _readerPicker.Select(index);
        RebuildReader(index);
    }

    private string FormatReaderSummaries(CampaignMissionReaderSummaries summaries)
    {
        var text = new StringBuilder();
        text.AppendLine($"map.zrd: {ReaderShape(summaries.Map)}");
        text.AppendLine("No map-specific CSVM briefing or campaign-flight consumer is established; "
            + "animation definitions in any reader can still be scanned.");
        text.AppendLine($"net.zrd: {ReaderShape(summaries.Net.Reader)}");
        if (summaries.Net.Problem is string netProblem)
        {
            text.AppendLine(netProblem);
        }

        text.AppendLine($"{summaries.Net.Points.Length} multiplayer spawn(s), {summaries.Net.IgnoredRows} ignored row(s). "
            + "The numbered values follow the accepted table order, not raw row indexes. "
            + "This table does not place the campaign player or define chapter AI nets.");
        for (int i = 0; i < Math.Min(16, summaries.Net.Points.Length); i++)
        {
            SpawnPoint point = summaries.Net.Points[i];
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"  {i + 1}: ({point.Position.X:R}, {point.Position.Y:R}, {point.Position.Z:R}), yaw {point.HeadingDeg:R} degrees"));
        }

        if (summaries.Net.Points.Length > 16)
        {
            text.AppendLine("  Additional multiplayer spawns are in Raw.");
        }

        CampaignAnimationSummary animations = summaries.Animations;
        text.AppendLine($"mis_anim.zrd reader index: {ReaderShape(animations.Index)}");
        foreach (string path in animations.ListedPaths.Take(30))
        {
            text.AppendLine($"  {path}");
        }

        if (animations.ListedPaths.Length > 30)
        {
            text.AppendLine("  Additional listed paths are in Raw.");
        }

        text.AppendLine($"Compiled archives: chapter {animations.ChapterArchive}, mission {animations.MissionArchive}. "
            + "They are read-only and distinct from reader indexes.");
        if (animations.Inspection.Program is AnimProgram program)
        {
            text.AppendLine($"Effective program: {program.CompiledCount} compiled and {program.ReaderCount} reader definitions, "
                + $"{program.StartAnims.Count} start entries, {program.ScriptPoolCount} compiled scripts.");
            text.AppendLine("Reader-sourced winning definitions (animation / anchor / source):");
            foreach (AnimDefinition definition in program.Defs.Where(def => def.Archive == null).Take(20))
            {
                text.AppendLine($"  {definition.AnimName ?? "(unnamed)"} / {definition.Name} / {definition.SourceFile}");
            }

            if (program.ReaderCount > 20)
            {
                text.AppendLine("  Additional reader definitions are not shown here.");
            }
        }
        else
        {
            text.AppendLine($"Effective program unverified: {animations.Inspection.UnavailableReason}");
        }

        text.Append("Listed reader paths do not prove a compiled or live-bound animation exists.");
        return text.ToString();
    }

    private string ReaderShape(CampaignReaderShape shape) =>
        !shape.Present
            ? "absent"
            : shape.FirstListCount is int count
                ? $"{shape.RootCount} root item(s), first list has {count} item(s)"
                : $"{shape.RootCount} root item(s), first is {shape.FirstKind}";

    private void BuildDangerPanel()
    {
        _dangerTab = new ScrollContainer { Name = "Danger Zones", Visible = false };
        var panel = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _dangerTab.AddChild(panel);
        _dangerScope = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        panel.AddChild(_dangerScope);
        _createDanger = Button("Create empty mission danger-zone reader", CreateDangerReader);
        panel.AddChild(_createDanger);
        _dangerPicker = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _dangerPicker.ItemSelected += _ => ShowDangerZone();
        panel.AddChild(_dangerPicker);
        _dangerDetails = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        panel.AddChild(_dangerDetails);
        panel.AddChild(new Label
        {
            Text = "Chapter ribbon, top-down X/Z, before mission setup (read-only; not a geometry editor)",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        _dangerPreview = new CampaignDangerRibbonPreview();
        panel.AddChild(_dangerPreview);
        _dangerEnabled = new CheckBox { Text = "Enable this named zone" };
        _dangerSnapshot = new CheckBox { Text = "Allow snapshot on completion" };
        panel.AddChild(_dangerEnabled);
        panel.AddChild(_dangerSnapshot);
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = "Scrapbook/completion slot" });
        _dangerSlot = new LineEdit
        {
            PlaceholderText = "Absent (-1 at runtime)",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        row.AddChild(_dangerSlot);
        panel.AddChild(row);
        _applyDanger = Button("Apply mission overrides", ApplyDangerZone);
        panel.AddChild(_applyDanger);
        _dangerReferences = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        panel.AddChild(_dangerReferences);
        _dangerStatus = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        panel.AddChild(_dangerStatus);
        _typedTabs.AddChild(_dangerTab);
    }

    private void BindDangerZones()
    {
        if (_document == null || _dependencyBundle == null || _dangerWorld == null)
        {
            HideDangerZones();
            return;
        }

        string? selectedName = _dangerPicker.Selected >= 0
            ? _dangerPicker.GetItemText(_dangerPicker.Selected)
            : null;
        _dangerZones = CampaignMissionDangerZones.Bind(_document, _dangerWorld);
        _dangerTab.Visible = true;
        bool chapterHasReader = _dependencyBundle.ChapterReaders.Availability.State == CampaignDependencyState.Ready
            && _dependencyBundle.ChapterReaders.Value.HasFile("dzones.json");
        _dangerScope.Text = _dangerZones.FileIndex >= 0
            ? "Mission-local dzones.zrd. Only this mission's exported reader is edited."
            : "No mission dzones.zrd. The runtime uses no overrides, not a chapter fallback.";
        if (chapterHasReader)
        {
            _dangerScope.Text += " A chapter reader exists but is not consumed here.";
        }

        _createDanger.Visible = _dangerZones.CanCreate;
        _createDanger.Disabled = !_dangerZones.CanCreate;
        _dangerPicker.Clear();
        int selected = 0;
        for (int i = 0; i < _dangerZones.Entries.Length; i++)
        {
            CampaignDangerZoneEntry entry = _dangerZones.Entries[i];
            _dangerPicker.AddItem(entry.Name);
            if (string.Equals(selectedName, entry.Name, StringComparison.OrdinalIgnoreCase))
            {
                selected = i;
            }
        }

        _dangerPicker.Disabled = _dangerZones.Entries.IsEmpty;
        if (!_dangerZones.Entries.IsEmpty)
        {
            _dangerPicker.Select(selected);
        }

        ShowDangerZone();
    }

    private void HideDangerZones()
    {
        _dangerZones = null;
        if (_dangerTab != null)
        {
            _dangerTab.Visible = false;
        }
    }

    private CampaignDangerZoneEntry? SelectedDangerZone()
    {
        int index = _dangerPicker.Selected;
        return _dangerZones != null && (uint)index < (uint)_dangerZones.Entries.Length
            ? _dangerZones.Entries[index]
            : null;
    }

    private void ShowDangerZone()
    {
        CampaignDangerZoneEntry? entry = SelectedDangerZone();
        bool editable = entry != null && _dangerZones?.CanEdit == true;
        _dangerEnabled.Disabled = !editable;
        _dangerSnapshot.Disabled = !editable;
        _dangerSlot.Editable = editable;
        _applyDanger.Disabled = !editable;
        _dangerPreview.ShowRibbon(entry?.Ribbon);
        _dangerReferences.Text = "";
        _dangerStatus.Text = "";
        if (entry == null)
        {
            _dangerDetails.Text = _dangerWorld is { Problem.Length: > 0 } dangerWorld
                ? dangerWorld.Problem
                : "No chapter path, selected objective, or mission override names a danger zone.";
            _dangerEnabled.ButtonPressed = false;
            _dangerSnapshot.ButtonPressed = false;
            _dangerSlot.Text = "";
            return;
        }

        _dangerEnabled.ButtonPressed = entry.State.Enabled;
        _dangerSnapshot.ButtonPressed = entry.State.SnapshotAllowed;
        _dangerSlot.Text = entry.State.ObjectiveSlot is float slot ? FormatFloat(slot) : "";
        string objectives = entry.ObjectiveBlocks.IsEmpty
            ? "No selected objective names this zone; its player gates cannot arm."
            : "Selected objective blocks: " + string.Join(", ", entry.ObjectiveBlocks.Select(
                number => $"OBJECTIVE{number.ToString(CultureInfo.InvariantCulture)}")) + ".";
        if (!entry.State.Enabled && !entry.ObjectiveBlocks.IsEmpty)
        {
            objectives += " This zone cannot complete while disabled; review each objective's required count.";
        }

        string gates = entry.HasGates switch
        {
            true => "The first matching chapter node has usable player gates.",
            false => "The first matching chapter node has no usable player gates.",
            _ => "Chapter gate geometry is unavailable or could not be inspected.",
        };
        string ribbon = entry.Ribbon == null
            ? "No readable AI ribbon is available."
            : $"AI ribbon '{entry.Ribbon.Name}', difficulty {entry.Ribbon.Difficulty}, "
                + $"{entry.Ribbon.Lanes} lane(s), {entry.Ribbon.Points.Length} samples.";
        if (entry.Ribbon != null
            && !string.Equals(entry.Name, entry.Ribbon.Name, StringComparison.OrdinalIgnoreCase))
        {
            ribbon += " This name does not disable the differently named selected AI ribbon.";
        }

        _dangerDetails.Text = $"{objectives} {gates} {ribbon} {_dangerWorld?.Problem}";
        _dangerReferences.Text = DangerReferences(entry);
        _dangerStatus.Text = "A photo also requires an armed player zone, a nonnegative slot, "
            + "a profile directory, and an available camera. Use -1 to disable an existing slot without removing its raw pair. "
            + "The snapshot commit sweep covers 10-31; debrief rows cover 18-30.";
    }

    private string DangerReferences(CampaignDangerZoneEntry entry)
    {
        if (_referenceDiagnostics == null)
        {
            return "References unavailable.";
        }

        var text = new StringBuilder("Danger-zone references:\n");
        int count = 0;
        foreach (CheckedReference checkedReference in _referenceDiagnostics.Current.References)
        {
            MissionReference reference = checkedReference.Reference;
            if (reference.Kind != ReferenceKind.DangerZone
                || !(string.Equals(reference.Value.Name, entry.Name, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(reference.Context.RelatedName, entry.Name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            text.Append(reference.Source.Context ?? "danger zone");
            text.Append(": ");
            text.Append(checkedReference.State);
            if (!string.IsNullOrEmpty(checkedReference.Detail))
            {
                text.Append(" - ");
                text.Append(checkedReference.Detail);
            }

            text.AppendLine();
            count++;
        }

        return count == 0 ? "No mission gate or slot references." : text.ToString();
    }

    private void CreateDangerReader()
    {
        if (_dangerZones == null || _document == null)
        {
            return;
        }

        try
        {
            int index = _dangerZones.CreateMissionReader();
            _readerPicker.AddItem(_document.Files[index].Key);
            _readerPicker.Disabled = false;
            _readerPicker.Select(index);
            BindDangerZones();
            RebuildReader(index);
            UpdateExportState();
            Success("Created an empty reader in this mission only.");
        }
        catch (InvalidOperationException error)
        {
            _dangerStatus.Text = error.Message;
        }
    }

    private void ApplyDangerZone()
    {
        CampaignDangerZoneEntry? entry = SelectedDangerZone();
        if (entry == null || _dangerZones == null)
        {
            return;
        }

        try
        {
            float? slot = CampaignMissionDangerZones.ParseSlot(_dangerSlot.Text, entry.State.ObjectiveSlot);
            bool changed = _dangerZones.Apply(entry, new CampaignDangerZoneState(
                _dangerEnabled.ButtonPressed, _dangerSnapshot.ButtonPressed, slot));
            BindDangerZones();
            RebuildSelectedReader();
            UpdateExportState();
            Success(changed ? "Updated this mission's danger-zone overrides." : "Danger-zone overrides are unchanged.");
        }
        catch (Exception error) when (error is FormatException or ArgumentException or InvalidOperationException)
        {
            _dangerStatus.Text = error.Message;
        }
    }

    private void BuildTargetPanel()
    {
        _targetTab = new ScrollContainer
        {
            Name = "Targets",
            Visible = false,
        };
        _targetPanel = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _targetTab.AddChild(_targetPanel);
        _targetScope = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _targetPanel.AddChild(_targetScope);
        _copyTargets = Button("Copy chapter targets to this mission", ConfirmTargetCopy);
        _targetPanel.AddChild(_copyTargets);
        _targetPicker = new OptionButton
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _targetPicker.ItemSelected += ShowTarget;
        _targetPanel.AddChild(_targetPicker);

        var flags = new HBoxContainer();
        _targetObjective = new CheckBox { Text = "Objective" };
        _targetOther = new CheckBox { Text = "Other target" };
        flags.AddChild(_targetObjective);
        flags.AddChild(_targetOther);
        _targetPanel.AddChild(flags);

        var grid = new GridContainer { Columns = 2 };
        AddTargetLabel(grid, CampaignTargetLabel.Description, "Description key");
        AddTargetLabel(grid, CampaignTargetLabel.Category, "Category key");
        AddTargetLabel(grid, CampaignTargetLabel.Help, "Help key");
        _targetPanel.AddChild(grid);
        _applyTarget = Button("Apply target flags and labels", ApplyTarget);
        _targetPanel.AddChild(_applyTarget);
        _targetReferences = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _targetPanel.AddChild(_targetReferences);
        _targetStatus = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _targetPanel.AddChild(_targetStatus);
        _typedTabs.AddChild(_targetTab);

        _targetCopyDialog = new ConfirmationDialog
        {
            Title = "Copy inherited targets",
        };
        _targetCopyDialog.Confirmed += CopyTargetTable;
        AddChild(_targetCopyDialog);
    }

    private void AddTargetLabel(
        GridContainer grid,
        CampaignTargetLabel field,
        string title)
    {
        grid.AddChild(new Label { Text = title });
        var edit = new LineEdit
        {
            Name = field.ToString(),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _targetLabels.Add(field, edit);
        grid.AddChild(edit);
    }

    private void BindTargets()
    {
        if (_document == null || _dependencyBundle == null)
        {
            HideTargets();
            return;
        }

        int selected = Math.Max(0, _targetPicker.Selected);
        _targets = CampaignMissionTargets.Bind(
            _document,
            _dependencyBundle.ChapterReaders.Value,
            _dependencyBundle.ChapterReaders.Availability.State
                == CampaignDependencyState.Ready);
        _targetTab.Visible = true;
        string missionSource = _importedScope?.SourcePath
            ?? CampaignMissionDocument.ArchivePath(_document.Mission);
        _targetScope.Text = _targets.Origin switch
        {
            CampaignTargetOrigin.Mission =>
                $"Mission-local '{_targets.FileKey}' in '{missionSource}'; "
                + "edits affect this mission's exported scope.",
            CampaignTargetOrigin.Chapter =>
                $"Inherited '{_targets.FileKey}' from '{_dependencyBundle.ChapterReaders.Availability.Path}'; "
                + "read-only and potentially shared by multiple missions.",
            _ => _targets.Problem,
        };
        _copyTargets.Visible = _targets.Origin == CampaignTargetOrigin.Chapter;
        _copyTargets.Disabled = !_targets.CanCopy;
        _targetPicker.Clear();
        foreach (CampaignTargetEntry entry in _targets.Entries)
        {
            string[] nodes = entry.Nodes
                .Where(node => node.Selected)
                .Select(node => node.Key)
                .ToArray();
            string names = nodes.Length == 0
                ? "(no runtime nodes)"
                : string.Join(", ", nodes);
            string suffix = nodes.Length > 0 && !entry.HasEffectiveNode
                ? " (shadowed)"
                : "";
            _targetPicker.AddItem(string.Create(
                CultureInfo.InvariantCulture,
                $"[{entry.RawIndex}] {names}{suffix}"));
        }

        _targetPicker.Disabled = _targets.Entries.Count == 0;
        if (_targets.Entries.Count == 0)
        {
            SetTargetEditable(null);
            _targetReferences.Text = "";
            _targetStatus.Text = _targets.Problem;
            return;
        }

        selected = Math.Min(selected, _targets.Entries.Count - 1);
        _targetPicker.Select(selected);
        ShowTarget(selected);
    }

    private void HideTargets()
    {
        _targets = null;
        _pendingTargetCopy = null;
        if (_targetCopyDialog != null)
        {
            _targetCopyDialog.Hide();
        }

        if (_targetTab != null)
        {
            _targetTab.Visible = false;
        }
    }

    private void ShowTarget(long selected)
    {
        if (_targets == null
            || (ulong)selected >= (ulong)_targets.Entries.Count)
        {
            return;
        }

        CampaignTargetEntry entry = _targets.Entries[(int)selected];
        CampaignTargetState state = entry.State;
        _targetLabels[CampaignTargetLabel.Description].Text = state.Description ?? "";
        _targetLabels[CampaignTargetLabel.Category].Text = state.Category ?? "";
        _targetLabels[CampaignTargetLabel.Help].Text = state.Help ?? "";
        _targetObjective.ButtonPressed = state.Objective;
        _targetOther.ButtonPressed = state.OtherTarget;
        SetTargetEditable(entry);
        _targetReferences.Text = TargetReferences(entry, state);
        _targetStatus.Text = !entry.CanEdit
            ? _targets.Origin == CampaignTargetOrigin.Chapter
                ? "Copy the chapter table explicitly before editing."
                : "This entry has no runtime-selected node to edit."
            : entry.HasEffectiveNode
                ? "Both flags may be set; Objective wins the target-cycle choice."
                : "A later entry replaces every node in this entry at runtime.";
    }

    private void SetTargetEditable(CampaignTargetEntry? entry)
    {
        foreach ((CampaignTargetLabel field, LineEdit edit) in _targetLabels)
        {
            edit.Editable = entry?.CanEditLabel(field) == true;
        }

        bool editable = entry?.CanEdit == true;
        _targetObjective.Disabled = !editable;
        _targetOther.Disabled = !editable;
        _applyTarget.Disabled = !editable;
    }

    private string TargetReferences(
        CampaignTargetEntry entry,
        CampaignTargetState state)
    {
        var text = new StringBuilder();
        foreach (CampaignTargetNode node in entry.Nodes)
        {
            if (!node.Selected)
            {
                continue;
            }

            text.Append("Node ");
            text.Append(node.Key);
            text.Append(": ");
            text.AppendLine(TargetReferenceState(ReferenceRule.TargetNode, node.Path));
        }

        AppendTargetLabel(text, entry, CampaignTargetLabel.Description, state.Description);
        AppendTargetLabel(text, entry, CampaignTargetLabel.Category, state.Category);
        AppendTargetLabel(text, entry, CampaignTargetLabel.Help, state.Help);
        return text.ToString();
    }

    private void AppendTargetLabel(
        StringBuilder text,
        CampaignTargetEntry entry,
        CampaignTargetLabel label,
        string? key)
    {
        text.Append(label);
        text.Append(": ");
        if (!entry.LabelWellFormed(label))
        {
            text.AppendLine("malformed selected value, raw only");
            return;
        }

        if (key == null)
        {
            text.AppendLine("not authored");
            return;
        }

        if (key.Length == 0)
        {
            text.AppendLine("empty");
            return;
        }

        text.Append(TargetReferenceState(ReferenceRule.MessageKey, entry.LabelPath(label)));
        if (_dependencyBundle?.MessageTable.Availability.State == CampaignDependencyState.Ready
            && _dependencyBundle.MessageTable.Value.Entries.TryGetValue(key, out string? value))
        {
            text.Append(" - ");
            text.Append(value);
        }

        text.AppendLine();
    }

    private string TargetReferenceState(
        ReferenceRule rule,
        IReadOnlyList<int> path)
    {
        if (_targets == null || _referenceDiagnostics == null)
        {
            return "unverified";
        }

        ReferenceScope scope = _targets.Origin == CampaignTargetOrigin.Chapter
            ? ReferenceScope.Chapter
            : ReferenceScope.Mission;
        foreach (CheckedReference reference in _referenceDiagnostics.Current.References)
        {
            ReferenceSource source = reference.Reference.Source;
            if (reference.Reference.Rule == rule
                && source.Scope == scope
                && source.FileIndex == _targets.FileIndex
                && string.Equals(source.Container, _targets.FileKey, StringComparison.Ordinal)
                && source.Path.SequenceEqual(path))
            {
                return reference.State.ToString();
            }
        }

        return "unverified";
    }

    private void ApplyTarget()
    {
        int selected = _targetPicker.Selected;
        if (_targets == null
            || (uint)selected >= (uint)_targets.Entries.Count)
        {
            return;
        }

        CampaignTargetEntry entry = _targets.Entries[selected];
        CampaignTargetState current = entry.State;
        try
        {
            var state = new CampaignTargetState(
                TargetLabelValue(CampaignTargetLabel.Description, current.Description),
                TargetLabelValue(CampaignTargetLabel.Category, current.Category),
                TargetLabelValue(CampaignTargetLabel.Help, current.Help),
                _targetObjective.ButtonPressed,
                _targetOther.ButtonPressed);
            bool changed = entry.Apply(state);
            BindTargets();
            RebuildSelectedReader();
            UpdateExportState();
            Success(changed ? "Updated target entry." : "Target entry is unchanged.");
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            _targetStatus.Text = error.Message;
        }
    }

    private string? TargetLabelValue(CampaignTargetLabel label, string? current)
    {
        string text = _targetLabels[label].Text;
        return current == null && text.Length == 0 ? null : text;
    }

    private void ConfirmTargetCopy()
    {
        if (_targets?.CanCopy != true || _dependencyBundle == null)
        {
            return;
        }

        _pendingTargetCopy = _targets;
        _targetCopyDialog.DialogText =
            $"The chapter table at '{_dependencyBundle.ChapterReaders.Availability.Path}' can serve multiple missions. "
            + "This action copies it into the selected mission's complete export; the chapter stays unchanged. "
            + "The local copy replaces the whole chapter table for this mission and will not follow later chapter changes.";
        _targetCopyDialog.PopupCentered();
    }

    private void CopyTargetTable()
    {
        CampaignMissionTargets? pending = _pendingTargetCopy;
        _pendingTargetCopy = null;
        if (pending == null || !ReferenceEquals(pending, _targets) || _document == null)
        {
            Failure("The target selection changed before the copy was confirmed.");
            return;
        }

        try
        {
            int index = pending.CopyToMission();
            _readerPicker.AddItem(_document.Files[index].Key);
            _readerPicker.Disabled = false;
            _readerPicker.Select(index);
            BindTargets();
            RebuildReader(index);
            UpdateExportState();
            Success("Copied chapter targets into the selected mission only.");
        }
        catch (InvalidOperationException error)
        {
            _targetStatus.Text = error.Message;
        }
    }

    private void BuildReferencePanel()
    {
        _referencePanel = new VBoxContainer
        {
            Name = "Diagnostics",
            Visible = false,
        };
        _referencePanel.AddChild(new Label
        {
            Text = "Continuous cross-reader reference diagnostics",
        });
        _referenceSummary = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _referencePanel.AddChild(_referenceSummary);
        _referenceDetails = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        _referencePanel.AddChild(_referenceDetails);
        _typedTabs.AddChild(_referencePanel);
    }

    private void BindReferenceDiagnostics()
    {
        if (_referenceDiagnostics == null)
        {
            _referencePanel.Visible = false;
            _referenceSummary.Text = "";
            _referenceDetails.Text = "";
            return;
        }

        ReferenceSnapshot snapshot = _referenceDiagnostics.Current;
        int resolved = 0;
        int suppressed = 0;
        foreach (CheckedReference reference in snapshot.References)
        {
            if (reference.State == ReferenceState.Resolved)
            {
                resolved++;
            }
            else if (reference.State == ReferenceState.Suppressed)
            {
                suppressed++;
            }
        }

        _referenceSummary.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"{resolved} resolved, {snapshot.MissingCount} new missing, {snapshot.InheritedMissingCount} inherited missing, {snapshot.UnverifiedCount} unverified, {suppressed} suppressed; {snapshot.Issues.Length} bundle or parse issues.");
        var details = new StringBuilder();
        foreach (ReferenceIssue issue in snapshot.Issues)
        {
            details.Append(issue.Severity);
            details.Append(": ");
            details.AppendLine(issue.Detail);
        }

        int shown = 0;
        foreach (CheckedReference reference in snapshot.References)
        {
            if (reference.State
                is ReferenceState.Resolved or ReferenceState.Suppressed)
            {
                continue;
            }

            if (shown == 100)
            {
                details.AppendLine(
                    "Additional diagnostics remain in the immutable snapshot.");
                break;
            }

            MissionReference value = reference.Reference;
            details.Append(reference.State);
            details.Append(": ");
            details.Append(value.Kind);
            details.Append(' ');
            details.Append(ReferenceDisplay(value.Value));
            details.Append(" at ");
            details.Append(value.Source.Container);
            if (!value.Source.Path.IsDefaultOrEmpty)
            {
                details.Append(" [");
                details.Append(string.Join(",", value.Source.Path));
                details.Append(']');
            }

            if (!string.IsNullOrWhiteSpace(reference.Detail))
            {
                details.Append(" - ");
                details.Append(reference.Detail);
            }

            details.AppendLine();
            shown++;
        }

        if (details.Length == 0)
        {
            details.Append(
                "No unresolved references or dependency issues.");
        }

        _referenceDetails.Text = details.ToString();
        _referencePanel.Visible = true;
    }

    private void BuildWeatherSummaryPanel()
    {
        _weatherSummaryPanel = new VBoxContainer
        {
            Name = "Summary",
            Visible = false,
        };
        _weatherSummaryPanel.AddChild(new Label
        {
            Text = "Runtime-derived weather summary",
        });

        var zoneRow =
            new HBoxContainer();
        zoneRow.AddChild(new Label
        {
            Text = "Selected runtime zone",
        });
        _weatherSummaryPicker = new OptionButton
        {
            SizeFlagsHorizontal =
                SizeFlags.ExpandFill,
        };
        _weatherSummaryPicker.ItemSelected +=
            SelectWeatherSummaryZone;
        zoneRow.AddChild(
            _weatherSummaryPicker);
        _weatherSummaryPanel.AddChild(zoneRow);

        var altitudeRow =
            new HBoxContainer();
        altitudeRow.AddChild(new Label
        {
            Text = "Altitude probe (m)",
        });
        _weatherSummaryAltitudeEdit = new LineEdit
        {
            SizeFlagsHorizontal =
                SizeFlags.ExpandFill,
        };
        _weatherSummaryAltitudeEdit.TextSubmitted +=
            _ => UpdateWeatherSummaryAltitude();
        altitudeRow.AddChild(
            _weatherSummaryAltitudeEdit);
        altitudeRow.AddChild(
            Button(
                "Update",
                UpdateWeatherSummaryAltitude));
        _weatherSummaryPanel.AddChild(
            altitudeRow);

        _weatherSummaryPreview =
            new CampaignWeatherPreview
            {
                SizeFlagsHorizontal =
                    SizeFlags.ExpandFill,
            };
        _weatherSummaryPanel.AddChild(
            _weatherSummaryPreview);

        _weatherSummaryCloud =
            WeatherSummaryLabel();
        _weatherSummaryProbe =
            WeatherSummaryLabel();
        _weatherSummaryZone =
            WeatherSummaryLabel();
        _weatherSummaryWind =
            WeatherSummaryLabel();
        _weatherSummaryPrecipitation =
            WeatherSummaryLabel();
        _weatherSummaryLighting =
            WeatherSummaryLabel();
        _weatherSummaryPanel.AddChild(
            _weatherSummaryCloud);
        _weatherSummaryPanel.AddChild(
            _weatherSummaryProbe);
        _weatherSummaryPanel.AddChild(
            _weatherSummaryZone);
        _weatherSummaryPanel.AddChild(
            _weatherSummaryWind);
        _weatherSummaryPanel.AddChild(
            _weatherSummaryPrecipitation);
        _weatherSummaryPanel.AddChild(
            _weatherSummaryLighting);

        _weatherSummaryStatus =
            WeatherSummaryLabel();
        _weatherSummaryPanel.AddChild(
            _weatherSummaryStatus);
        _typedTabs.AddChild(
            _weatherSummaryPanel);
    }

    private void BindWeatherSummary()
    {
        if (_document == null
            || !CampaignWeatherDiagnostics.TryCreate(
                _document,
                out CampaignWeatherDiagnostics? diagnostics,
                out _))
        {
            HideWeatherSummary();
            return;
        }

        _weatherDiagnostics = diagnostics;
        _weatherSummaryPanel.Visible = true;
        if (!_weatherSummaryAltitudeSet)
        {
            _weatherSummaryAltitude =
                diagnostics.DefaultAltitude;
            _weatherSummaryAltitudeSet = true;
        }

        _weatherSummaryAltitudeEdit.Text =
            FormatFloat(
                _weatherSummaryAltitude);
        _weatherSummaryPicker.Clear();
        int selected = 0;
        for (int i = 0;
             i < diagnostics.ZoneNames.Count;
             i++)
        {
            string zone =
                diagnostics.ZoneNames[i];
            _weatherSummaryPicker.AddItem(zone);
            if (_weatherSummaryZoneName != null
                && zone.Equals(
                    _weatherSummaryZoneName,
                    StringComparison.OrdinalIgnoreCase))
            {
                selected = i;
            }
        }

        _weatherSummaryPicker.Disabled =
            diagnostics.ZoneNames.Count == 0;
        if (diagnostics.ZoneNames.Count > 0)
        {
            _weatherSummaryPicker.Select(selected);
            _weatherSummaryZoneName =
                diagnostics.ZoneNames[selected];
        }
        else
        {
            _weatherSummaryZoneName = null;
        }

        ShowWeatherSummary();
    }

    private void HideWeatherSummary()
    {
        _weatherDiagnostics = null;
        if (_weatherSummaryPanel != null)
        {
            _weatherSummaryPanel.Visible = false;
        }
    }

    private void SelectWeatherSummaryZone(
        long selected)
    {
        if (_weatherDiagnostics == null
            || (ulong)selected
                >= (ulong)_weatherDiagnostics.ZoneNames.Count)
        {
            return;
        }

        _weatherSummaryZoneName =
            _weatherDiagnostics.ZoneNames[
                (int)selected];
        ShowWeatherSummary();
    }

    private void UpdateWeatherSummaryAltitude()
    {
        string text =
            _weatherSummaryAltitudeEdit.Text;
        if (!float.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float altitude)
            || !float.IsFinite(altitude))
        {
            _weatherSummaryStatus.Text =
                "Altitude probe must be a finite number.";
            return;
        }

        _weatherSummaryAltitude =
            altitude;
        _weatherSummaryAltitudeSet = true;
        _weatherSummaryAltitudeEdit.Text =
            FormatFloat(altitude);
        ShowWeatherSummary();
    }

    private void ShowWeatherSummary()
    {
        if (_weatherDiagnostics == null)
        {
            return;
        }

        CampaignWeatherSummary summary =
            _weatherDiagnostics.Summary(
                _weatherSummaryZoneName,
                _weatherSummaryAltitude);
        _weatherSummaryCloud.Text =
            summary.CloudLabel;
        _weatherSummaryProbe.Text =
            summary.ProbeLabel;
        _weatherSummaryZone.Text =
            summary.ZoneLabel;
        _weatherSummaryWind.Text =
            summary.WindLabel;
        _weatherSummaryPrecipitation.Text =
            summary.PrecipitationLabel;
        _weatherSummaryLighting.Text =
            summary.LightingLabel;
        _weatherSummaryPreview.ShowSummary(
            summary);
        _weatherSummaryStatus.Text =
            "Numeric weather only. Chapter deck, horizon, and fog-volume geometry are not loaded.";
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
            BindWeatherSummary();
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
            BindWeatherSummary();
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

    private void BuildWeatherPrecipitationPanel()
    {
        _weatherPrecipitationPanel = new VBoxContainer
        {
            Name = "Precipitation",
            Visible = false,
        };
        _weatherPrecipitationPanel.AddChild(new Label
        {
            Text = "Weather precipitation",
        });

        _weatherPrecipitationGrid = new GridContainer
        {
            Columns = 2,
        };
        _weatherPrecipitationGrid.AddChild(new Label
        {
            Text = "Type",
        });
        _weatherPrecipitationKind = new OptionButton();
        _weatherPrecipitationKind.AddItem("Rain");
        _weatherPrecipitationKind.AddItem("Snow");
        _weatherPrecipitationGrid.AddChild(
            _weatherPrecipitationKind);
        AddWeatherPrecipitationField(
            WeatherPrecipitationField.ColorR,
            "Color red (raw)");
        AddWeatherPrecipitationField(
            WeatherPrecipitationField.ColorG,
            "Color green (raw)");
        AddWeatherPrecipitationField(
            WeatherPrecipitationField.ColorB,
            "Color blue (raw)");
        AddWeatherPrecipitationField(
            WeatherPrecipitationField.WindDir,
            "Drift direction (deg)");
        AddWeatherPrecipitationField(
            WeatherPrecipitationField.WindVel,
            "Drift speed (data units)");
        AddWeatherPrecipitationField(
            WeatherPrecipitationField.Gravity,
            "Gravity (data units)");
        AddWeatherPrecipitationField(
            WeatherPrecipitationField.Particles,
            "Particles (integer)");
        AddWeatherPrecipitationField(
            WeatherPrecipitationField.AlphaGradientX,
            "Alpha gradient [0] (peak)");
        AddWeatherPrecipitationField(
            WeatherPrecipitationField.AlphaGradientY,
            "Alpha gradient [1] (raw)");
        _weatherPrecipitationPanel.AddChild(
            _weatherPrecipitationGrid);

        _applyWeatherPrecipitation = Button(
            "Apply precipitation",
            ApplyWeatherPrecipitation);
        _weatherPrecipitationPanel.AddChild(
            _applyWeatherPrecipitation);
        _weatherPrecipitationStatus = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _weatherPrecipitationPanel.AddChild(
            _weatherPrecipitationStatus);
        _typedTabs.AddChild(_weatherPrecipitationPanel);
    }

    private void AddWeatherPrecipitationField(
        WeatherPrecipitationField field,
        string label)
    {
        _weatherPrecipitationGrid.AddChild(new Label
        {
            Text = label,
        });
        LineEdit edit = new()
        {
            Name = field.ToString(),
        };
        _weatherPrecipitationFields.Add(field, edit);
        _weatherPrecipitationGrid.AddChild(edit);
    }

    private void BindWeatherPrecipitation()
    {
        if (_document == null)
        {
            HideWeatherPrecipitation();
            return;
        }

        if (!CampaignWeatherPrecipitation.TryBind(
                _document,
                out CampaignWeatherPrecipitation? binding,
                out _))
        {
            HideWeatherPrecipitation();
            return;
        }

        _weatherPrecipitation = binding;
        _weatherPrecipitationPanel.Visible = true;
        CampaignWeatherPrecipitationState state =
            binding.State;
        _weatherPrecipitationKind.Select(
            (int)state.Kind);
        SetWeatherPrecipitationField(
            WeatherPrecipitationField.ColorR,
            state.Color.R);
        SetWeatherPrecipitationField(
            WeatherPrecipitationField.ColorG,
            state.Color.G);
        SetWeatherPrecipitationField(
            WeatherPrecipitationField.ColorB,
            state.Color.B);
        SetWeatherPrecipitationField(
            WeatherPrecipitationField.WindDir,
            state.WindDirDeg);
        SetWeatherPrecipitationField(
            WeatherPrecipitationField.WindVel,
            state.WindVel);
        SetWeatherPrecipitationField(
            WeatherPrecipitationField.Gravity,
            state.Gravity);
        SetWeatherPrecipitationParticles(
            state.Particles);
        SetWeatherPrecipitationField(
            WeatherPrecipitationField.AlphaGradientX,
            state.AlphaGradientX);
        SetWeatherPrecipitationField(
            WeatherPrecipitationField.AlphaGradientY,
            state.AlphaGradientY);
        SetWeatherPrecipitationEditable(
            true,
            state.Particles.HasValue);

        string encoding =
            binding.UsesByteColor
                ? "0-255"
                : "normalized 0-1";
        string particles =
            state.Particles.HasValue
                ? "PARTICLES is present and remains an integer."
                : "PARTICLES is absent and will not be created.";
        _weatherPrecipitationStatus.Text =
            $"COLOR uses authored {encoding} values. {particles}";
    }

    private void HideWeatherPrecipitation()
    {
        _weatherPrecipitation = null;
        if (_weatherPrecipitationPanel != null)
        {
            _weatherPrecipitationPanel.Visible = false;
        }
    }

    private void SetWeatherPrecipitationEditable(
        bool editable,
        bool particlesEditable = false)
    {
        _weatherPrecipitationKind.Disabled = !editable;
        foreach (LineEdit field in
            _weatherPrecipitationFields.Values)
        {
            field.Editable = editable;
        }

        _weatherPrecipitationFields[
            WeatherPrecipitationField.Particles].Editable =
            editable && particlesEditable;
        _applyWeatherPrecipitation.Disabled = !editable;
    }

    private void SetWeatherPrecipitationField(
        WeatherPrecipitationField field,
        float value) =>
        _weatherPrecipitationFields[field].Text =
            FormatFloat(value);

    private void SetWeatherPrecipitationParticles(
        int? particles) =>
        _weatherPrecipitationFields[
            WeatherPrecipitationField.Particles].Text =
            particles?.ToString(
                CultureInfo.InvariantCulture)
            ?? "";

    private void ApplyWeatherPrecipitation()
    {
        if (_weatherPrecipitation == null)
        {
            return;
        }

        try
        {
            CampaignWeatherPrecipitationState current =
                _weatherPrecipitation.State;
            CampaignWeatherPrecipitationState state = new(
                (CampaignWeatherPrecipitationKind)
                    _weatherPrecipitationKind.Selected,
                WeatherPrecipitationColor(),
                WeatherPrecipitationValue(
                    WeatherPrecipitationField.WindDir),
                WeatherPrecipitationValue(
                    WeatherPrecipitationField.WindVel),
                WeatherPrecipitationValue(
                    WeatherPrecipitationField.Gravity),
                current.Particles.HasValue
                    ? WeatherPrecipitationInteger(
                        WeatherPrecipitationField.Particles)
                    : null,
                WeatherPrecipitationValue(
                    WeatherPrecipitationField.AlphaGradientX),
                WeatherPrecipitationValue(
                    WeatherPrecipitationField.AlphaGradientY));
            bool changed =
                _weatherPrecipitation.Apply(state);
            BindWeatherPrecipitation();
            BindWeatherSummary();
            RebuildSelectedReader();
            UpdateExportState();
            Success(changed
                ? "Updated weather precipitation."
                : "Weather precipitation is unchanged.");
        }
        catch (FormatException e)
        {
            _weatherPrecipitationStatus.Text = e.Message;
        }
        catch (ArgumentException e)
        {
            _weatherPrecipitationStatus.Text = e.Message;
        }
        catch (InvalidOperationException e)
        {
            _weatherPrecipitationStatus.Text = e.Message;
            Log.Warn("ui", $"campaign editor: {e.Message}");
        }
    }

    private CampaignWeatherColorState WeatherPrecipitationColor() =>
        new(
            WeatherPrecipitationValue(
                WeatherPrecipitationField.ColorR),
            WeatherPrecipitationValue(
                WeatherPrecipitationField.ColorG),
            WeatherPrecipitationValue(
                WeatherPrecipitationField.ColorB));

    private float WeatherPrecipitationValue(
        WeatherPrecipitationField field)
    {
        string text =
            _weatherPrecipitationFields[field].Text;
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

    private int WeatherPrecipitationInteger(
        WeatherPrecipitationField field)
    {
        string text =
            _weatherPrecipitationFields[field].Text;
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
            BindWeatherSummary();
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

    private void BuildWeatherLightingPanel()
    {
        _weatherLightingPanel = new VBoxContainer
        {
            Name = "Light",
            Visible = false,
        };
        _weatherLightingPanel.AddChild(new Label
        {
            Text = "Weather zone clip and sunlight",
        });

        _weatherLightingPicker = new OptionButton
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _weatherLightingPicker.ItemSelected +=
            ShowWeatherLightingZone;
        _weatherLightingPanel.AddChild(
            _weatherLightingPicker);

        _weatherLightingGrid = new GridContainer
        {
            Columns = 2,
        };
        AddWeatherLightingField(
            WeatherLightingField.ClipFar,
            "Clip far (m)");
        AddWeatherLightingField(
            WeatherLightingField.SunDiffuse,
            "Sun diffuse");
        AddWeatherLightingField(
            WeatherLightingField.SunAmbient,
            "Sun ambient");
        AddWeatherLightingField(
            WeatherLightingField.DiffuseColorR,
            "Diffuse red (raw)");
        AddWeatherLightingField(
            WeatherLightingField.DiffuseColorG,
            "Diffuse green (raw)");
        AddWeatherLightingField(
            WeatherLightingField.DiffuseColorB,
            "Diffuse blue (raw)");
        AddWeatherLightingField(
            WeatherLightingField.AmbientColorR,
            "Ambient red (raw)");
        AddWeatherLightingField(
            WeatherLightingField.AmbientColorG,
            "Ambient green (raw)");
        AddWeatherLightingField(
            WeatherLightingField.AmbientColorB,
            "Ambient blue (raw)");
        AddWeatherLightingField(
            WeatherLightingField.SunPitch,
            "Shading pitch (degrees)");
        AddWeatherLightingField(
            WeatherLightingField.SunYaw,
            "Shading yaw (degrees)");
        AddWeatherLightingField(
            WeatherLightingField.SunRoll,
            "Shading roll (degrees)");
        _weatherLightingGrid.AddChild(new Label
        {
            Text = "Bicolored light",
        });
        _weatherLightingBicolored = new CheckBox();
        _weatherLightingGrid.AddChild(
            _weatherLightingBicolored);
        _weatherLightingPanel.AddChild(
            _weatherLightingGrid);

        _applyWeatherLighting = Button(
            "Apply weather light",
            ApplyWeatherLighting);
        _weatherLightingPanel.AddChild(
            _applyWeatherLighting);
        _weatherLightingStatus = new Label
        {
            AutowrapMode =
                TextServer.AutowrapMode.WordSmart,
        };
        _weatherLightingPanel.AddChild(
            _weatherLightingStatus);
        _typedTabs.AddChild(
            _weatherLightingPanel);
    }

    private void AddWeatherLightingField(
        WeatherLightingField field,
        string label)
    {
        _weatherLightingGrid.AddChild(new Label
        {
            Text = label,
        });
        LineEdit edit = new()
        {
            Name = field.ToString(),
        };
        _weatherLightingFields.Add(field, edit);
        _weatherLightingGrid.AddChild(edit);
    }

    private void BindWeatherLighting()
    {
        if (_document == null)
        {
            HideWeatherLighting();
            return;
        }

        int selected =
            Math.Max(
                0,
                _weatherLightingPicker.Selected);
        _weatherLightingPanel.Visible = true;
        _weatherLightingPicker.Clear();
        if (!CampaignWeatherLightingZones.TryBind(
                _document,
                out CampaignWeatherLightingZones? binding,
                out string problem))
        {
            _weatherLighting = null;
            _weatherLightingPicker.Disabled = true;
            SetWeatherLightingEditable(false);
            _weatherLightingStatus.Text = problem;
            return;
        }

        _weatherLighting = binding;
        foreach (CampaignWeatherLightingZone zone
            in binding.Zones)
        {
            _weatherLightingPicker.AddItem(zone.Name);
        }

        _weatherLightingPicker.Disabled =
            binding.Zones.Count == 0;
        selected =
            Math.Min(
                selected,
                binding.Zones.Count - 1);
        _weatherLightingPicker.Select(selected);
        ShowWeatherLightingZone(selected);
    }

    private void HideWeatherLighting()
    {
        _weatherLighting = null;
        if (_weatherLightingPanel != null)
        {
            _weatherLightingPanel.Visible = false;
        }
    }

    private void ShowWeatherLightingZone(
        long selected)
    {
        if (_weatherLighting == null
            || (ulong)selected
                >= (ulong)_weatherLighting.Zones.Count)
        {
            return;
        }

        CampaignWeatherLightingZone zone =
            _weatherLighting.Zones[(int)selected];
        CampaignWeatherLightingState state =
            zone.State;
        SetWeatherLightingField(
            WeatherLightingField.ClipFar,
            state.ClipFar);
        SetWeatherLightingField(
            WeatherLightingField.SunDiffuse,
            state.SunDiffuse);
        SetWeatherLightingField(
            WeatherLightingField.SunAmbient,
            state.SunAmbient);
        SetWeatherLightingColor(
            state.SunColorDiffuse,
            WeatherLightingField.DiffuseColorR,
            WeatherLightingField.DiffuseColorG,
            WeatherLightingField.DiffuseColorB);
        SetWeatherLightingColor(
            state.SunColorAmbient,
            WeatherLightingField.AmbientColorR,
            WeatherLightingField.AmbientColorG,
            WeatherLightingField.AmbientColorB);
        SetWeatherLightingField(
            WeatherLightingField.SunPitch,
            state.SunPitch);
        SetWeatherLightingField(
            WeatherLightingField.SunYaw,
            state.SunYaw);
        SetWeatherLightingField(
            WeatherLightingField.SunRoll,
            state.SunRoll);
        _weatherLightingBicolored.ButtonPressed =
            state.SunBicolored ?? false;
        _weatherLightingBicolored.Disabled =
            !state.SunBicolored.HasValue;
        _applyWeatherLighting.Disabled =
            !HasEditableWeatherLightingField()
            && _weatherLightingBicolored.Disabled;

        string diffuseEncoding =
            WeatherLightingEncoding(
                "Diffuse",
                zone.DiffuseUsesByteColor);
        string ambientEncoding =
            WeatherLightingEncoding(
                "Ambient",
                zone.AmbientUsesByteColor);
        string roll =
            state.SunRoll.HasValue
                ? "The authored roll remains editable."
                : "The absent roll will not be created.";
        _weatherLightingStatus.Text =
            $"{diffuseEncoding} {ambientEncoding} {roll} Missing values stay absent. Orientation is the shading direction, not the sun billboard. Clip far is informational in CSVM.";
    }

    private void SetWeatherLightingEditable(
        bool editable)
    {
        foreach (LineEdit field
            in _weatherLightingFields.Values)
        {
            field.Editable = editable;
        }

        _weatherLightingBicolored.Disabled =
            !editable;
        _applyWeatherLighting.Disabled =
            !editable;
    }

    private void SetWeatherLightingField(
        WeatherLightingField field,
        float? value)
    {
        LineEdit edit =
            _weatherLightingFields[field];
        edit.Text =
            value.HasValue
                ? FormatFloat(value.Value)
                : "";
        edit.Editable = value.HasValue;
    }

    private void SetWeatherLightingColor(
        CampaignWeatherColorState? color,
        WeatherLightingField red,
        WeatherLightingField green,
        WeatherLightingField blue)
    {
        SetWeatherLightingField(
            red,
            color?.R);
        SetWeatherLightingField(
            green,
            color?.G);
        SetWeatherLightingField(
            blue,
            color?.B);
    }

    private bool HasEditableWeatherLightingField()
    {
        foreach (LineEdit field
            in _weatherLightingFields.Values)
        {
            if (field.Editable)
            {
                return true;
            }
        }

        return false;
    }

    private void ApplyWeatherLighting()
    {
        CampaignWeatherLightingZone? zone =
            SelectedWeatherLightingZone();
        if (zone == null)
        {
            return;
        }

        try
        {
            CampaignWeatherLightingState current =
                zone.State;
            var state =
                new CampaignWeatherLightingState(
                    WeatherLightingValue(
                        WeatherLightingField.ClipFar,
                        current.ClipFar),
                    WeatherLightingValue(
                        WeatherLightingField.SunDiffuse,
                        current.SunDiffuse),
                    WeatherLightingValue(
                        WeatherLightingField.SunAmbient,
                        current.SunAmbient),
                    WeatherLightingColor(
                        current.SunColorDiffuse,
                        WeatherLightingField.DiffuseColorR,
                        WeatherLightingField.DiffuseColorG,
                        WeatherLightingField.DiffuseColorB),
                    WeatherLightingColor(
                        current.SunColorAmbient,
                        WeatherLightingField.AmbientColorR,
                        WeatherLightingField.AmbientColorG,
                        WeatherLightingField.AmbientColorB),
                    current.SunBicolored.HasValue
                        ? _weatherLightingBicolored.ButtonPressed
                        : null,
                    WeatherLightingValue(
                        WeatherLightingField.SunPitch,
                        current.SunPitch),
                    WeatherLightingValue(
                        WeatherLightingField.SunYaw,
                        current.SunYaw),
                    WeatherLightingValue(
                        WeatherLightingField.SunRoll,
                        current.SunRoll));
            bool changed =
                zone.Apply(state);
            BindWeatherLighting();
            BindWeatherSummary();
            RebuildSelectedReader();
            UpdateExportState();
            Success(changed
                ? $"Updated weather light '{zone.Name}'."
                : $"Weather light '{zone.Name}' is unchanged.");
        }
        catch (FormatException e)
        {
            _weatherLightingStatus.Text = e.Message;
        }
        catch (ArgumentException e)
        {
            _weatherLightingStatus.Text = e.Message;
        }
        catch (InvalidOperationException e)
        {
            _weatherLightingStatus.Text = e.Message;
            Log.Warn(
                "ui",
                $"campaign editor: {e.Message}");
        }
    }

    private CampaignWeatherLightingZone?
        SelectedWeatherLightingZone()
    {
        int index =
            _weatherLightingPicker.Selected;
        return _weatherLighting != null
            && (uint)index
                < (uint)_weatherLighting.Zones.Count
            ? _weatherLighting.Zones[index]
            : null;
    }

    private float? WeatherLightingValue(
        WeatherLightingField field,
        float? current)
    {
        if (!current.HasValue)
        {
            return null;
        }

        string text =
            _weatherLightingFields[field].Text;
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

    private CampaignWeatherColorState?
        WeatherLightingColor(
            CampaignWeatherColorState? current,
            WeatherLightingField red,
            WeatherLightingField green,
            WeatherLightingField blue)
    {
        if (!current.HasValue)
        {
            return null;
        }

        return new CampaignWeatherColorState(
            WeatherLightingValue(
                red,
                current.Value.R)!.Value,
            WeatherLightingValue(
                green,
                current.Value.G)!.Value,
            WeatherLightingValue(
                blue,
                current.Value.B)!.Value);
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
                BindTargets();
                BindWeatherCloudBand();
                BindWeatherWind();
                BindWeatherPrecipitation();
                BindWeatherFog();
                BindWeatherLighting();
                BindWeatherSummary();
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

        if (_referenceDiagnostics?.Current.HasErrors == true)
        {
            BindReferenceDiagnostics();
            Failure(
                "Repair the confirmed reference and dependency errors before exporting.");
            return;
        }

        try
        {
            string projectRoot = ProjectSettings.GlobalizePath("res://");
            string output = _document.Export(
                _modPath.Text.Trim(),
                projectRoot);
            UpdateExportState();
            string owner = _importedScope == null
                ? "retail scope"
                : $"existing {_importedScope.SourceKind} scope";
            Success(
                $"Exported the complete {owner} to '{output}'.");
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
        _undoBridge?.Invalidate();
        _undoBridge = null;
        _preview?.Clear();
        _referenceDiagnostics?.Dispose();
        _referenceDiagnostics = null;
        _document = null;
        _dependencyBundle = null;
        _importedScope = null;
        _typedTabs.Visible = false;
        HidePlayerInit();
        HideRosterStarts();
        HideTargets();
        HideZeppelins();
        HideGenerators();
        HideObjectives();
        HideDangerZones();
        HideStartAnimations();
        _dangerWorld = null;
        BindReferenceDiagnostics();
        HideWeatherSummary();
        HideWeatherCloudBand();
        HideWeatherWind();
        HideWeatherPrecipitation();
        HideWeatherFog();
        HideWeatherLighting();
        _weatherSummaryZoneName = null;
        _weatherSummaryAltitudeSet = false;
        _readerPicker.Clear();
        _readerPicker.Disabled = true;
        _readerTree.Clear();
        UpdateExportState();
    }

    private void ClearSource()
    {
        _source = null;
        _editorData = null;
        _scopeMode.Disabled = true;
        _scopeMode.Select((int)CampaignScopeMode.Retail);
        _importRow.Visible = false;
        _importPath.Text = "";
        _importMissionIndex = null;
        _missions.Clear();
        _missionPicker.Clear();
        _missionPicker.Disabled = true;
        ClearDocument();
    }

    private void UndoRegistrationFailed(Exception error)
    {
        RefreshAfterHistory();
        Failure("The edit could not be added to editor history.", error);
    }

    private void RefreshAfterHistory()
    {
        if (_document == null)
        {
            return;
        }

        _pendingTargetCopy = null;
        _targetCopyDialog.Hide();
        _objectiveSwapDialog.Hide();
        int selected = _readerPicker.Selected;
        _readerPicker.Clear();
        foreach (ZrdrFile file in _document.Files)
        {
            _readerPicker.AddItem(file.Key);
        }

        _readerPicker.Disabled = _document.Files.Count == 0;
        if (_document.Files.Count == 0)
        {
            _readerTree.Clear();
        }
        else
        {
            selected = Math.Clamp(selected, 0, _document.Files.Count - 1);
            _readerPicker.Select(selected);
            RebuildReader(selected);
        }

        BindReferenceDiagnostics();
        BindPlayerInit();
        BindRosterStarts();
        BindTargets();
        BindZeppelins();
        BindGenerators();
        BindObjectives();
        BindDangerZones();
        BindStartAnimations();
        BindWeatherCloudBand();
        BindWeatherWind();
        BindWeatherPrecipitation();
        BindWeatherFog();
        BindWeatherLighting();
        BindWeatherSummary();
        UpdateExportState();
        Success("Updated the mission from editor history.");
    }

    private void UpdateExportState()
    {
        if (_exportButton != null)
        {
            if (_rosterStarts != null && !_rosterStarts.IsCurrent)
            {
                BindRosterStarts();
            }

            if (_targets != null && !_targets.IsCurrent)
            {
                BindTargets();
            }

            if (_zeppelins != null && !_zeppelins.IsCurrent)
            {
                BindZeppelins();
            }

            if (_generators != null && !_generators.IsCurrent)
            {
                BindGenerators();
            }

            if (_objectives != null && !_objectives.IsCurrent)
            {
                BindObjectives();
            }

            if (_dangerZones != null && !_dangerZones.IsCurrent)
            {
                BindDangerZones();
            }

            BindReferenceDiagnostics();
            if (_startAnimations != null && !_startAnimations.IsCurrent)
            {
                BindStartAnimations();
            }

            bool referenceErrors =
                _referenceDiagnostics?.Current.HasErrors == true;
            _exportButton.Disabled = _document == null
                || string.IsNullOrWhiteSpace(_modPath.Text)
                || referenceErrors;
        }
    }

    private void OpenSourceDialog() =>
        OpenDialog(_sourceDialog, _sourcePath.Text);

    private void OpenModDialog() =>
        OpenDialog(_modDialog, _modPath.Text);

    private void OpenImportDialog() =>
        OpenDialog(_importDialog, _importPath.Text);

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
        bool foldersOnly,
        string[]? filters = null) =>
        new()
        {
            FileMode = foldersOnly
                ? FileDialog.FileModeEnum.OpenDir
                : FileDialog.FileModeEnum.OpenAny,
            Filters = filters
                ?? (foldersOnly
                    ? Array.Empty<string>()
                    : new[]
                {
                    "*.iso, *.cue, *.ccd, *.bin, *.img ; Disc images",
                }),
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
