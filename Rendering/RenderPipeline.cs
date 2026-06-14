using rrr.Core;
using rrr.Scene;
using rrr.VMath;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace rrr.Rendering;

public static class RenderPipeline
{
    public static void Render(
        FrameBuffer framebuffer,
        DepthBuffer depthBuffer,
        Scene.Scene scene,
        RenderSettings settings)
    {
        Matrix4x4f view =
            scene.Camera.GetViewMatrix();

        Matrix4x4f projection =
            scene.Camera.GetProjectionMatrix(
                (float)framebuffer.Width /
                framebuffer.Height);

        if (settings.ShadingMode == ShadingMode.Wireframe)
        {
            RenderWireframe(
                framebuffer, scene, view, projection, settings);
            return;
        }

        // Render a shadow map per shadow-casting light (if enabled).
        ShadowMap?[] shadowMaps = BuildShadowMaps(scene, settings);

        //
        // Untextured objects render with the selected shading mode.
        //
        switch (settings.ShadingMode)
        {
            case ShadingMode.Gouraud:
                RenderShaded<GouraudShader>(
                    framebuffer, depthBuffer, scene,
                    view, projection, settings, shadowMaps, MakeGouraud, IsUntextured);
                break;

            case ShadingMode.Phong:
                RenderShaded<PhongShader>(
                    framebuffer, depthBuffer, scene,
                    view, projection, settings, shadowMaps, MakePhong, IsUntextured);
                break;

            case ShadingMode.Flat:
            default:
                RenderShaded<FlatShader>(
                    framebuffer, depthBuffer, scene,
                    view, projection, settings, shadowMaps, MakeFlat, IsUntextured);
                break;
        }

        //
        // Textured objects always use per-pixel textured shading (the
        // texture supplies the per-pixel albedo). Drawn as a separate pass;
        // since geometry is opaque and depth-tested, the result is
        // independent of the order between the two passes.
        //
        RenderShaded<TextureShader>(
            framebuffer, depthBuffer, scene,
            view, projection, settings, shadowMaps, MakeTexture, IsTextured);
    }

    private static ShadowMap?[] BuildShadowMaps(
        Scene.Scene scene, RenderSettings settings)
    {
        ShadowMap?[] maps = new ShadowMap?[scene.Lights.Count];

        if (!settings.ShadowsEnabled)
            return maps;

        var (min, max) = scene.GetBoundingBox();

        for (int i = 0; i < scene.Lights.Count; i++)
        {
            Light light = scene.Lights[i];

            if (!light.CastsShadows)
                continue;

            Matrix4x4f lightVp =
                ShadowMapRenderer.BuildLightMatrix(light, min, max);

            maps[i] = ShadowMapRenderer.Render(
                scene, lightVp, settings.ShadowMapResolution);
        }

        return maps;
    }

    private static bool IsTextured(SceneObject o)
    {
        Material? m = o.Material;
        return m != null &&
            (m.DiffuseTexture != null ||
             m.SpecularTexture != null ||
             m.EmissiveTexture != null ||
             m.NormalTexture != null);
    }

    private static bool IsUntextured(SceneObject o) => !IsTextured(o);

    //
    // Shaded pipeline: phase 1 transforms every (selected) triangle to
    // screen space (serial), phase 2 rasterizes in parallel over horizontal
    // bands. Each band owns disjoint rows, so no synchronization is needed,
    // and within a band triangles keep scene order -> output identical to
    // the serial renderer at any thread count.
    //

    private delegate TShader ShaderFactory<TShader>(
        in ProjectedTriangle triangle,
        ColorRGBAf baseColor,
        ColorRGBAf specularColor,
        float shininess,
        ColorRGBAf emissiveColor,
        Vector3f cameraPosition,
        IReadOnlyList<Light> lights,
        IReadOnlyList<ShadowMap?> shadowMaps,
        Material? material)
        where TShader : struct, IPixelShader;

    private static void RenderShaded<TShader>(
        FrameBuffer framebuffer,
        DepthBuffer depthBuffer,
        Scene.Scene scene,
        Matrix4x4f view,
        Matrix4x4f projection,
        RenderSettings settings,
        IReadOnlyList<ShadowMap?> shadowMaps,
        ShaderFactory<TShader> makeShader,
        Func<SceneObject, bool> includeObject)
        where TShader : struct, IPixelShader
    {
        Vector3f cameraPosition =
            scene.Camera.Position;

        IReadOnlyList<Light> lights =
            scene.Lights;

        //
        // Phase 1 — transform, clip, cull, build per-triangle shaders.
        //

        List<ScreenTriangle<TShader>> triangles =
            new List<ScreenTriangle<TShader>>();

        foreach (SceneObject sceneObject in scene.Objects)
        {
            if (!includeObject(sceneObject))
                continue;

            Matrix4x4f world =
                sceneObject.Transform;

            Matrix4x4f wvp =
                projection * view * world;

            RenderMesh mesh =
                sceneObject.GetRenderMesh();

            Material? material = sceneObject.Material;

            for (int i = 0;
                 i < mesh.Indices.Count;
                 i += 3)
            {
                if (!ProjectTriangle(
                        framebuffer, mesh, i, world, wvp,
                        settings.BackfaceCulling,
                        out ProjectedTriangle projected))
                {
                    continue;
                }

                GetSurface(
                    material, i,
                    out ColorRGBAf baseColor,
                    out ColorRGBAf specularColor,
                    out float shininess,
                    out ColorRGBAf emissiveColor);

                TShader shader =
                    makeShader(
                        in projected,
                        baseColor, specularColor, shininess, emissiveColor,
                        cameraPosition, lights, shadowMaps, material);

                triangles.Add(new ScreenTriangle<TShader>
                {
                    X0 = projected.X0, Y0 = projected.Y0, Z0 = projected.Z0,
                    X1 = projected.X1, Y1 = projected.Y1, Z1 = projected.Z1,
                    X2 = projected.X2, Y2 = projected.Y2, Z2 = projected.Z2,
                    IW0 = projected.IW0, IW1 = projected.IW1, IW2 = projected.IW2,
                    Shader = shader
                });
            }
        }

        //
        // Phase 2 — rasterize in parallel, one horizontal band per task.
        //

        int height = framebuffer.Height;

        int bandCount = (settings.Parallel==0 ?
             Math.Min(
                height,
                Math.Max(1, Environment.ProcessorCount * 4)) : 
                settings.Parallel); 

        Parallel.For(0, bandCount, band =>
        {
            int bandMinY =
                (int)((long)band * height / bandCount);

            int bandMaxY =
                (int)((long)(band + 1) * height / bandCount) - 1;

            if (bandMaxY < bandMinY)
                return;

            for (int t = 0; t < triangles.Count; t++)
            {
                ScreenTriangle<TShader> s = triangles[t];

                Rasterizer.FillTriangle(
                    framebuffer, depthBuffer,
                    s.X0, s.Y0, s.Z0,
                    s.X1, s.Y1, s.Z1,
                    s.X2, s.Y2, s.Z2,
                    s.IW0, s.IW1, s.IW2,
                    s.Shader,
                    bandMinY, bandMaxY);
            }
        });
    }

    private static void RenderWireframe(
        FrameBuffer framebuffer,
        Scene.Scene scene,
        Matrix4x4f view,
        Matrix4x4f projection,
        RenderSettings settings)
    {
        foreach (SceneObject sceneObject in scene.Objects)
        {
            Matrix4x4f world =
                sceneObject.Transform;

            Matrix4x4f wvp =
                projection * view * world;

            RenderMesh mesh =
                sceneObject.GetRenderMesh();

            for (int i = 0;
                 i < mesh.Indices.Count;
                 i += 3)
            {
                // Wireframe never culls (matches previous behaviour).
                if (!ProjectTriangle(
                        framebuffer, mesh, i, world, wvp,
                        backfaceCull: false,
                        out ProjectedTriangle t))
                {
                    continue;
                }

                ColorRGBAf baseColor =
                    sceneObject.Material != null
                        ? sceneObject.Material.DiffuseColor
                        : RandomColor(i);

                uint wire =
                    PixelPacker.Pack(
                        ColorRGBA32.FromRGBAf(baseColor));

                Rasterizer.DrawLine(
                    framebuffer, t.X0, t.Y0, t.X1, t.Y1, wire);

                Rasterizer.DrawLine(
                    framebuffer, t.X1, t.Y1, t.X2, t.Y2, wire);

                Rasterizer.DrawLine(
                    framebuffer, t.X2, t.Y2, t.X0, t.Y0, wire);
            }
        }
    }

    /// <summary>
    /// Runs the vertex shader, near-plane reject, perspective divide,
    /// viewport transform and (optionally) backface culling for one
    /// triangle, also producing the world-space positions, normals and UVs
    /// needed for shading. Returns false if the triangle is rejected.
    /// </summary>
    private static bool ProjectTriangle(
        FrameBuffer framebuffer,
        RenderMesh mesh,
        int triangleIndex,
        Matrix4x4f world,
        Matrix4x4f wvp,
        bool backfaceCull,
        out ProjectedTriangle result)
    {
        result = default;

        int i0 = mesh.Indices[triangleIndex + 0];
        int i1 = mesh.Indices[triangleIndex + 1];
        int i2 = mesh.Indices[triangleIndex + 2];

        RenderVertex v0 = mesh.Vertices[i0];
        RenderVertex v1 = mesh.Vertices[i1];
        RenderVertex v2 = mesh.Vertices[i2];

        //
        // Vertex shader
        //

        Vector4f p0 = wvp * Vector4f.FromVector3(v0.Position, 1.0f);
        Vector4f p1 = wvp * Vector4f.FromVector3(v1.Position, 1.0f);
        Vector4f p2 = wvp * Vector4f.FromVector3(v2.Position, 1.0f);

        //
        // Near-plane hack
        //

        if (p0.W <= 0 || p1.W <= 0 || p2.W <= 0)
            return false;

        //
        // Perspective divide
        //

        Vector3f ndc0 = p0.PerspectiveDivide();
        Vector3f ndc1 = p1.PerspectiveDivide();
        Vector3f ndc2 = p2.PerspectiveDivide();

        //
        // Viewport transform
        //

        int x0 = (int)((ndc0.X * 0.5f + 0.5f) * framebuffer.Width);
        int y0 = (int)((1.0f - (ndc0.Y * 0.5f + 0.5f)) * framebuffer.Height);

        int x1 = (int)((ndc1.X * 0.5f + 0.5f) * framebuffer.Width);
        int y1 = (int)((1.0f - (ndc1.Y * 0.5f + 0.5f)) * framebuffer.Height);

        int x2 = (int)((ndc2.X * 0.5f + 0.5f) * framebuffer.Width);
        int y2 = (int)((1.0f - (ndc2.Y * 0.5f + 0.5f)) * framebuffer.Height);

        //
        // Backface culling (screen space). With the Y-flip above, front
        // faces have a negative signed area; back faces (>= 0) are dropped.
        //

        if (backfaceCull)
        {
            int screenArea =
                (x2 - x0) * (y1 - y0) -
                (y2 - y0) * (x1 - x0);

            if (screenArea >= 0)
                return false;
        }

        //
        // World-space geometry + UVs for shading.
        //

        result.X0 = x0; result.Y0 = y0; result.Z0 = ndc0.Z;
        result.X1 = x1; result.Y1 = y1; result.Z1 = ndc1.Z;
        result.X2 = x2; result.Y2 = y2; result.Z2 = ndc2.Z;

        // 1/w per vertex (w > 0 here, guaranteed by the near-plane reject).
        result.IW0 = 1.0f / p0.W;
        result.IW1 = 1.0f / p1.W;
        result.IW2 = 1.0f / p2.W;

        result.W0 = (world * Vector4f.FromVector3(v0.Position, 1.0f)).XYZ();
        result.W1 = (world * Vector4f.FromVector3(v1.Position, 1.0f)).XYZ();
        result.W2 = (world * Vector4f.FromVector3(v2.Position, 1.0f)).XYZ();

        // Geometric face normal, used as a fallback when the mesh has no
        // per-vertex normals (e.g. an OBJ with "f v/vt" faces) — otherwise
        // the interpolated normal would be zero and lighting would vanish.
        Vector3f faceNormal =
            Vector3f.Cross(result.W1 - result.W0, result.W2 - result.W0)
                .Normalized();

        result.N0 = VertexNormal(world, v0.Normal, faceNormal);
        result.N1 = VertexNormal(world, v1.Normal, faceNormal);
        result.N2 = VertexNormal(world, v2.Normal, faceNormal);

        result.T0 = TransformNormal(world, v0.Tangent);
        result.T1 = TransformNormal(world, v1.Tangent);
        result.T2 = TransformNormal(world, v2.Tangent);

        result.H0 = v0.Handedness;
        result.H1 = v1.Handedness;
        result.H2 = v2.Handedness;

        result.UV0 = v0.UV;
        result.UV1 = v1.UV;
        result.UV2 = v2.UV;

        return true;
    }

    private static void GetSurface(
        Material? material,
        int triangleIndex,
        out ColorRGBAf baseColor,
        out ColorRGBAf specularColor,
        out float shininess,
        out ColorRGBAf emissiveColor)
    {
        baseColor =
            material != null
                ? material.DiffuseColor
                : RandomColor(triangleIndex);

        specularColor =
            material?.SpecularColor ?? ColorRGBAf.White;

        shininess =
            material?.Shininess ?? 0.0f;

        emissiveColor =
            material?.EmissiveColor ?? ColorRGBAf.Black;
    }

    //
    // Per-mode shader factories (invoked once per triangle, in phase 1).
    //

    private static FlatShader MakeFlat(
        in ProjectedTriangle t,
        ColorRGBAf baseColor,
        ColorRGBAf specularColor,
        float shininess,
        ColorRGBAf emissiveColor,
        Vector3f cameraPosition,
        IReadOnlyList<Light> lights,
        IReadOnlyList<ShadowMap?> shadowMaps,
        Material? material)
    {
        // One light evaluation per triangle, using the face normal.
        Vector3f faceNormal =
            Vector3f.Cross(t.W1 - t.W0, t.W2 - t.W0)
                .Normalized();

        Vector3f centroid =
            (t.W0 + t.W1 + t.W2) / 3.0f;

        ColorRGBAf lit =
            Lighting.Shade(
                centroid, faceNormal, cameraPosition, lights,
                baseColor, specularColor, shininess, emissiveColor, shadowMaps);

        return new FlatShader(lit);
    }

    private static GouraudShader MakeGouraud(
        in ProjectedTriangle t,
        ColorRGBAf baseColor,
        ColorRGBAf specularColor,
        float shininess,
        ColorRGBAf emissiveColor,
        Vector3f cameraPosition,
        IReadOnlyList<Light> lights,
        IReadOnlyList<ShadowMap?> shadowMaps,
        Material? material)
    {
        ColorRGBAf c0 = Lighting.Shade(
            t.W0, t.N0, cameraPosition, lights,
            baseColor, specularColor, shininess, emissiveColor, shadowMaps);
        ColorRGBAf c1 = Lighting.Shade(
            t.W1, t.N1, cameraPosition, lights,
            baseColor, specularColor, shininess, emissiveColor, shadowMaps);
        ColorRGBAf c2 = Lighting.Shade(
            t.W2, t.N2, cameraPosition, lights,
            baseColor, specularColor, shininess, emissiveColor, shadowMaps);

        return new GouraudShader(c0, c1, c2);
    }

    private static PhongShader MakePhong(
        in ProjectedTriangle t,
        ColorRGBAf baseColor,
        ColorRGBAf specularColor,
        float shininess,
        ColorRGBAf emissiveColor,
        Vector3f cameraPosition,
        IReadOnlyList<Light> lights,
        IReadOnlyList<ShadowMap?> shadowMaps,
        Material? material)
    {
        return new PhongShader(
            t.W0, t.W1, t.W2,
            t.N0, t.N1, t.N2,
            cameraPosition,
            lights,
            shadowMaps,
            baseColor,
            specularColor,
            shininess,
            emissiveColor);
    }

    private static TextureShader MakeTexture(
        in ProjectedTriangle t,
        ColorRGBAf baseColor,
        ColorRGBAf specularColor,
        float shininess,
        ColorRGBAf emissiveColor,
        Vector3f cameraPosition,
        IReadOnlyList<Light> lights,
        IReadOnlyList<ShadowMap?> shadowMaps,
        Material? material)
    {
        // Diffuse texture (if any) is the albedo, tinted by baseColor.
        // Specular/emissive maps modulate their respective colors.
        return new TextureShader(
            t.W0, t.W1, t.W2,
            t.N0, t.N1, t.N2,
            t.T0, t.T1, t.T2,
            t.H0, t.H1, t.H2,
            t.UV0, t.UV1, t.UV2,
            cameraPosition,
            lights,
            shadowMaps,
            material?.DiffuseTexture,
            material?.SpecularTexture,
            material?.EmissiveTexture,
            material?.NormalTexture,
            material?.NormalIsHeightMap ?? false,
            material?.InvertNormalGreen ?? false,
            material?.BumpScale ?? 1.0f,
            baseColor,
            specularColor,
            shininess,
            emissiveColor);
    }

    /// <summary>
    /// Transforms a normal/direction by the upper-left 3x3 of a matrix
    /// (translation ignored). Correct for rotation and uniform scale,
    /// which covers the transforms currently produced by the pipeline.
    /// </summary>
    /// <summary>
    /// World-space vertex normal, falling back to the face normal when the
    /// source normal is missing/degenerate.
    /// </summary>
    private static Vector3f VertexNormal(
        Matrix4x4f world,
        Vector3f normal,
        Vector3f faceNormal)
    {
        if (normal.LengthSquared() < 1e-12f)
            return faceNormal;

        return TransformNormal(world, normal);
    }

    private static Vector3f TransformNormal(
        Matrix4x4f m,
        Vector3f n)
    {
        return new Vector3f(
            m.M11 * n.X + m.M12 * n.Y + m.M13 * n.Z,
            m.M21 * n.X + m.M22 * n.Y + m.M23 * n.Z,
            m.M31 * n.X + m.M32 * n.Y + m.M33 * n.Z)
            .Normalized();
    }

    /// <summary>
    /// Deterministic pastel color seeded by the triangle index, so an
    /// object without a material is rendered with stable per-triangle
    /// colors instead of flickering between frames.
    /// </summary>
    private static ColorRGBAf RandomColor(int seed)
    {
        Random random = new Random(seed);

        return new ColorRGBAf(
            0.4f + random.NextSingle() * 0.6f,
            0.4f + random.NextSingle() * 0.6f,
            0.4f + random.NextSingle() * 0.6f);
    }

    //
    // Screen-space data carried from phase 1 to phase 2.
    //

    private struct ProjectedTriangle
    {
        public int X0, Y0; public float Z0;
        public int X1, Y1; public float Z1;
        public int X2, Y2; public float Z2;

        public float IW0, IW1, IW2;   // 1 / clip-space w (for perspective-correct interp)

        public Vector3f W0, W1, W2;   // world-space positions
        public Vector3f N0, N1, N2;   // world-space normals
        public Vector3f T0, T1, T2;   // world-space tangents
        public float H0, H1, H2;      // tangent handedness
        public Vector2f UV0, UV1, UV2;
    }

    private struct ScreenTriangle<TShader>
        where TShader : struct, IPixelShader
    {
        public int X0, Y0; public float Z0;
        public int X1, Y1; public float Z1;
        public int X2, Y2; public float Z2;

        public float IW0, IW1, IW2;

        public TShader Shader;
    }

    //
    // Shaders
    //

    /// <summary>
    /// Flat shader: constant precomputed color over the whole triangle.
    /// </summary>
    private readonly struct FlatShader : IPixelShader
    {
        private readonly ColorRGBAf _color;

        public FlatShader(ColorRGBAf color)
        {
            _color = color;
        }

        public ColorRGBAf Shade(float b0, float b1, float b2)
        {
            return _color;
        }
    }

    /// <summary>
    /// Gouraud shader: interpolates the three precomputed vertex colors.
    /// </summary>
    private readonly struct GouraudShader : IPixelShader
    {
        private readonly ColorRGBAf _c0;
        private readonly ColorRGBAf _c1;
        private readonly ColorRGBAf _c2;

        public GouraudShader(
            ColorRGBAf c0,
            ColorRGBAf c1,
            ColorRGBAf c2)
        {
            _c0 = c0;
            _c1 = c1;
            _c2 = c2;
        }

        public ColorRGBAf Shade(float b0, float b1, float b2)
        {
            return new ColorRGBAf(
                b0 * _c0.R + b1 * _c1.R + b2 * _c2.R,
                b0 * _c0.G + b1 * _c1.G + b2 * _c2.G,
                b0 * _c0.B + b1 * _c1.B + b2 * _c2.B,
                b0 * _c0.A + b1 * _c1.A + b2 * _c2.A);
        }
    }

    /// <summary>
    /// Phong shader: interpolates world position and normal per pixel,
    /// then evaluates lighting.
    /// </summary>
    private readonly struct PhongShader : IPixelShader
    {
        private readonly Vector3f _w0;
        private readonly Vector3f _w1;
        private readonly Vector3f _w2;
        private readonly Vector3f _n0;
        private readonly Vector3f _n1;
        private readonly Vector3f _n2;
        private readonly Vector3f _cameraPosition;
        private readonly IReadOnlyList<Light> _lights;
        private readonly IReadOnlyList<ShadowMap?> _shadowMaps;
        private readonly ColorRGBAf _baseColor;
        private readonly ColorRGBAf _specularColor;
        private readonly float _shininess;
        private readonly ColorRGBAf _emissive;

        public PhongShader(
            Vector3f w0,
            Vector3f w1,
            Vector3f w2,
            Vector3f n0,
            Vector3f n1,
            Vector3f n2,
            Vector3f cameraPosition,
            IReadOnlyList<Light> lights,
            IReadOnlyList<ShadowMap?> shadowMaps,
            ColorRGBAf baseColor,
            ColorRGBAf specularColor,
            float shininess,
            ColorRGBAf emissive)
        {
            _w0 = w0;
            _w1 = w1;
            _w2 = w2;
            _n0 = n0;
            _n1 = n1;
            _n2 = n2;
            _cameraPosition = cameraPosition;
            _lights = lights;
            _shadowMaps = shadowMaps;
            _baseColor = baseColor;
            _specularColor = specularColor;
            _shininess = shininess;
            _emissive = emissive;
        }

        public ColorRGBAf Shade(float b0, float b1, float b2)
        {
            Vector3f position =
                _w0 * b0 + _w1 * b1 + _w2 * b2;

            Vector3f normal =
                _n0 * b0 + _n1 * b1 + _n2 * b2;

            return Lighting.Shade(
                position, normal, _cameraPosition, _lights,
                _baseColor, _specularColor, _shininess, _emissive, _shadowMaps);
        }
    }

    /// <summary>
    /// Mapped-material shader: interpolates UVs (and world position/normal)
    /// per pixel, samples the diffuse/specular/emissive maps (each optional),
    /// then evaluates lighting (per-pixel, Phong-quality). Material colors
    /// tint their respective maps.
    /// </summary>
    private readonly struct TextureShader : IPixelShader
    {
        private readonly Vector3f _w0;
        private readonly Vector3f _w1;
        private readonly Vector3f _w2;
        private readonly Vector3f _n0;
        private readonly Vector3f _n1;
        private readonly Vector3f _n2;
        private readonly Vector3f _t0;
        private readonly Vector3f _t1;
        private readonly Vector3f _t2;
        private readonly float _h0;
        private readonly float _h1;
        private readonly float _h2;
        private readonly Vector2f _uv0;
        private readonly Vector2f _uv1;
        private readonly Vector2f _uv2;
        private readonly Vector3f _cameraPosition;
        private readonly IReadOnlyList<Light> _lights;
        private readonly IReadOnlyList<ShadowMap?> _shadowMaps;
        private readonly Texture2D? _diffuse;
        private readonly Texture2D? _specular;
        private readonly Texture2D? _emissive;
        private readonly Texture2D? _normalMap;
        private readonly bool _normalIsHeight;
        private readonly bool _invertGreen;
        private readonly float _bumpScale;
        private readonly ColorRGBAf _tint;
        private readonly ColorRGBAf _specularColor;
        private readonly float _shininess;
        private readonly ColorRGBAf _emissiveColor;

        public TextureShader(
            Vector3f w0,
            Vector3f w1,
            Vector3f w2,
            Vector3f n0,
            Vector3f n1,
            Vector3f n2,
            Vector3f t0,
            Vector3f t1,
            Vector3f t2,
            float h0,
            float h1,
            float h2,
            Vector2f uv0,
            Vector2f uv1,
            Vector2f uv2,
            Vector3f cameraPosition,
            IReadOnlyList<Light> lights,
            IReadOnlyList<ShadowMap?> shadowMaps,
            Texture2D? diffuse,
            Texture2D? specular,
            Texture2D? emissive,
            Texture2D? normalMap,
            bool normalIsHeight,
            bool invertGreen,
            float bumpScale,
            ColorRGBAf tint,
            ColorRGBAf specularColor,
            float shininess,
            ColorRGBAf emissiveColor)
        {
            _w0 = w0;
            _w1 = w1;
            _w2 = w2;
            _n0 = n0;
            _n1 = n1;
            _n2 = n2;
            _t0 = t0;
            _t1 = t1;
            _t2 = t2;
            _h0 = h0;
            _h1 = h1;
            _h2 = h2;
            _uv0 = uv0;
            _uv1 = uv1;
            _uv2 = uv2;
            _cameraPosition = cameraPosition;
            _lights = lights;
            _shadowMaps = shadowMaps;
            _diffuse = diffuse;
            _specular = specular;
            _emissive = emissive;
            _normalMap = normalMap;
            _normalIsHeight = normalIsHeight;
            _invertGreen = invertGreen;
            _bumpScale = bumpScale;
            _tint = tint;
            _specularColor = specularColor;
            _shininess = shininess;
            _emissiveColor = emissiveColor;
        }

        public ColorRGBAf Shade(float b0, float b1, float b2)
        {
            Vector2f uv =
                _uv0 * b0 + _uv1 * b1 + _uv2 * b2;

            // Albedo: diffuse map (sRGB placeholder) tinted, or just the tint.
            ColorRGBAf albedo = _tint;

            if (_diffuse != null)
            {
                ColorRGBAf d =
                    ColorSpace.SrgbToLinear(_diffuse.Sample(uv.X, uv.Y));

                albedo = new ColorRGBAf(
                    d.R * _tint.R, d.G * _tint.G, d.B * _tint.B, d.A * _tint.A);
            }

            // Specular color modulated by the specular map.
            ColorRGBAf specular = _specularColor;

            if (_specular != null)
            {
                ColorRGBAf s = _specular.Sample(uv.X, uv.Y);
                specular = new ColorRGBAf(
                    s.R * _specularColor.R,
                    s.G * _specularColor.G,
                    s.B * _specularColor.B);
            }

            // Emissive color modulated by the emissive map.
            ColorRGBAf emissive = _emissiveColor;

            if (_emissive != null)
            {
                ColorRGBAf e =
                    ColorSpace.SrgbToLinear(_emissive.Sample(uv.X, uv.Y));

                emissive = new ColorRGBAf(
                    e.R * _emissiveColor.R,
                    e.G * _emissiveColor.G,
                    e.B * _emissiveColor.B);
            }

            Vector3f position =
                _w0 * b0 + _w1 * b1 + _w2 * b2;

            Vector3f normal =
                (_n0 * b0 + _n1 * b1 + _n2 * b2).Normalized();

            if (_normalMap != null)
                normal = PerturbNormal(normal, b0, b1, b2, uv);

            return Lighting.Shade(
                position, normal, _cameraPosition, _lights,
                albedo, specular, _shininess, emissive, _shadowMaps);
        }

        private Vector3f PerturbNormal(
            Vector3f normal, float b0, float b1, float b2, Vector2f uv)
        {
            Vector3f tIn = _t0 * b0 + _t1 * b1 + _t2 * b2;

            // Re-orthonormalize the tangent against the interpolated normal.
            Vector3f t = tIn - normal * Vector3f.Dot(normal, tIn);

            if (t.LengthSquared() < 1e-12f)
                return normal;

            t = t.Normalized();

            float handedness = (_h0 * b0 + _h1 * b1 + _h2 * b2) < 0.0f ? -1.0f : 1.0f;
            Vector3f bitangent = Vector3f.Cross(normal, t) * handedness;

            Vector3f tn = SampleTangentNormal(uv);

            // tangent-space -> world-space
            return (t * tn.X + bitangent * tn.Y + normal * tn.Z).Normalized();
        }

        private Vector3f SampleTangentNormal(Vector2f uv)
        {
            // Normal/height maps store linear data, so no sRGB conversion.
            if (_normalIsHeight)
            {
                float du = 1.0f / _normalMap!.Width;
                float dv = 1.0f / _normalMap.Height;

                float h = _normalMap.Sample(uv.X, uv.Y).R;
                float hx = _normalMap.Sample(uv.X + du, uv.Y).R;
                float hy = _normalMap.Sample(uv.X, uv.Y + dv).R;

                return new Vector3f(
                    (h - hx) * _bumpScale,
                    (h - hy) * _bumpScale,
                    1.0f).Normalized();
            }

            ColorRGBAf c = _normalMap!.Sample(uv.X, uv.Y);

            float ny = (c.G * 2.0f - 1.0f) * _bumpScale;
            if (_invertGreen)
                ny = -ny;

            return new Vector3f(
                (c.R * 2.0f - 1.0f) * _bumpScale,
                ny,
                c.B * 2.0f - 1.0f).Normalized();
        }
    }
}
