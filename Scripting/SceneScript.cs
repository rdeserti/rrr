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
            string line = StripComment(lines[i]).Trim();

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
            case "translate": DoTransform(command, subType, args); break;
            case "rotate": DoTransform(command, subType, args); break;
            case "scale": DoTransform(command, subType, args); break;
            case "material": DoMaterial(args); break;
            case "assign": DoAssign(args); break;
            case "camera": DoCamera(subType, args); break;
            case "light": DoLight(subType, args); break;
            case "render": DoRender(args); break;
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
            ShadowPcfRadius = args.GetInt("shadowpcf", _settings.ShadowPcfRadius)
        };

        _width = args.GetInt("width", _width);
        _height = args.GetInt("height", _height);
        _background = args.GetColor("background", _background);
    }

    private void DoLoad(string? subType, ScriptArguments args)
    {
        subType ??= "obj";

        if (subType != "obj")
            throw new ScriptException(args.Line, $"Unsupported load type '{subType}'.");

        string file = args.GetString("file", "");

        if (file.Length == 0)
            throw new ScriptException(args.Line, "load obj requires file=...");

        string path = ResolvePath(file);
        string name = args.GetString("name", Path.GetFileNameWithoutExtension(file));

        ObjImporter importer = new ObjImporter();
        Scene.Scene loaded = importer.Load(path);

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

        RegisterObjects(name, added);
        ApplyOptionalMaterial(added, args);
    }

    private void DoMesh(string? subType, ScriptArguments args)
    {
        subType ??= "cube";

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

        material.BumpScale = args.GetFloat("bumpscale", material.BumpScale);
        material.InvertNormalGreen = args.GetBool("invertgreen", material.InvertNormalGreen);

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

        if (comfy)
        {
            var (min, max) = _scene.GetBoundingBox();
            _scene.Camera = Camera.CreateComfyCam(min, max);
            return;
        }

        _scene.Camera = new Camera
        {
            Position = args.GetVector3("position", new Vector3f(0, 2, -5)),
            Target = args.GetVector3("target", Vector3f.Zero),
            Up = args.GetVector3("up", new Vector3f(0, 1, 0)),
            Fov = args.GetFloat("fov", 60f),
            Near = args.GetFloat("near", 0.1f),
            Far = args.GetFloat("far", 1000f)
        };
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
            _scene, _settings, file, _width, _height, _background);

        for (int i = 0; i < _scene.Objects.Count; i++)
            _scene.Objects[i].Transform = saved[i];

        _rendered = true;
    }

    private void EnsureCameraAndLights()
    {
        var (min, max) = _scene.GetBoundingBox();

        if (_scene.Camera == null)
            _scene.Camera = Camera.CreateComfyCam(min, max);

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
