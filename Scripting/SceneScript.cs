using rrr.Core;
using rrr.Imaging;
using rrr.Importers;
using rrr.Rendering;
using rrr.Scene;
using rrr.VMath;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace rrr.Scripting;

/// <summary>
/// Imperative scene scripting language (.rrr). One command per line:
///   command [subtype] key=value key=value ...
/// '#' starts a comment. Every argument has a default. If no 'render' command
/// runs, the scene is rendered at end-of-file to '&lt;scriptname&gt;.bmp'.
/// </summary>
public sealed class SceneScript
{
    private readonly string _scriptPath;
    private readonly string _scriptDirectory;

    private readonly Scene.Scene _scene = new Scene.Scene();
    private readonly Dictionary<string, List<SceneObject>> _objects =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Material> _materials =
        new(StringComparer.OrdinalIgnoreCase);

    private RenderSettings _settings = RenderSettings.Flat();
    private Matrix4x4f _world = Matrix4x4f.Identity;
    private ColorRGBAf _background = ColorRGBAf.Black;

    private int _width = 1920;
    private int _height = 1080;

    private string? _lastObject;
    private string? _lastMaterial;
    private int _autoCounter;
    private bool _rendered;
    private double _loadMs;

    private MeshAuthoring? _authoring;

    /// <summary>
    /// In-progress hand-authored mesh, between <c>mesh begin</c> and
    /// <c>mesh end</c>. Each <c>vertex</c> appends a unified vertex (position +
    /// optional uv/normal sharing one index, OBJ-style); <c>face</c>/<c>quad</c>
    /// reference those by 1-based index (negatives are relative to the current
    /// count) and are fan-triangulated.
    /// </summary>
    private sealed class MeshAuthoring
    {
        public string Name = "";
        public string? MaterialName;
        public int Line;

        public readonly List<Vector3f> Positions = new();
        public readonly List<Vector2f?> Uvs = new();
        public readonly List<Vector3f?> Normals = new();
        public readonly List<ColorRGBAf?> Colors = new();
        public readonly List<(int A, int B, int C)> Tris = new();

        public bool AnyUv;
        public bool AnyNormal;
        public bool AnyColor;
    }

    private SceneScript(string scriptPath)
    {
        _scriptPath = scriptPath;
        _scriptDirectory =
            Path.GetDirectoryName(Path.GetFullPath(scriptPath)) ?? "";
    }

    public static void Run(string scriptPath)
    {
        SceneScript script = new SceneScript(scriptPath);
        script.Execute();
    }

    private void Execute()
    {
        string[] lines = File.ReadAllLines(_scriptPath);

        for (int i = 0; i < lines.Length; i++)
        {
            int lineNumber = i + 1;
            string line = StripComment(lines[i]);

            // Backslash line continuation: a line whose content ends with '\'
            // is joined with the following physical line(s). Errors keep the
            // first physical line's number.
            while (EndsWithContinuation(line) && i + 1 < lines.Length)
            {
                line = line.TrimEnd();
                line = line.Substring(0, line.Length - 1); // drop the trailing '\'
                line += " " + StripComment(lines[++i]);
            }

            line = line.Trim();

            if (line.Length == 0)
                continue;

            List<string> tokens = Tokenize(line);
            if (tokens.Count == 0)
                continue;

            string command = tokens[0].ToLowerInvariant();
            string? subType = null;

            Dictionary<string, string> map =
                new(StringComparer.OrdinalIgnoreCase);

            for (int t = 1; t < tokens.Count; t++)
            {
                string token = tokens[t];
                int eq = token.IndexOf('=');

                if (eq < 0)
                {
                    // First bare token is the subtype (obj, cube, point, ...).
                    subType ??= token.ToLowerInvariant();
                }
                else
                {
                    string key = token.Substring(0, eq).ToLowerInvariant();
                    string value = token.Substring(eq + 1);
                    map[key] = value;
                }
            }

            ScriptArguments args = new ScriptArguments(map, lineNumber);

            Dispatch(command, subType, args);
        }

        if (_authoring != null)
            throw new ScriptException(_authoring.Line,
                $"mesh '{_authoring.Name}' opened with 'mesh begin' was never closed with 'mesh end'.");

        if (!_rendered)
            RenderToFile(DefaultOutputFile());
    }

    private void Dispatch(string command, string? subType, ScriptArguments args)
    {
        switch (command)
        {
            case "rendering": DoRendering(args); break;
            case "load": DoLoad(subType, args); break;
            case "mesh": DoMesh(subType, args); break;
            case "vertex": DoVertex(args); break;
            case "face": DoFace(args); break;
            case "quad": DoQuad(args); break;
            case "translate": DoTransform(command, subType, args); break;
            case "rotate": DoTransform(command, subType, args); break;
            case "scale": DoTransform(command, subType, args); break;
            case "material": DoMaterial(args); break;
            case "assign": DoAssign(args); break;
            case "camera": DoCamera(subType, args); break;
            case "light": DoLight(subType, args); break;
            case "render": DoRender(args); break;
            case "listcameras": DoListCameras(); break;
            case "listobjects": DoListObjects(); break;
            default:
                throw new ScriptException(args.Line, $"Unknown command '{command}'.");
        }
    }

    //
    // Commands
    //

    private void DoRendering(ScriptArguments args)
    {
        _settings = new RenderSettings
        {
            ShadingMode = args.GetEnum("shading", _settings.ShadingMode),
            Parallel = args.GetInt("parallel", 0),
            BackfaceCulling = args.GetBool("backface", _settings.BackfaceCulling),
            ShadowsEnabled = args.GetBool("shadows", _settings.ShadowsEnabled),
            ShadowMapResolution = args.GetInt("shadowres", _settings.ShadowMapResolution),
            ShadowPcfRadius = args.GetInt("shadowpcf", _settings.ShadowPcfRadius),
            ShadowFrontFaceCull = args.GetBool("shadowcull", _settings.ShadowFrontFaceCull),
            ShadowSoftness = args.GetFloat("shadowsoft", _settings.ShadowSoftness)
        };

        _width = args.GetInt("width", _width);
        _height = args.GetInt("height", _height);
        _background = args.GetColor("background", _background);
    }

    private static readonly HashSet<string> LoadSubtypes =
        new(StringComparer.OrdinalIgnoreCase) { "obj", "stl", "gltf", "glb", "model" };

    private void DoLoad(string? subType, ScriptArguments args)
    {
        subType ??= "obj";

        if (!LoadSubtypes.Contains(subType))
            throw new ScriptException(args.Line, $"Unsupported load type '{subType}'.");

        string file = args.GetString("file", "");

        if (file.Length == 0)
            throw new ScriptException(args.Line, $"load {subType} requires file=...");

        string path = ResolvePath(file);
        string name = args.GetString("name", Path.GetFileNameWithoutExtension(file));

        // Dispatch by file extension; the subtype is just a readability hint.
        System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
        Scene.Scene loaded = ModelImporter.Load(path);
        sw.Stop();
        _loadMs += sw.Elapsed.TotalMilliseconds;

        List<SceneObject> added = new List<SceneObject>();

        foreach (SceneObject obj in loaded.Objects)
        {
            _scene.Objects.Add(obj);
            added.Add(obj);

            if (obj.Material != null && !_scene.Materials.Contains(obj.Material))
            {
                _scene.Materials.Add(obj.Material);
                if (!string.IsNullOrEmpty(obj.Material.Name))
                    _materials[obj.Material.Name] = obj.Material;
            }
        }

        // Optional generated UVs (box / triplanar projection) for meshes that
        // have none — e.g. texturing an STL part. uv=box, optional uvscale.
        string uvMode = args.GetString("uv", "none").ToLowerInvariant();
        if (uvMode is "box" or "planar" or "triplanar")
        {
            float uvScale = args.GetFloat("uvscale", 1.0f);
            foreach (SceneObject obj in added)
                if (obj.Mesh != null && obj.Mesh.Positions.Count > 0)
                    MeshUtils.GenerateBoxUVs(obj.Mesh, uvScale);
        }

        // Merge cameras and lights defined inside the model (e.g. glTF). An
        // imported camera becomes active (a later script `camera` overrides it);
        // imported lights coexist with any script lights.
        foreach (Camera camera in loaded.Cameras)
            RegisterCamera(camera);

        foreach (Light light in loaded.Lights)
            _scene.Lights.Add(light);

        RegisterObjects(name, added);
        ApplyOptionalMaterial(added, args);
    }

    private void DoMesh(string? subType, ScriptArguments args)
    {
        subType ??= "cube";

        if (subType == "begin")
        {
            BeginAuthoring(args);
            return;
        }

        if (subType == "end")
        {
            EndAuthoring(args);
            return;
        }

        Mesh mesh = subType switch
        {
            "plane" => Primitives.Plane(
                args.GetVector2("size", new Vector2f(1, 1)).X,
                args.GetVector2("size", new Vector2f(1, 1)).Y),

            "cube" => CubeFromArgs(args),

            "sphere" => Primitives.Sphere(
                args.GetFloat("radius", 1f),
                args.GetInt("segments", 32),
                args.GetInt("rings", 16)),

            "cylinder" => Primitives.Cylinder(
                args.GetFloat("radius", 0.5f),
                args.GetFloat("height", 2f),
                args.GetInt("segments", 32)),

            "cone" => Primitives.Cone(
                args.GetFloat("radius", 0.5f),
                args.GetFloat("height", 2f),
                args.GetInt("segments", 32)),

            "pyramid" => Primitives.Pyramid(
                args.GetVector2("base", new Vector2f(1, 1)).X,
                args.GetVector2("base", new Vector2f(1, 1)).Y,
                args.GetFloat("height", 1.5f)),

            _ => throw new ScriptException(args.Line, $"Unknown primitive '{subType}'.")
        };

        string name = args.GetString("name", $"{subType}{++_autoCounter}");

        Material material = CreateDefaultMaterial(name);

        SceneObject obj = new SceneObject
        {
            Name = name,
            Mesh = mesh,
            Material = material
        };

        _scene.Objects.Add(obj);
        RegisterObjects(name, new List<SceneObject> { obj });
        ApplyOptionalMaterial(new List<SceneObject> { obj }, args);
    }

    private static Mesh CubeFromArgs(ScriptArguments args)
    {
        Vector3f size = args.GetVector3("size", new Vector3f(1, 1, 1));
        return Primitives.Cube(size.X, size.Y, size.Z);
    }

    //
    // Hand-authored mesh: mesh begin / vertex / face / quad / mesh end
    //

    private void BeginAuthoring(ScriptArguments args)
    {
        if (_authoring != null)
            throw new ScriptException(args.Line,
                "'mesh begin' while another mesh is still open (missing 'mesh end').");

        _authoring = new MeshAuthoring
        {
            Name = args.GetString("name", $"mesh{++_autoCounter}"),
            MaterialName = args.Has("material") ? args.GetString("material", "") : null,
            Line = args.Line
        };
    }

    private void DoVertex(ScriptArguments args)
    {
        MeshAuthoring a = RequireAuthoring(args, "vertex");

        if (!args.Has("pos"))
            throw new ScriptException(args.Line, "vertex requires pos=x,y,z.");

        a.Positions.Add(args.GetVector3("pos", Vector3f.Zero));

        if (args.Has("uv"))
        {
            a.Uvs.Add(args.GetVector2("uv", new Vector2f(0, 0)));
            a.AnyUv = true;
        }
        else
        {
            a.Uvs.Add(null);
        }

        if (args.Has("normal"))
        {
            a.Normals.Add(args.GetVector3("normal", Vector3f.Zero));
            a.AnyNormal = true;
        }
        else
        {
            a.Normals.Add(null);
        }

        if (args.Has("color"))
        {
            a.Colors.Add(args.GetColor("color", ColorRGBAf.White));
            a.AnyColor = true;
        }
        else
        {
            a.Colors.Add(null);
        }
    }

    private void DoFace(ScriptArguments args)
    {
        MeshAuthoring a = RequireAuthoring(args, "face");
        AddPolygon(a, ParseIndexList(args), args.Line);
    }

    private void DoQuad(ScriptArguments args)
    {
        MeshAuthoring a = RequireAuthoring(args, "quad");

        int[] v = ParseIndexList(args);
        if (v.Length != 4)
            throw new ScriptException(args.Line, $"quad expects 4 indices, got {v.Length}.");

        AddPolygon(a, v, args.Line);
    }

    private static void AddPolygon(MeshAuthoring a, int[] indices, int line)
    {
        if (indices.Length < 3)
            throw new ScriptException(line, "face/quad needs at least 3 vertices.");

        // 1-based, negatives relative to the current vertex count (OBJ style).
        int count = a.Positions.Count;
        int[] resolved = new int[indices.Length];

        for (int i = 0; i < indices.Length; i++)
        {
            int idx = indices[i];
            int zero = idx > 0 ? idx - 1 : count + idx;

            if (idx == 0 || zero < 0 || zero >= count)
                throw new ScriptException(line,
                    $"face index {idx} is out of range (1..{count}).");

            resolved[i] = zero;
        }

        // Fan triangulation. Author CCW seen from outside (no auto-flip).
        for (int i = 1; i < resolved.Length - 1; i++)
            a.Tris.Add((resolved[0], resolved[i], resolved[i + 1]));
    }

    private void EndAuthoring(ScriptArguments args)
    {
        MeshAuthoring a = RequireAuthoring(args, "mesh end");
        bool smooth = args.GetBool("smooth", false);

        Mesh mesh = FinalizeAuthoredMesh(a, smooth);
        _authoring = null;

        SceneObject obj = new SceneObject
        {
            Name = a.Name,
            Mesh = mesh,
            Material = CreateDefaultMaterial(a.Name)
        };

        _scene.Objects.Add(obj);
        RegisterObjects(a.Name, new List<SceneObject> { obj });

        if (a.MaterialName != null)
        {
            Material material = GetOrCreateMaterial(a.MaterialName);
            obj.Material = material;
            _lastMaterial = material.Name;
        }
    }

    private static Mesh FinalizeAuthoredMesh(MeshAuthoring a, bool smooth)
    {
        Mesh mesh = new Mesh();
        mesh.Positions.AddRange(a.Positions);

        if (a.AnyUv)
            for (int i = 0; i < a.Uvs.Count; i++)
                mesh.UVs.Add(a.Uvs[i] ?? new Vector2f(0, 0));

        // Per-vertex colors: if any vertex set one, fill the rest with white.
        if (a.AnyColor)
            for (int i = 0; i < a.Colors.Count; i++)
                mesh.Colors.Add(a.Colors[i] ?? ColorRGBAf.White);

        // Use explicit per-vertex normals only if every vertex supplied one and
        // smoothing was not requested; otherwise smooth or fall back to the
        // renderer's geometric face normal (N = -1).
        bool explicitNormals =
            !smooth && a.AnyNormal && a.Normals.TrueForAll(n => n.HasValue);

        if (explicitNormals)
            for (int i = 0; i < a.Normals.Count; i++)
                mesh.Normals.Add(a.Normals[i]!.Value.Normalized());

        foreach (var (x, y, z) in a.Tris)
        {
            int uvX = a.AnyUv ? x : -1;
            int uvY = a.AnyUv ? y : -1;
            int uvZ = a.AnyUv ? z : -1;

            int nX = explicitNormals ? x : -1;
            int nY = explicitNormals ? y : -1;
            int nZ = explicitNormals ? z : -1;

            mesh.Triangles.Add(
                new Triangle(x, uvX, nX, y, uvY, nY, z, uvZ, nZ));
        }

        if (smooth && mesh.Positions.Count > 0)
            MeshUtils.GenerateNormals(mesh);

        return mesh;
    }

    private MeshAuthoring RequireAuthoring(ScriptArguments args, string command)
    {
        if (_authoring == null)
            throw new ScriptException(args.Line,
                $"'{command}' is only valid between 'mesh begin' and 'mesh end'.");

        return _authoring;
    }

    /// <summary>Parses the <c>v=</c> comma-separated 1-based index list.</summary>
    private static int[] ParseIndexList(ScriptArguments args)
    {
        string raw = args.GetString("v", "");

        if (raw.Length == 0)
            throw new ScriptException(args.Line, "face/quad requires v=i,j,k...");

        string[] parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries);
        int[] result = new int[parts.Length];

        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i].Trim(),
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out result[i]))
                throw new ScriptException(args.Line, $"face index '{parts[i]}' is not an integer.");
        }

        return result;
    }

    private void DoTransform(string command, string? subType, ScriptArguments args)
    {
        Matrix4x4f m = command switch
        {
            "translate" => Matrix4x4f.CreateTranslation(
                args.GetVector3("by", Vector3f.Zero)),

            "scale" => Matrix4x4f.CreateScale(
                args.GetScale("by", Vector3f.One)),

            "rotate" => RotationMatrix(args),

            _ => Matrix4x4f.Identity
        };

        bool world =
            string.Equals(subType, "world", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(args.GetString("target", ""), "world", StringComparison.OrdinalIgnoreCase);

        if (world)
        {
            _world = m * _world;
            return;
        }

        string name = args.GetString("name", _lastObject ?? "");

        foreach (SceneObject obj in ResolveObjects(name, args.Line))
            obj.Transform = m * obj.Transform;
    }

    private static Matrix4x4f RotationMatrix(ScriptArguments args)
    {
        if (args.Has("euler"))
        {
            Vector3f e = args.GetVector3("euler", Vector3f.Zero);
            float rx = Deg(e.X), ry = Deg(e.Y), rz = Deg(e.Z);

            return Matrix4x4f.CreateRotationZ(rz) *
                   Matrix4x4f.CreateRotationY(ry) *
                   Matrix4x4f.CreateRotationX(rx);
        }

        string axis = args.GetString("axis", "y").ToLowerInvariant();
        float angle = Deg(args.GetFloat("angle", 0f));

        return axis switch
        {
            "x" => Matrix4x4f.CreateRotationX(angle),
            "y" => Matrix4x4f.CreateRotationY(angle),
            "z" => Matrix4x4f.CreateRotationZ(angle),
            _ => throw new ScriptException(args.Line, $"Unknown axis '{axis}'.")
        };
    }

    private void DoMaterial(ScriptArguments args)
    {
        string name = args.GetString("name", $"material{++_autoCounter}");

        Material material = GetOrCreateMaterial(name);

        material.DiffuseColor = args.GetColor("diffuse", material.DiffuseColor);
        material.SpecularColor = args.GetColor("specular", material.SpecularColor);
        material.Shininess = args.GetFloat("shininess", material.Shininess);
        material.EmissiveColor = args.GetColor("emissive", material.EmissiveColor);

        if (args.Has("texture"))
            material.DiffuseTexture = LoadTextureArg(args.GetString("texture", ""));

        if (args.Has("specmap"))
            material.SpecularTexture = LoadTextureArg(args.GetString("specmap", ""));

        if (args.Has("emissivemap"))
        {
            material.EmissiveTexture = LoadTextureArg(args.GetString("emissivemap", ""));

            // Avoid multiplying the emissive map by a black color.
            if (material.EmissiveTexture != null &&
                material.EmissiveColor.R == 0 &&
                material.EmissiveColor.G == 0 &&
                material.EmissiveColor.B == 0)
            {
                material.EmissiveColor = ColorRGBAf.White;
            }
        }

        if (args.Has("normalmap"))
        {
            material.NormalTexture = LoadTextureArg(args.GetString("normalmap", ""));
            material.NormalIsHeightMap = false;
        }

        if (args.Has("heightmap"))
        {
            material.NormalTexture = LoadTextureArg(args.GetString("heightmap", ""));
            material.NormalIsHeightMap = true;
        }

        if (args.Has("occlusionmap"))
            material.OcclusionTexture = LoadTextureArg(args.GetString("occlusionmap", ""));

        material.BumpScale = args.GetFloat("bumpscale", material.BumpScale);
        material.InvertNormalGreen = args.GetBool("invertgreen", material.InvertNormalGreen);

        material.DoubleSided = args.GetBool("doublesided", material.DoubleSided);
        material.Unlit = args.GetBool("unlit", material.Unlit);
        material.AlphaMode = args.GetEnum("alphamode", material.AlphaMode);
        material.AlphaCutoff = args.GetFloat("alphacutoff", material.AlphaCutoff);

        _lastMaterial = name;
    }

    private Texture2D? LoadTextureArg(string file)
    {
        return file.Length > 0 ? Texture2D.TryLoad(ResolvePath(file)) : null;
    }

    private void DoAssign(ScriptArguments args)
    {
        string materialName = args.GetString("material", _lastMaterial ?? "");
        string objectName = args.GetString("to", _lastObject ?? "");

        Material material = GetOrCreateMaterial(materialName);

        foreach (SceneObject obj in ResolveObjects(objectName, args.Line))
            obj.Material = material;
    }

    private void DoCamera(string? subType, ScriptArguments args)
    {
        bool comfy =
            string.Equals(subType, "comfy", StringComparison.OrdinalIgnoreCase) ||
            !args.Any;

        Camera camera;

        if (comfy)
        {
            var (min, max) = _scene.GetBoundingBox();
            camera = Camera.CreateComfyCam(min, max);
            camera.Name = args.GetString("name", "comfy");
        }
        else
        {
            camera = new Camera
            {
                Name = args.GetString("name", $"camera{_scene.Cameras.Count + 1}"),
                Position = args.GetVector3("position", new Vector3f(0, 2, -5)),
                Target = args.GetVector3("target", Vector3f.Zero),
                Up = args.GetVector3("up", new Vector3f(0, 1, 0)),
                Fov = args.GetFloat("fov", 60f),
                Near = args.GetFloat("near", 0.1f),
                Far = args.GetFloat("far", 1000f)
            };
        }

        RegisterCamera(camera);
    }

    /// <summary>
    /// Adds the camera (or updates an existing one with the same name) and makes
    /// it the active camera. The most recently defined camera wins at render.
    /// </summary>
    private void RegisterCamera(Camera camera)
    {
        int existing = _scene.Cameras.FindIndex(
            c => string.Equals(c.Name, camera.Name, StringComparison.OrdinalIgnoreCase));

        if (existing >= 0)
            _scene.Cameras[existing] = camera;
        else
            _scene.Cameras.Add(camera);

        _scene.Camera = camera;
    }

    private void DoListCameras()
    {
        Console.WriteLine($"Cameras ({_scene.Cameras.Count}):");

        if (_scene.Cameras.Count == 0)
        {
            Console.WriteLine("  (none defined yet — a comfy camera will be auto-created)");
            return;
        }

        foreach (Camera c in _scene.Cameras)
        {
            string marker = ReferenceEquals(c, _scene.Camera) ? "*" : " ";
            Console.WriteLine(
                $"  {marker} {c.Name}: position={Fmt(c.Position)} target={Fmt(c.Target)} " +
                $"fov={Num(c.Fov)} near={Num(c.Near)} far={Num(c.Far)}");
        }
    }

    private static string Fmt(Vector3f v)
        => $"{Num(v.X)},{Num(v.Y)},{Num(v.Z)}";

    private static string Num(float value)
        => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private void DoListObjects()
    {
        Console.WriteLine($"Objects ({_scene.Objects.Count}):");

        if (_scene.Objects.Count == 0)
        {
            Console.WriteLine("  (none created yet)");
            return;
        }

        foreach (SceneObject obj in _scene.Objects)
        {
            int verts = obj.Mesh?.Positions.Count ?? 0;
            int tris = obj.Mesh?.Triangles.Count ?? 0;
            string material = obj.Material?.Name ?? "(none)";

            Console.WriteLine(
                $"  {obj.Name}: vertices={verts} triangles={tris} material={material}");
        }
    }

    private void DoLight(string? subType, ScriptArguments args)
    {
        subType ??= args.Any ? "point" : "comfy";

        Light light;

        switch (subType)
        {
            case "comfy":
                {
                    var (min, max) = _scene.GetBoundingBox();
                    light = PointLight.CreateComfyLight(min, max);
                    break;
                }

            case "point":
                {
                    var (min, max) = _scene.GetBoundingBox();
                    Vector3f defaultPos =
                        PointLight.CreateComfyLight(min, max).Position;

                    light = new PointLight(
                        args.GetVector3("position", defaultPos),
                        args.GetColor("color", ColorRGBAf.White),
                        args.GetFloat("intensity", 1f));
                    break;
                }

            case "directional":
                {
                    light = new DirectionalLight
                    {
                        Direction = args.GetVector3("direction", new Vector3f(-1, -1, -1)).Normalized(),
                        Color = args.GetColor("color", ColorRGBAf.White),
                        Intensity = args.GetFloat("intensity", 1f)
                    };
                    break;
                }

            case "spot":
                {
                    var (min, max) = _scene.GetBoundingBox();
                    Vector3f center = (min + max) * 0.5f;
                    Vector3f defaultPos =
                        PointLight.CreateComfyLight(min, max).Position;

                    Vector3f position = args.GetVector3("position", defaultPos);

                    // Default direction aims at the scene center.
                    Vector3f defaultDir = (center - position).Normalized();
                    if (defaultDir.LengthSquared() < 1e-8f)
                        defaultDir = new Vector3f(0, -1, 0);

                    float cone = args.GetFloat("cone", 40f);

                    light = new SpotLight
                    {
                        Position = position,
                        Direction = args.GetVector3("direction", defaultDir).Normalized(),
                        Color = args.GetColor("color", ColorRGBAf.White),
                        Intensity = args.GetFloat("intensity", 1f),
                        ConeAngleDegrees = cone,
                        InnerAngleDegrees = args.GetFloat("inner", cone * 0.8f)
                    };
                    break;
                }

            default:
                throw new ScriptException(args.Line, $"Unknown light type '{subType}'.");
        }

        light.CastsShadows = args.GetBool("shadows", true);
        _scene.Lights.Add(light);
    }

    private void DoRender(ScriptArguments args)
    {
        string file = args.GetString("file", DefaultOutputFile());
        _width = args.GetInt("width", _width);
        _height = args.GetInt("height", _height);

        RenderToFile(ResolvePath(file));
    }

    //
    // Rendering
    //

    private void RenderToFile(string file)
    {
        EnsureCameraAndLights();

        // Bake the world matrix into each object's transform for this render,
        // then restore so further commands keep operating on local transforms.
        Matrix4x4f[] saved = new Matrix4x4f[_scene.Objects.Count];

        for (int i = 0; i < _scene.Objects.Count; i++)
        {
            saved[i] = _scene.Objects[i].Transform;
            _scene.Objects[i].Transform = _world * saved[i];
        }

        rrr.App.SceneRenderer.Render(
            _scene, _settings, file, _width, _height, _background, _loadMs);

        for (int i = 0; i < _scene.Objects.Count; i++)
            _scene.Objects[i].Transform = saved[i];

        _rendered = true;
    }

    private void EnsureCameraAndLights()
    {
        var (min, max) = _scene.GetBoundingBox();

        if (_scene.Camera == null)
        {
            // No camera was defined: auto-frame a comfy one.
            Camera comfy = Camera.CreateComfyCam(min, max);
            comfy.Name = "comfy";
            RegisterCamera(comfy);

            rrr.App.Log.Debug(
                $"No camera defined; using auto-framed comfy camera '{comfy.Name}'.");
        }
        else
        {
            // At least one camera is present: use it instead of a comfy camera.
            rrr.App.Log.Debug(
                $"Using camera '{_scene.Camera.Name}' (of {_scene.Cameras.Count} defined).");
        }

        if (_scene.Lights.Count == 0)
            _scene.Lights.Add(PointLight.CreateComfyLight(min, max));
    }

    //
    // Helpers
    //

    private string DefaultOutputFile()
    {
        string name = Path.GetFileNameWithoutExtension(_scriptPath) + ".bmp";
        return Path.Combine(_scriptDirectory, name);
    }

    private string ResolvePath(string file)
    {
        return Path.IsPathRooted(file)
            ? file
            : Path.Combine(_scriptDirectory, file);
    }

    private void RegisterObjects(string name, List<SceneObject> objects)
    {
        _objects[name] = objects;
        _lastObject = name;
    }

    private List<SceneObject> ResolveObjects(string name, int line)
    {
        if (name.Length == 0)
            throw new ScriptException(line, "No object specified and none created yet.");

        if (!_objects.TryGetValue(name, out List<SceneObject>? list))
            throw new ScriptException(line, $"Unknown object '{name}'.");

        return list;
    }

    private Material GetOrCreateMaterial(string name)
    {
        if (name.Length == 0)
            name = $"material{++_autoCounter}";

        if (_materials.TryGetValue(name, out Material? existing))
            return existing;

        Material material = new Material { Name = name };
        _materials[name] = material;
        _scene.Materials.Add(material);
        return material;
    }

    private Material CreateDefaultMaterial(string objectName)
    {
        Material material = new Material
        {
            Name = objectName + "_mat",
            DiffuseColor = ColorRGBAf.White
        };

        _materials[material.Name] = material;
        _scene.Materials.Add(material);
        return material;
    }

    private void ApplyOptionalMaterial(List<SceneObject> objects, ScriptArguments args)
    {
        if (!args.Has("material"))
            return;

        Material material = GetOrCreateMaterial(args.GetString("material", ""));

        foreach (SceneObject obj in objects)
            obj.Material = material;

        _lastMaterial = material.Name;
    }

    private static float Deg(float degrees) => degrees * MathF.PI / 180.0f;

    private static string StripComment(string line)
    {
        int hash = line.IndexOf('#');
        return hash < 0 ? line : line.Substring(0, hash);
    }

    private static bool EndsWithContinuation(string line)
    {
        return line.TrimEnd().EndsWith('\\');
    }

    private static List<string> Tokenize(string line)
    {
        List<string> tokens = new List<string>();
        StringBuilder sb = new StringBuilder();
        bool inQuotes = false;

        foreach (char ch in line)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(ch))
            {
                if (sb.Length > 0)
                {
                    tokens.Add(sb.ToString());
                    sb.Clear();
                }
            }
            else
            {
                sb.Append(ch);
            }
        }

        if (sb.Length > 0)
            tokens.Add(sb.ToString());

        return tokens;
    }
}
