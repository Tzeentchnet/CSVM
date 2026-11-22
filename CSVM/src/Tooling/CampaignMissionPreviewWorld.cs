using System;
using System.Collections.Generic;
using CSVM.Effects;
using CSVM.Mech3;
using CSVM.Mech3.Anim;
using CSVM.Session.Objectives;
using Godot;

namespace CSVM.Tooling;

/// <summary>Disposable editor-only chapter world and runtime-style name and setup resolution.</summary>
internal sealed class CampaignMissionPreviewWorld : ICampaignPreviewWorld, IDisposable
{
    private readonly TextureArchive _textures;
    private readonly Node3D _root;
    private readonly NameResolver<Node3D> _names;
    private readonly ShaderGlobals _globals;
    private readonly object _shaderOwner;
    private readonly Dictionary<ulong, RestPose> _placed = new();
    private bool _disposed;

    private CampaignMissionPreviewWorld(
        TextureArchive textures, Node3D root, NameResolver<Node3D> names, ShaderGlobals globals, object shaderOwner)
    {
        _textures = textures;
        _root = root;
        _names = names;
        _globals = globals;
        _shaderOwner = shaderOwner;
    }

    public static CampaignMissionPreviewWorld Build(
        SubViewport viewport, CampaignMissionDependencyBundle bundle)
    {
        if (bundle.World.Availability.State != CampaignDependencyState.Ready
            || bundle.World.Value is not { } world)
        {
            throw new InvalidOperationException("The selected chapter world is unavailable.");
        }
        if (bundle.Textures.Availability.State != CampaignDependencyState.Ready)
        {
            throw new InvalidOperationException("The selected chapter texture archive is unavailable.");
        }

        var globals = ShaderGlobals.Acquire();
        TextureArchive? textures = null;
        WorldBuilder? builder = null;
        Node3D? root = null;
        var shaderOwner = new object();
        object? previousOwner = ShaderTwins.BuildingWorld;
        try
        {
            ShaderTwins.BuildingWorld = shaderOwner;
            textures = new TextureArchive(bundle.Textures.Value);
            MissionSetup? setup = bundle.Setup.Availability.State == CampaignDependencyState.Ready
                ? bundle.Setup.Value : null;
            builder = new WorldBuilder(
                world, textures, collision: false, scrollOverrides: setup?.ScrollByModel(world));
            root = builder.Build();
            root.AddChild(builder.Cycler);
            viewport.AddChild(root);

            var names = new NameResolver<Node3D>(NodeIdentity.Instance, isLive: GodotObject.IsInstanceValid);
            Index(root, root.GetParent() as Node3D, names);
            if (setup != null)
            {
                var rest = new Dictionary<ulong, Vector3>();
                Vector3 RestScale(Node3D node)
                {
                    ulong id = node.GetInstanceId();
                    if (!rest.TryGetValue(id, out Vector3 scale))
                    {
                        scale = node.Basis.Scale;
                        rest.Add(id, scale);
                    }

                    return scale;
                }

                setup.BindPartitions(world);
                setup.Apply(
                    (name, scope) => names.FindAll(name, scope),
                    AnimRuntime.SetSubtreeActive,
                    (node, position) =>
                    {
                        _ = RestScale(node);
                        node.Position = position;
                    },
                    (node, radians) => node.Basis =
                        Basis.FromEuler(radians, EulerOrder.Yxz).Scaled(RestScale(node)),
                    (index, active) =>
                    {
                        if (names.ByGamezIndex(index) is { } node)
                        {
                            AnimRuntime.SetSubtreeActive(node, active);
                        }
                    });
            }

            return new CampaignMissionPreviewWorld(textures, root, names, globals, shaderOwner);
        }
        catch
        {
            if (root != null && GodotObject.IsInstanceValid(root))
            {
                root.Free();
            }

            if (builder != null && GodotObject.IsInstanceValid(builder.Cycler))
            {
                builder.Cycler.Free();
            }

            textures?.Dispose();
            ShaderTwins.Untrack(shaderOwner);
            globals.Dispose();
            throw;
        }
        finally
        {
            ShaderTwins.BuildingWorld = previousOwner;
        }
    }

    public bool TryPose(string name, out Transform3D pose)
    {
        Node3D? node = First(name, null);
        pose = node?.GlobalTransform ?? Transform3D.Identity;
        return node != null && pose.Origin.IsFinite()
            && pose.Basis.X.IsFinite() && pose.Basis.Y.IsFinite() && pose.Basis.Z.IsFinite();
    }

    public bool TryTarget(IReadOnlyList<string> path, out Vector3 position)
    {
        Node3D? current = null;
        foreach (string segment in path)
        {
            current = First(segment, current);
            if (current == null)
            {
                position = default;
                return false;
            }
        }

        position = current == null ? default : ObjectiveSites.SiteAnchor(current);
        return current != null && position.IsFinite();
    }

    public bool TryGenerator(
        string host,
        string? origin,
        bool zeppelin,
        out Transform3D hostPose,
        out Vector3 offset,
        out string note)
    {
        hostPose = Transform3D.Identity;
        offset = Vector3.Zero;
        Node3D? node = First(host, null);
        if (node == null)
        {
            note = "The generator host is not built in this static world.";
            return false;
        }

        hostPose = node.GlobalTransform;
        float determinant = hostPose.Basis.Determinant();
        if (!hostPose.Origin.IsFinite() || !float.IsFinite(determinant) || Mathf.Abs(determinant) < 1e-9f)
        {
            note = "The generator host has an unusable transform.";
            return false;
        }

        if (zeppelin)
        {
            Node3D? child = origin == null ? null : First(origin, node);
            if (child != null)
            {
                offset = hostPose.AffineInverse() * child.GlobalPosition;
                note = "Origin is resolved under the hull in its post-setup host frame; net eligibility is not simulated.";
            }
            else
            {
                note = origin == null
                    ? "No origin is authored; the runtime falls back to the host. Net eligibility is not simulated."
                    : "Origin is unresolved under the host; the runtime falls back to the host. Net eligibility is not simulated.";
            }

            return offset.IsFinite();
        }

        Node3D? first = First(EnemyGenerators.LaunchPathNode(host, 0), node);
        Node3D? second = First(EnemyGenerators.LaunchPathNode(host, 1), node);
        if (first == null || second == null)
        {
            note = "The two host-scoped surface launch path nodes are not built.";
            return false;
        }

        offset = hostPose.AffineInverse() * first.GlobalPosition;
        note = "First of at least two host-scoped launch points, with the runtime's world-up lift; net eligibility is not simulated.";
        return offset.IsFinite();
    }

    public void PlaceZeppelins(IReadOnlyList<CampaignPreviewItem> zeppelins)
    {
        foreach (RestPose rest in _placed.Values)
        {
            if (GodotObject.IsInstanceValid(rest.Node))
            {
                rest.Node.Transform = rest.Local;
                rest.Node.Visible = rest.Visible;
            }
        }

        _placed.Clear();
        foreach (CampaignPreviewItem item in zeppelins)
        {
            Node3D? host = First(item.Name, null);
            if (host == null)
            {
                continue;
            }

            _placed.TryAdd(host.GetInstanceId(), new RestPose(host, host.Transform, host.Visible));
            host.Visible = true;
            host.GlobalTransform = item.Pose;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _placed.Clear();
        if (GodotObject.IsInstanceValid(_root))
        {
            _root.Free();
        }

        _textures.Dispose();
        ShaderTwins.Untrack(_shaderOwner);
        _globals.Dispose();
    }

    private static void Index(Node3D node, Node3D? parent, NameResolver<Node3D> names)
    {
        string source = node.HasMeta(AnimRuntime.NameMeta)
            ? node.GetMeta(AnimRuntime.NameMeta).AsString()
            : node.Name.ToString();
        int? index = node.HasMeta(AnimRuntime.IndexMeta)
            ? node.GetMeta(AnimRuntime.IndexMeta).AsInt32()
            : null;
        names.Add(node, source, parent, index);
        for (int i = 0; i < node.GetChildCount(); i++)
        {
            if (node.GetChild(i) is Node3D child)
            {
                Index(child, node, names);
            }
        }
    }

    private Node3D? First(string name, Node3D? scope)
    {
        List<Node3D> found = _names.FindAll(name, scope);
        return found.Count == 0 ? null : found[0];
    }

    private sealed record RestPose(Node3D Node, Transform3D Local, bool Visible);

    private sealed class NodeIdentity : IEqualityComparer<Node3D>
    {
        internal static readonly NodeIdentity Instance = new();

        public bool Equals(Node3D? a, Node3D? b) =>
            (a?.GetInstanceId() ?? 0) == (b?.GetInstanceId() ?? 0);

        public int GetHashCode(Node3D node) => node.GetInstanceId().GetHashCode();
    }

    // Launcher registers these for a running game, but the editor process does not run Launcher.
    // Each preview shares the editor-only registrations and removes only those it added.
    private sealed class ShaderGlobals : IDisposable
    {
        private static readonly List<string> Added = new();
        private static int _users;
        private bool _active;

        public static ShaderGlobals Acquire()
        {
            var lease = new ShaderGlobals();
            if (!Engine.IsEditorHint())
            {
                return lease;
            }

            if (_users == 0)
            {
                try
                {
                    Register();
                }
                catch
                {
                    RemoveAdded();
                    throw;
                }
            }

            _users++;
            lease._active = true;
            return lease;
        }

        public void Dispose()
        {
            if (!_active)
            {
                return;
            }

            _active = false;
            if (--_users == 0)
            {
                RemoveAdded();
            }
        }

        private static void Register()
        {
            var present = new HashSet<string>(StringComparer.Ordinal);
            foreach (var name in RenderingServer.GlobalShaderParameterGetList())
            {
                present.Add(name.ToString());
            }

            void Add(string name, RenderingServer.GlobalShaderParameterType type, Variant value)
            {
                if (present.Add(name))
                {
                    RenderingServer.GlobalShaderParameterAdd(name, type, value);
                    Added.Add(name);
                }
            }

            void Many(RenderingServer.GlobalShaderParameterType type, Variant value, params string[] names)
            {
                foreach (string name in names)
                {
                    Add(name, type, value);
                }
            }

            Add("csky_fog_color", RenderingServer.GlobalShaderParameterType.Vec3, new Vector3(0.69f, 0.69f, 0.69f));
            Many(RenderingServer.GlobalShaderParameterType.Vec2, new Vector2(1e8f, 1e9f),
                "csky_fog_range", "csky_fog_alt");
            Many(RenderingServer.GlobalShaderParameterType.Float, 1f, "csky_world_light");
            Many(RenderingServer.GlobalShaderParameterType.Vec3, Vector3.One,
                "csky_tune_sky_tint", "csky_tune_cloud_tint", "csky_vegetation_light",
                "csky_sun_ambient_rgb", "csky_sun_fill_rgb");
            Many(RenderingServer.GlobalShaderParameterType.Vec3, Vector3.Zero,
                "csky_gi_ambient_fill", "csky_sun_diffuse_rgb", "csky_cloud_shadow_tint");
            Add("csky_sun_dir", RenderingServer.GlobalShaderParameterType.Vec3, Vector3.Up);
            Add("csky_sun_light", RenderingServer.GlobalShaderParameterType.Vec2, new Vector2(1f, 0f));
            Many(RenderingServer.GlobalShaderParameterType.Float, 0f,
                "csky_gi_self_lit", "csky_mip_bias", "csky_time", "csky_ocean_on",
                "csky_cloud_shadow_ambient", "csky_terrain_shadow_strength",
                "csky_city_ground", "csky_clutter_fade_scale_sq");
            Many(RenderingServer.GlobalShaderParameterType.Int, 0,
                "csky_view_count", "csky_light_count", "csky_aircraft_light_count", "csky_fog_volume_count");
            for (int i = 0; i < 8; i++)
            {
                Add($"csky_view_{i}", RenderingServer.GlobalShaderParameterType.Mat4, Projection.Zero);
                Add($"csky_view_sun_{i}", RenderingServer.GlobalShaderParameterType.Mat4, Projection.Zero);
            }

            Many(RenderingServer.GlobalShaderParameterType.Sampler2D, default,
                "csky_light_data", "csky_aircraft_light_data", "csky_fog_volume_data",
                "csky_terrain_shadow", "csky_ocean_mask", "csky_ocean_zone", "csky_ocean_inland",
                "csky_pc_tops_lat");
            Many(RenderingServer.GlobalShaderParameterType.Sampler3D, default,
                "csky_pc_shape_tex", "csky_pc_warp_lat");
            Add("csky_terrain_shadow_rect", RenderingServer.GlobalShaderParameterType.Vec4, new Vector4(0f, 0f, 1f, 1f));
            Add("csky_terrain_shadow_soft", RenderingServer.GlobalShaderParameterType.Vec2, new Vector2(0f, 1f));
            Add("csky_cloud_shadow_edge", RenderingServer.GlobalShaderParameterType.Vec2, CloudShadowPattern.Edge);
            Add("csky_cloud_shadow_band", RenderingServer.GlobalShaderParameterType.Vec2, Vector2.Zero);
            Add("csky_cloud_shadow", RenderingServer.GlobalShaderParameterType.Vec4, CloudShadow.OffValue);
            Add("csky_pc_ground_sun", RenderingServer.GlobalShaderParameterType.Vec4, new Vector4(0f, 1f, 0f, 0f));
            Many(RenderingServer.GlobalShaderParameterType.Vec4, Vector4.Zero,
                "csky_pc_fog", "csky_ocean_rect", "csky_ocean_surf",
                "csky_pc_ground", "csky_pc_heads_shape", "csky_pc_heads_layer",
                "csky_pc_heads_density", "csky_pc_pw", "csky_pc_pw_deck", "csky_pc_veil",
                "csky_pc_bubble0", "csky_pc_bubble1", "csky_pc_bubble2", "csky_pc_bubble3");
        }

        private static void RemoveAdded()
        {
            foreach (string name in Added)
            {
                RenderingServer.GlobalShaderParameterRemove(name);
            }

            Added.Clear();
        }
    }
}
