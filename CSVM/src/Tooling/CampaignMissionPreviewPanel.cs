using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CSVM.Utils;
using Godot;

namespace CSVM.Tooling;

/// <summary>Opt-in editor viewport for disposable chapter geometry and mission overlays.</summary>
[Tool]
internal sealed partial class CampaignMissionPreviewPanel : VBoxContainer
{
    private static readonly CampaignPreviewLayers[] LayerOrder =
    {
        CampaignPreviewLayers.Player,
        CampaignPreviewLayers.Roster,
        CampaignPreviewLayers.Targets,
        CampaignPreviewLayers.Generators,
        CampaignPreviewLayers.Zeppelins,
        CampaignPreviewLayers.Danger,
    };

    private readonly Dictionary<CampaignPreviewLayers, Node3D> _layers = new();
    private readonly List<(CampaignPreviewItem Item, Node3D Handle)> _handles = new();
    private readonly List<CampaignPreviewItem> _rows = new();
    private CampaignMissionDocument? _document;
    private CampaignMissionDependencyBundle? _bundle;
    private CampaignDangerZoneWorld? _danger;
    private CampaignMissionPreview? _model;
    private CampaignMissionPreviewWorld? _world;
    private SubViewport? _viewport;
    private Node3D? _overlays;
    private Camera3D? _camera;
    private Button _toggle = null!;
    private Button _frame = null!;
    private Button _showRaw = null!;
    private SubViewportContainer _viewportHost = null!;
    private ItemList _itemList = null!;
    private Label _details = null!;
    private Label _status = null!;
    private Vector3 _focus;
    private float _distance = 1200f;
    private float _azimuth = 0.6f;
    private float _elevation = 0.55f;
    private bool _orbiting;

    internal event Action<CampaignPreviewSource>? ShowSource;

    public override void _Ready()
    {
        _toggle = new Button { Name = "preview_toggle", Text = "Open read-only 3D preview", Disabled = true };
        _toggle.Pressed += Toggle;
        AddChild(_toggle);
        AddChild(new Label
        {
            Text = "Left-click a handle or choose a row. Right-drag orbits; the wheel zooms. Frame centres the selection.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        _viewportHost = new SubViewportContainer
        {
            Name = "preview_container",
            Stretch = true,
            CustomMinimumSize = new Vector2(350f, 230f),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Stop,
            Visible = false,
        };
        _viewportHost.GuiInput += ViewportInput;
        AddChild(_viewportHost);
        _itemList = new ItemList
        {
            CustomMinimumSize = new Vector2(0f, 100f),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            Visible = false,
        };
        _itemList.ItemSelected += index => Select((int)index);
        AddChild(_itemList);
        var actions = new HBoxContainer();
        _frame = new Button { Text = "Frame selection", Disabled = true };
        _frame.Pressed += FrameSelection;
        actions.AddChild(_frame);
        _showRaw = new Button { Text = "Show mission source", Disabled = true };
        _showRaw.Pressed += ShowMissionSource;
        actions.AddChild(_showRaw);
        AddChild(actions);
        _details = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        AddChild(_details);
        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        AddChild(_status);
    }

    public override void _Process(double delta)
    {
        if (_model == null || _model.Dirty == CampaignPreviewLayers.None)
        {
            return;
        }

        try
        {
            Refresh();
        }
        catch (Exception error) when (InputError(error))
        {
            Fail(error);
        }
    }

    public override void _ExitTree()
    {
        Clear();
        ShowSource = null;
    }

    internal void Bind(
        CampaignMissionDocument document,
        CampaignMissionDependencyBundle bundle,
        CampaignDangerZoneWorld danger)
    {
        Clear();
        _document = document;
        _bundle = bundle;
        _danger = danger;
        _toggle.Disabled = false;
        _status.Text = "Preview is closed. Opening builds a transient copy of the selected chapter world.";
    }

    internal void Clear()
    {
        ClosePreview();
        _document = null;
        _bundle = null;
        _danger = null;
        if (_toggle != null)
        {
            _toggle.Disabled = true;
            _status.Text = "Load a mission to preview.";
        }
    }

    private static bool InputError(Exception error) =>
        error is ArgumentException or InvalidOperationException or IndexOutOfRangeException
            or System.IO.IOException or NotSupportedException or OverflowException
            or FormatException or KeyNotFoundException;

    private static CampaignPreviewSource? MissionSource(CampaignPreviewItem item) =>
        item.Source.Scope == ReferenceScope.Mission
            ? item.Source
            : item.RelatedSource?.Scope == ReferenceScope.Mission ? item.RelatedSource : null;

    private static string SourceText(CampaignPreviewSource source) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{source.Scope} {source.Container} reader {source.FileIndex?.ToString(CultureInfo.InvariantCulture) ?? "-"} [{string.Join(",", source.Path)}]");

    private static Color ColorOf(CampaignPreviewLayers layer, bool enabled)
    {
        if (!enabled)
        {
            return new Color(0.55f, 0.55f, 0.55f);
        }

        return layer switch
        {
            CampaignPreviewLayers.Player => new Color(0.2f, 1f, 1f),
            CampaignPreviewLayers.Roster => new Color(0.3f, 1f, 0.3f),
            CampaignPreviewLayers.Targets => new Color(1f, 0.25f, 0.25f),
            CampaignPreviewLayers.Generators => new Color(1f, 1f, 0.15f),
            CampaignPreviewLayers.Zeppelins => new Color(0.85f, 0.4f, 1f),
            _ => new Color(1f, 0.55f, 0.1f),
        };
    }

    private void Toggle()
    {
        if (_model != null)
        {
            ClosePreview();
            _status.Text = "Preview is closed.";
            return;
        }

        if (_document == null || _bundle == null || _danger == null)
        {
            return;
        }

        try
        {
            _viewport = new SubViewport
            {
                Name = "mission_preview",
                Size = new Vector2I(350, 230),
                OwnWorld3D = true,
                World3D = new World3D(),
                RenderTargetUpdateMode = SubViewport.UpdateMode.WhenVisible,
            };
            _viewportHost.AddChild(_viewport);
            _viewportHost.Visible = true;
            _world = CampaignMissionPreviewWorld.Build(_viewport, _bundle);
            _overlays = new Node3D { Name = "derived_overlays" };
            _viewport.AddChild(_overlays);
            _camera = new Camera3D
            {
                Name = "preview_camera",
                Current = true,
                Far = 100000f,
                CullMask = 0xFFFFF,
            };
            _viewport.AddChild(_camera);
            _model = new CampaignMissionPreview(
                _document,
                _bundle.ChapterReaders.Value,
                _bundle.ChapterReaders.Availability.State == CampaignDependencyState.Ready,
                _danger,
                _world,
                _bundle.World.Availability.Path);
            _toggle.Text = "Close 3D preview";
            _itemList.Visible = true;
            Refresh();
            if (_rows.Count > 0)
            {
                _itemList.Select(0);
                Select(0);
                FrameSelection();
            }
            else
            {
                MoveCamera();
            }
        }
        catch (Exception error) when (InputError(error))
        {
            Fail(error);
        }
    }

    private void Refresh()
    {
        if (_model == null)
        {
            return;
        }

        CampaignPreviewLayers changed = _model.Refresh();
        foreach (CampaignPreviewLayers layer in LayerOrder)
        {
            if (changed.HasFlag(layer))
            {
                RebuildLayer(layer, _model.Items(layer));
            }
        }

        RebuildRows();
        bool setupAvailable = _bundle != null
            && _bundle.Setup.Availability.State == CampaignDependencyState.Ready
            && _bundle.Setup.Value != null;
        string setup = setupAvailable
            ? "Mission setup is applied."
            : "Mission setup is unavailable or absent; showing the unmodified world.";
        var notes = new List<string>
        {
            setup,
            "Startup/compiled animations, runtime staging, AI nets, weather zone culling and live movement are not simulated.",
            _model.LocationStatus,
            "Cyan player, green roster, red targets, yellow generators, purple zeppelins, orange danger ribbons.",
        };
        foreach (CampaignPreviewLayers layer in LayerOrder)
        {
            string problem = _model.Status(layer);
            if (problem.Length > 0)
            {
                notes.Add($"{layer}: {problem}");
            }
        }

        _status.Text = string.Join("\n", notes);
        ScaleHandles();
    }

    private void RebuildLayer(CampaignPreviewLayers layer, IReadOnlyList<CampaignPreviewItem> items)
    {
        if (_overlays == null)
        {
            return;
        }

        if (_layers.Remove(layer, out Node3D? previous))
        {
            _handles.RemoveAll(entry => entry.Item.Layer == layer);
            previous.Free();
        }

        var group = new Node3D { Name = layer.ToString() };
        _overlays.AddChild(group);
        _layers.Add(layer, group);
        foreach (CampaignPreviewItem item in items)
        {
            var material = new StandardMaterial3D
            {
                AlbedoColor = ColorOf(layer, item.Enabled),
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                NoDepthTest = true,
                DisableFog = true,
            };
            if (!item.Points.IsDefaultOrEmpty)
            {
                var line = new ImmediateMesh();
                line.SurfaceBegin(Mesh.PrimitiveType.LineStrip, material);
                foreach (Vector3 point in item.Points)
                {
                    line.SurfaceAddVertex(point);
                }

                line.SurfaceEnd();
                group.AddChild(new MeshInstance3D
                {
                    Name = "ribbon",
                    Mesh = line,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                });
            }

            var handle = new Node3D
            {
                Name = "handle",
                Transform = new Transform3D(item.Pose.Basis.Orthonormalized(), item.Pose.Origin),
            };
            group.AddChild(handle);
            handle.AddChild(new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = 0.35f, Height = 0.7f },
                MaterialOverride = material,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
            if (layer is CampaignPreviewLayers.Player or CampaignPreviewLayers.Roster
                or CampaignPreviewLayers.Zeppelins)
            {
                var arrow = new ImmediateMesh();
                arrow.SurfaceBegin(Mesh.PrimitiveType.Lines, material);
                arrow.SurfaceAddVertex(Vector3.Zero);
                arrow.SurfaceAddVertex(new Vector3(0f, 0f, -3f));
                arrow.SurfaceAddVertex(new Vector3(0f, 0f, -3f));
                arrow.SurfaceAddVertex(new Vector3(-0.5f, 0f, -2f));
                arrow.SurfaceAddVertex(new Vector3(0f, 0f, -3f));
                arrow.SurfaceAddVertex(new Vector3(0.5f, 0f, -2f));
                arrow.SurfaceEnd();
                handle.AddChild(new MeshInstance3D
                {
                    Mesh = arrow,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                });
            }

            _handles.Add((item, handle));
        }
    }

    private void RebuildRows()
    {
        if (_model == null)
        {
            return;
        }

        _rows.Clear();
        _itemList.Clear();
        int selected = -1;
        foreach (CampaignPreviewLayers layer in LayerOrder)
        {
            foreach (CampaignPreviewItem item in _model.Items(layer))
            {
                if (_model.Selection is { } selection
                    && selection.Layer == layer
                    && CampaignMissionPreview.SameSource(selection.Source, item.Source))
                {
                    selected = _rows.Count;
                }

                _rows.Add(item);
                _itemList.AddItem($"{layer}: {item.Name}{(item.Enabled ? "" : " (inactive)")}");
            }
        }

        if (selected >= 0)
        {
            _itemList.Select(selected);
            Select(selected);
        }
        else
        {
            _details.Text = "";
            _frame.Disabled = true;
            _showRaw.Disabled = true;
        }
    }

    private void Select(int index)
    {
        if (_model == null || (uint)index >= (uint)_rows.Count || !_model.Select(_rows[index]))
        {
            return;
        }

        CampaignPreviewItem item = _rows[index];
        string source = SourceText(item.Source);
        string related = item.RelatedSource == null ? "" : $"\nRelated: {SourceText(item.RelatedSource)}";
        string additional = item.AdditionalSources.IsDefaultOrEmpty
            ? ""
            : string.Concat(item.AdditionalSources.Select(extra => $"\nRelated: {SourceText(extra)}"));
        _details.Text = $"{item.Name}\n{source}{related}{additional}\n{item.Note}";
        _frame.Disabled = false;
        _showRaw.Disabled = MissionSource(item) == null;
        ScaleHandles();
    }

    private void FrameSelection()
    {
        if (_model?.Selection is not { } selection)
        {
            return;
        }

        CampaignPreviewItem? item = _rows.FirstOrDefault(row =>
            row.Layer == selection.Layer && CampaignMissionPreview.SameSource(row.Source, selection.Source));
        if (item == null)
        {
            return;
        }

        _focus = item.Pose.Origin;
        MoveCamera();
    }

    private void ShowMissionSource()
    {
        if (_model?.Selection is not { } selection)
        {
            return;
        }

        CampaignPreviewItem? item = _rows.FirstOrDefault(row =>
            row.Layer == selection.Layer && CampaignMissionPreview.SameSource(row.Source, selection.Source));
        CampaignPreviewSource? source = item == null ? null : MissionSource(item);
        if (source != null)
        {
            ShowSource?.Invoke(source);
        }
    }

    private void ViewportInput(InputEvent input)
    {
        if (_camera == null)
        {
            return;
        }

        switch (input)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Right } right:
                _orbiting = right.Pressed;
                break;
            case InputEventMouseMotion motion when _orbiting:
                _azimuth -= motion.Relative.X * 0.008f;
                _elevation = Mathf.Clamp(_elevation + (motion.Relative.Y * 0.008f), -1.45f, 1.45f);
                MoveCamera();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
                _distance = Mathf.Clamp(_distance * 0.8f, 10f, 50000f);
                MoveCamera();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
                _distance = Mathf.Clamp(_distance / 0.8f, 10f, 50000f);
                MoveCamera();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } left:
                Pick(left.Position);
                break;
        }
    }

    private void Pick(Vector2 position)
    {
        if (_camera == null)
        {
            return;
        }

        float best = 18f * 18f;
        CampaignPreviewItem? picked = null;
        foreach ((CampaignPreviewItem item, Node3D handle) in _handles)
        {
            if (_camera.IsPositionBehind(handle.GlobalPosition))
            {
                continue;
            }

            float distance = _camera.UnprojectPosition(handle.GlobalPosition).DistanceSquaredTo(position);
            if (distance < best)
            {
                best = distance;
                picked = item;
            }
        }

        if (picked == null)
        {
            return;
        }

        int index = _rows.FindIndex(row => row.Layer == picked.Layer
            && CampaignMissionPreview.SameSource(row.Source, picked.Source));
        if (index >= 0)
        {
            _itemList.Select(index);
            Select(index);
        }
    }

    private void MoveCamera()
    {
        if (_camera == null)
        {
            return;
        }

        float horizontal = Mathf.Cos(_elevation);
        Vector3 direction = new(
            Mathf.Sin(_azimuth) * horizontal,
            Mathf.Sin(_elevation),
            Mathf.Cos(_azimuth) * horizontal);
        _camera.GlobalPosition = _focus + (direction * _distance);
        _camera.LookAt(_focus, Vector3.Up);
        ScaleHandles();
    }

    private void ScaleHandles()
    {
        if (_camera == null)
        {
            return;
        }

        foreach ((CampaignPreviewItem item, Node3D handle) in _handles)
        {
            float size = Mathf.Clamp(_camera.GlobalPosition.DistanceTo(handle.GlobalPosition) * 0.012f, 2f, 600f);
            if (_model?.Selection is { } selected && selected.Layer == item.Layer
                && CampaignMissionPreview.SameSource(selected.Source, item.Source))
            {
                size *= 1.5f;
            }

            handle.Scale = Vector3.One * size;
        }
    }

    private void ClosePreview()
    {
        _model?.Dispose();
        _model = null;
        _world?.Dispose();
        _world = null;
        if (_viewport != null && GodotObject.IsInstanceValid(_viewport))
        {
            _viewport.Free();
        }

        _viewport = null;
        _overlays = null;
        _camera = null;
        _layers.Clear();
        _handles.Clear();
        _rows.Clear();
        _orbiting = false;
        if (_toggle != null)
        {
            _toggle.Text = "Open read-only 3D preview";
            _viewportHost.Visible = false;
            _itemList.Clear();
            _itemList.Visible = false;
            _details.Text = "";
            _frame.Disabled = true;
            _showRaw.Disabled = true;
        }
    }

    private void Fail(Exception error)
    {
        ClosePreview();
        _status.Text = $"Preview unavailable: {error.Message}";
        Log.Warn("ui", $"campaign preview: {error.Message}");
    }

}
