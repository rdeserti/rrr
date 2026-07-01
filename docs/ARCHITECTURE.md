# RRR — Architecture

Reference for the internals of the RRR renderer. Target: .NET 8, single
executable, no external dependencies.

## Namespaces & key files

### `Core` — pixels, buffers, color, profiling
- `FrameBuffer` — `uint[]` RGBA32 color target. `Pixels`, `Width/Height/Stride`,
  `SetPixel(Unsafe)`, `Clear`.
- `DepthBuffer` — `float[]` depth, `TestAndWrite(Unsafe)` (cleared to +∞).
- `ColorRGBAf` — float RGBA (working color). `FromRGBA32`, constants.
- `ColorRGBA32` — byte RGBA. `FromRGBAf`.
- `PixelPacker` (file `PixlePacker.cs`) — pack/unpack `uint` as **AARRGGBB**.
- `PixelFormat` — only `RGBA32`.
- `ColorSpace` — real piecewise **sRGB <-> linear** (`SrgbToLinear`/`LinearToSrgb`),
  each direction gated by its own static flag (`SrgbToLinearEnabled` /
  `LinearToSrgbEnabled`, set per render from `RenderSettings.LinearizeInput` /
  `EncodeSrgb`). Both **on by default**; a disabled direction is an immediate
  identity (no per-channel pow). Turning both off is bit-identical to the
  pre-gamma behaviour.
- `Profiler` — `Start` / `Mark(name)` / `Dump`.

### `VMath` — math
- `Vector2f`, `Vector3f`, `Vector4f` (with `PerspectiveDivide`, `XYZ`,
  `FromVector3`).
- `Matrix4x4f` — row-major; `Multiply`, operators, `CreateLookAt` (left-handed),
  `CreatePerspective`/`Degrees`, `CreateOrthographic` (z→[0,1]), `CreateTranslation`,
  `CreateScale`, `CreateRotationX/Y/Z`, `Transpose`.

### `Scene` — scene graph & assets
- `Scene` — active `Camera`, `List<Camera> Cameras` (all defined, for
  selection/listing), `List<SceneObject> Objects`, `List<Light> Lights`,
  `List<Material> Materials`, `GetBoundingBox()` (local positions, ignores
  transforms — see Limitations).
- `SceneObject` — `Name`, `Mesh`, cached `RenderMesh` via `GetRenderMesh()`
  (`InvalidateRenderMesh()` to rebuild), `Material`, `Transform` (local).
- `Mesh` — raw `Positions`, `Normals`, `UVs`, `Triangles` (index triplets), plus
  optional `Colors` (per-vertex, parallel to positions) and `Tangents` (Vec4,
  xyz + handedness w).
- `Triangle` — per-vertex position/uv/normal indices.
- `Camera` — `Name`, `Position/Target/Up/Fov/Near/Far`, `GetViewMatrix`,
  `GetProjectionMatrix`, `CreateComfyCam(min,max)` (auto-frame).
- `Material` — diffuse/specular/emissive colors, `Shininess`, and textures:
  `DiffuseTexture`, `SpecularTexture`, `EmissiveTexture`, `NormalTexture` +
  `NormalIsHeightMap`, `InvertNormalGreen`, `BumpScale`, `OcclusionTexture`
  (AO). Plus `AlphaMode` (Opaque/Mask/Blend) + `AlphaCutoff` (alpha-test),
  `DoubleSided` (per-material backface cull + two-sided normal), `Unlit`.
  **Ray-tracer only:** `Transmission` (0..1, refraction amount) and
  `IndexOfRefraction` (default 1.5) — ignored by the rasterizer.
- `Texture2D` — wraps a `FrameBuffer`; `WrapU/V`, `Filter`, `Sample` (nearest/
  bilinear), `TryLoad(path)` (safe → null on failure), `IsLikelyGrayscale()`.
- `Light` (abstract) — `CastsShadows` (default true). Subtypes: `PointLight`
  (`Position/Color/Intensity`, `CreateComfyLight`), `DirectionalLight`
  (`Direction/Color/Intensity`), `SpotLight` (`Position/Direction/Color/
  Intensity/ConeAngleDegrees/InnerAngleDegrees`).
- `Primitives` — `Plane/Cube/Sphere/Cylinder/Cone/Pyramid`, generated with
  normals + UVs, triangles auto-oriented outward.

### `Imaging` — image I/O (from scratch)
- `ImageReader.Load(path)` → `FrameBuffer` (dispatch by extension →
  `ImageReaderFactory` → `Readers/BmpImageReader`, `Readers/PngImageReader`,
  `Readers/JpegImageReader`).
- `ImageReader.Load(byte[])` → `FrameBuffer` — decodes in-memory image data,
  detecting the format from magic bytes (`DetectFormat`). Used for
  container-embedded textures (glTF bufferView / data-URI). `Texture2D.FromBytes`
  wraps it with the same graceful-null failure as `TryLoad`.
- `Readers/JpegImageReader` — **baseline and progressive** (8-bit, Huffman) JPEG:
  marker parse, canonical Huffman decode, `0xFF00` de-stuffing + restart markers,
  a **coefficient-store** model (each scan accumulates DCT coefficients — DC/AC
  first pass and successive-approximation refinement, spectral selection, EOB
  runs; interleaved & non-interleaved scans), then zig-zag dequant, separable
  8×8 IDCT, box chroma upsampling (4:4:4/4:2:2/4:2:0), YCbCr→RGB. Arithmetic/
  12-bit/lossless rejected (→ graceful fallback). Pure BCL, no `ZLibStream`.
- `ImageWriter.Save(fb, path)` (→ `ImageWriterFactory` → `Writers/BmpImageWriter`,
  `Writers/PngImageWriter`). PNG writer emits valid CRC32 chunks; uses BCL
  `ZLibStream` for DEFLATE.

### `Importers`
- `ModelImporter.Load(path)` → `Scene` — **facade** dispatching by extension
  (`.obj/.stl/.gltf/.glb`) to the importer below. Used by both the CLI quick
  path (`SceneRenderer.RenderModelFile`) and the script `load` command.
- `ObjImporter.Load(path)` → `Scene`. Reads v/vt/vn/f (fan-triangulated),
  `o`/`g` groups, `usemtl`, `mtllib`. **Flips v** (`1 - v`) to match the
  top-left texture convention. Resolves `.mtl` relative to the `.obj`; falls
  back to `<name>.mtl`.
- `MtlImporter.Load(path)` → `Dictionary<string,Material>`. Maps `Kd/Ks/Ns/Ke`,
  `d`/`Tr` (alpha), `map_Kd/map_Ks/map_Ke`, `norm`/`map_Bump`/`bump`
  (normal — auto-detected as height if grayscale), `map_Height`/`disp`
  (height). Textures via `Texture2D.TryLoad` (broken → solid color).
- `StlImporter.Load(path[, smoothNormals])` → `Scene`. Auto-detects **binary**
  (`84 + 50·N` byte size) vs **ASCII**. STL has no UVs/materials/shared indices,
  so it produces one `SceneObject` with a grey material; coincident vertices are
  **welded** (exact match). Normals are left unset (renderer uses the per-face
  geometric normal → faceted look) unless `smoothNormals` regenerates them.
  Coordinates are imported verbatim (CAD STLs are often Z-up).
- `GltfImporter.Load(path)` → `Scene` (`Gltf/` folder). Loads glTF 2.0 `.gltf`
  (JSON + external/`data:` base64 buffers) and `.glb` (`Gltf/` `ReadGlb`: 12-byte
  header + length-prefixed JSON/BIN chunks). Decodes accessors/bufferViews
  (`ReadFloats`/`ReadIndices`, all component types + stride + normalization;
  tightly-packed 32-bit floats take a `Buffer.BlockCopy` fast path) for
  `POSITION`/`NORMAL`/`TEXCOORD_0`/indices; one `Mesh` per triangle primitive.
  Walks the node graph composing transforms (`Gltf/Mat4`, column-major, TRS or
  matrix) and **bakes the world matrix into the vertices**. Converts RH→LH by
  negating Z and flipping winding; **does not flip v** (glTF UVs are already
  top-left). PBR metallic-roughness → Blinn-Phong: `baseColorFactor`→diffuse,
  `roughnessFactor`→shininess, and **`metallicFactor`** tints the specular
  (`SpecularColor = lerp(0.04, baseColor, metallic)`; diffuse halved for metals,
  not zeroed, so they stay visible in the raster) — this drives the ray tracer's
  derived reflectivity. Also `emissiveFactor` × `KHR_materials_emissive_strength`,
  `occlusionTexture`, `alphaMode`/`alphaCutoff`, `doubleSided`,
  `KHR_materials_unlit`, **`KHR_materials_transmission`** (→ `Transmission`) and
  **`KHR_materials_ior`** (→ `IndexOfRefraction`), base/emissive/normal textures.
  Textures resolve from external files, data-URIs and **bufferView-embedded**
  images (PNG/BMP/JPEG, via `ImageReader.Load(byte[])`), each **decoded once and
  in parallel** (`DecodeImages`) — the bulk of load time on texture-heavy models.
  Also imports
  **perspective cameras** (→ `Scene.Cameras`, first becomes active) and
  **`KHR_lights_punctual`** lights (directional/point/spot → `Scene.Lights`),
  positioned by each node's world transform (physical intensities clamped).
  Vertex attributes: POSITION/NORMAL/TEXCOORD_0/indices plus **COLOR_0** (vertex
  colors → `Mesh.Colors`) and **TANGENT** (→ `Mesh.Tangents`). **Sparse
  accessors** are decoded. JSON via `System.Text.Json` (runtime, no package).
  *Limits:* triangles only; metallic-roughness texture, TEXCOORD_1 (one UV set
  sampled), orthographic cameras, animations, skins, morph targets ignored.
  DTOs in `Gltf/GltfDom.cs`.

### `Rendering` — the pipeline
- `RenderPipeline.Render(fb, db, scene, settings)` — the orchestrator. First
  dispatches on `settings.Engine`: `Raytrace` → `Raytracing.RayTracer.Render`
  (depth buffer unused), otherwise the rasterizer below.
- `Rasterizer` — `DrawLine`, `FillTriangle` (uint and generic `<TShader>`),
  incremental edges, per-scanline spans, horizontal band clamp, perspective-
  correct weights. The generic fill depth-tests (peek) before shading and writes
  depth only if the shader didn't discard (a **negative alpha** from `Shade` =
  discard, used for alpha-test); equivalent to test-and-write for opaque shaders.
- `RenderMesh` / `RenderVertex` (Position/Normal/UV/**Tangent**/**Handedness**/
  **Color**) / `RenderMeshBuilder` (dedups vertices; **generates tangents** from
  UVs, or uses `Mesh.Tangents` when supplied; copies `Mesh.Colors` per vertex,
  defaulting to white).
- `IPixelShader` — `Shade(b0,b1,b2)`; implemented by struct shaders for
  zero-alloc, inlinable, perspective-correct shading.
- `Lighting.Shade(...)` — ambient + per-light diffuse/specular (× shadow) +
  emissive; handles point/directional/spot. Optional `occlusion` (scales
  ambient+lit, not emissive), `twoSided` (flips the normal toward the viewer),
  `unlit` (returns the base color directly). The BRDF is in `ShadeCore<TVis>`
  with a **zero-alloc generic visibility hook** `IVisibility`: the raster passes
  `ShadowMapVisibility` (samples the shadow-map list), the ray tracer passes
  `RayShadowVisibility` (casts shadow rays). One shared BRDF, two shadow sources.
  Debug: `DebugShadowVisualize` (from `RenderSettings.DebugShadow`, script
  `debugshadow=on`) makes it return the per-light shadow factor as grayscale —
  the same path on both engines, so their shadows can be compared directly.
- `SurfaceShading.Shade<TVis>(...)` — material/texture surface evaluation
  (albedo + diffuse/specular/emissive maps + normal/height map + occlusion +
  alpha-mask + two-sided/unlit) shared by the raster's `TextureShader` and the
  ray tracer, so both interpret materials identically. Calls `Lighting.Shade`.
  Outputs the perturbed shading normal (`out shadingNormal`) so the ray tracer
  reflects/refracts off the normal-mapped surface, not the interpolated normal.
- `Supersampler.Downsample(fb, factor)` — SSAA: box-averages a `factor×factor`
  oversized framebuffer down to output size (engine-agnostic; see funnel below).
- `RenderSettings` — `Engine` (Raster/Raytrace), shading mode, backface cull,
  all shadow knobs (see below), `Supersampling` (SSAA factor), `LinearizeInput`
  + `EncodeSrgb` (gamma), and ray-tracer knobs
  `MaxBounces`/`RayShadows`/`ShadowSamples`. Factory helpers
  `Flat()/Gouraud()/Phong()/WireFrame()`.
- `ShadingMode` — Wireframe/Flat/Gouraud/Phong.
- `ShadowMap` — per-light depth map + `Sample(worldPos, normal, L)`. Stores
  **linear light-space depth** (light-view Z), not perspective NDC z, so the
  occluder/receiver comparison has uniform precision; PCF or PCSS, small
  linear-units bias + normal-offset. (NDC z crammed its precision near the light,
  forcing a bias so large it bored a bright hole in contact shadows.)
- `ShadowMapRenderer` — `BuildLightMatrix(light, min, max, out worldExtent, out
  view)` (ortho/perspective, tight near/far via `DepthRange`) and `Render(...)`:
  reuses `Rasterizer` for the texel coverage (perspective screen xy) but writes
  the **linear light-view Z** as the depth value; front-face culling.

### `Rendering/Raytracing` — the ray tracer
A Whitted ray tracer that consumes the **same** `Scene`/`Camera`/`Light`/
`Material`/`Texture2D` as the rasterizer and writes the same `FrameBuffer`, so
it slots into the `App` funnel (profiler/stats/output stay shared). Selected via
`RenderSettings.Engine = Raytrace`.
- `Ray` / `RayHit` — half-line (origin + normalized direction) and the nearest-
  hit record (position, interpolated normal/tangent/handedness/uv/color, material).
- `RayCamera` — primary rays matching the raster camera exactly (LH basis from
  `CreateLookAt`, vertical FOV from `CreatePerspective`, top-left pixel origin),
  so silhouettes line up. Supports sub-pixel jitter (unused by SSAA).
- `Aabb` — AABB with a slab ray test (precomputed `invDir`).
- `RayScene` — world-space triangle soup (bakes `SceneObject.Transform` like the
  raster's `ProjectTriangle`) + a **BVH** (midpoint split on the largest centroid
  axis, median fallback; flat node array; allocation-free iterative traversal via
  `stackalloc`). `Intersect` (nearest, Möller–Trumbore, two-sided) and
  `IntersectAny` (shadow-ray any-hit). Exposes scene `Bounds`.
- `RayShadowVisibility` (`IVisibility`) — shadow factor per light: shadow ray
  toward the light (finite for point/spot, infinite for directional), gated by
  `Light.CastsShadows` + `RenderSettings.RayShadows`, normal-offset bias scaled
  to the scene diagonal. **Soft shadows** when `ShadowSoftness > 0`: averages
  `ShadowSamples` rays over a disk around the light (Fibonacci pattern rotated by
  a per-point hash → noise not banding). **Transparent shadows**: opaque scenes
  use the cheap `IntersectAny`; otherwise the ray walks transparent surfaces
  accumulating transmittance (glass `Transmission`, blend `1-alpha`, masked
  cutout) until an opaque hit.
- `RayTracer` — recursive `Trace(ray, depth)` to `MaxBounces`. Skips alpha-test
  **Mask** cutout fragments (pass-through, no bounce), shades the hit via the
  shared `SurfaceShading` (+ shadow rays), then: **refraction** for transmissive
  materials (dielectric: Fresnel split into reflected + refracted rays, Snell +
  total-internal-reflection, blended by `Transmission`); **alpha-Blend**
  composite (`local·a + behind·(1-a)` via a continuation ray); **reflection** for
  glossy materials (gloss = `smoothstep(24,160, Shininess)`, Fresnel-Schlick with
  `F0 = SpecularColor`, blended `local·(1-kr)+reflected·kr`). Reflection and
  refraction use the **perturbed** shading normal from `SurfaceShading`, so
  normal/height-mapped surfaces reflect/refract their detail. Misses return the
  background. `Parallel.For` over disjoint rows.

### `Scripting`
- `SceneScript.Run(path)` — lexer (quote-aware) + per-line dispatch; builds a
  `Scene` + `RenderSettings` and renders. World matrix for `rotate world`.
  Includes a stateful **mesh authoring** mode (`MeshAuthoring`): `mesh begin` …
  `vertex`/`face`/`quad` … `mesh end`. Unified vertices (position + optional
  uv/normal share one 1-based index, negatives relative — OBJ-style); faces are
  fan-triangulated; `mesh end smooth=on` regenerates normals via `MeshUtils`.
  Multiple named cameras are tracked in `Scene.Cameras`; the most recently
  defined is active, and a comfy camera is auto-created only when none exist
  (`Log.Debug` reports the chosen camera). `listcameras`/`listobjects` print the
  current scene state. Backslash (`\`) at end of line continues onto the next.
- `ScriptArguments` — typed `key=value` accessors with defaults; errors carry
  the line number.

### `App` — application layer
- `SceneRenderer.Render(scene, settings, output, w, h, background)` — the
  **single funnel**: creates buffers, renders, saves, prints profiler + stats.
  Applies **supersampling** here (engine-agnostic): renders into a
  `w·ss × h·ss` buffer then `Supersampler.Downsample`s to `w × h`
  (`ss = RenderSettings.Supersampling`, clamped 1–4). Sets the `ColorSpace`
  gamma flags, decodes the background to linear at clear (when `LinearizeInput`)
  and runs the final linear->sRGB `EncodeToSrgb` pass after the downsample (when
  `EncodeSrgb`). `RenderObjFile(obj, output)` is the OBJ quick path (comfy
  camera/light, Flat).
- `SceneStats` — counts cameras/lights/objects/vertices/triangles/textures and
  estimates memory.
- `Log.Debug(...)` — `[Conditional("DEBUG")]`, prints to stdout in Debug only.

### `Program.cs`
CLI dispatcher only: `.rrr` → `SceneScript.Run`; otherwise →
`SceneRenderer.RenderModelFile` (any supported model via `ModelImporter`).

## Data flow

```
CLI (Program.cs)
 ├─ *.rrr             → SceneScript.Run ─┐
 └─ *.obj/stl/gltf/glb → SceneRenderer.RenderModelFile (ModelImporter) ─┐
                                          ▼
                         SceneRenderer.Render(scene, settings, ...)
                          (Profiler + SceneStats; SSAA: render at w·ss × h·ss)
                                          ▼
                         RenderPipeline.Render(fb, db, scene, settings)
                          │
                          ├─ Engine = Raster:
                          │   1) BuildShadowMaps (one per shadow-casting light)
                          │   2) untextured pass  (Flat/Gouraud/Phong shader)
                          │   3) textured pass    (TextureShader)
                          │      → Rasterizer.FillTriangle → FrameBuffer
                          │
                          └─ Engine = Raytrace:
                              RayTracer.Render → BVH trace per pixel
                              (shade + shadow rays + reflection/refraction)
                                          ▼
                         Supersampler.Downsample (if ss > 1) → ImageWriter.Save
```

## Rasterization pipeline (shaded)

Two phases inside `RenderShaded<TShader>`:

1. **Transform (serial):** `ProjectTriangle` runs the vertex shader, near-plane
   reject, perspective divide, viewport transform, backface culling; outputs
   screen coords, `1/w` per vertex, world positions/normals/tangents and UVs.
   A per-triangle struct shader is built (`MakeFlat/Gouraud/Phong/Texture`) and
   queued into a `List<ScreenTriangle<TShader>>`.
2. **Raster (parallel):** `Parallel.For` over horizontal **bands**
   (`ProcessorCount × 4`). Each band owns disjoint rows of the frame/depth
   buffers, so no locks; within a band triangles keep scene order → output is
   identical to the serial renderer at any thread count.

Shaders are `struct`s implementing `IPixelShader`, passed through the generic
`FillTriangle<TShader>` so the JIT monomorphizes and inlines `Shade` (no
per-triangle allocation, no virtual call). The rasterizer hands `Shade` the
**perspective-correct** barycentric weights.

Textured objects (any of diffuse/specular/emissive/normal maps) render in a
separate pass with `TextureShader`; opaque + z-buffer makes the two passes
order-independent.

**Transparency** (`alphaMode = BLEND`) is a third, final pass routed through
`TextureShader` (which also covers untextured transparents). Its triangles are
**sorted back-to-front** by NDC depth and rasterized with the depth test on but
**depth writes off**, alpha-blending src-over into the single framebuffer (no
per-layer buffers). Sorting is per-triangle (the standard real-time
approximation; interpenetrating transparents may composite in the wrong order).

## Shadow system

For each shadow-casting light (`BuildShadowMaps`):
1. `ShadowMapRenderer.BuildLightMatrix` builds the light view-projection:
   directional → orthographic, spot → perspective (fov = full cone), point →
   perspective aimed at the scene center. Near/far are tightened by projecting
   the 8 bbox corners (`DepthRange`) for depth precision. Returns `worldExtent`
   (→ world texel size for the normal-offset). For perspective lights this is the
   **angular** texel size `fov·dist`, not `2·tan(fov/2)·dist` — the latter
   over-estimates the texel size at wide FOVs (a large floor inflates the
   bounding sphere → FOV clamps near 160° → `tan` blows up ~4×), which
   over-pushes the normal-offset and leaks light through contact shadows.
2. `ShadowMapRenderer.Render` rasterizes the scene into a `DepthBuffer` storing
   **linear light-view Z** (not NDC z), with **front-face culling** (back faces
   only → second-depth, which also keeps the lit occluder surface out of its own
   map, avoiding self-shadow acne).
3. The resulting `ShadowMap` is sampled in `Lighting.Shade` per light:
   - The receiver's **linear** light-view Z is compared to the stored occluder
     distance. Because precision is uniform, a **small constant bias** (~½ texel
     in world units) removes residual acne without leaking — unlike NDC z, where
     the required bias was huge at the occluder distance and leaked the umbra.
   - **Normal-offset**: sample pushed along the normal ~1 texel (× `1 + 2·slope`)
     to keep the contact line off the marginal comparison zone.
   - **PCF** (`ShadowPcfRadius`) or, if `ShadowSoftness > 0`, **PCSS** (blocker
     search → penumbra estimate → variable-radius PCF; kernel capped at 12).
   - *Residual:* the contact shadow edge can stair-step (shadow-map aliasing at
     grazing angles); raise `ShadowMapResolution` or rely on PCF/PCSS softening.

Shadow settings live in `RenderSettings`: `ShadowsEnabled`,
`ShadowMapResolution`, `ShadowPcfRadius`, `ShadowFrontFaceCull`,
`ShadowSoftness`. Per-light: `Light.CastsShadows`.

## Conventions & invariants

- **Handedness:** left-handed. `CreateLookAt` z = forward; projection maps z to
  **[0,1]**. World +X → screen right, +Y → screen up (viewport flips Y).
- **Backface culling:** front faces have **negative** screen-space signed area;
  back faces (≥ 0) are culled. Shadow pass culls the opposite (front faces).
- **Winding:** outward CCW. Primitives auto-orient triangles outward.
- **Color packing:** `uint` = AARRGGBB (`PixelPacker`).
- **UV origin:** top-left (`v=0` at the top), matching frame-buffer rows; OBJ v
  is flipped at import.
- **Normals:** if a mesh lacks per-vertex normals, the geometric face normal is
  used as a fallback (so unlit OBJs still shade).
- **Gamma (on by default; `LinearizeInput` + `EncodeSrgb`):** color inputs are
  decoded sRGB->linear (diffuse/emissive textures **and** flat diffuse/emissive
  colors **and** the background), lighting/reflection/refraction/SSAA happen in
  linear, and the final image is encoded linear->sRGB once after the downsample.
  normal/height/specular/occlusion maps and specular color are **linear data** —
  never decoded. The two directions switch independently; both off = identity
  everywhere (pre-gamma behaviour).

## Known limitations

- `Scene.GetBoundingBox()` uses **local** positions; `comfy` camera/light and
  shadow framing ignore object transforms / the script world matrix.
- Point-light shadows are a single perspective map (a cone), not an
  omnidirectional cube map (intentionally dropped for the rasterizer).
- PCSS visible penumbra is bounded by the kernel cap (12 texels) and the
  shadow-map resolution; cost grows with the kernel.
- Gamma correction is **on by default** (independently switchable via
  `LinearizeInput`/`EncodeSrgb`); the 8-bit linear intermediate framebuffer can
  band slightly in deep shadows — a float render target would remove that
  (future work). `linearize=off srgb=off` reproduces the old uncorrected look.
- Ray tracer: transparency is handled for both camera/secondary rays
  (transmission/refraction, alpha-blend composite, masked cutout) **and shadows**
  (shadow rays accumulate transmittance through glass/blend/cutout). Shadow
  transmittance is **scalar** (no colored/tinted shadows) and there are **no
  caustics** (no light focusing through refraction). Reflection-ray misses
  return the flat background (no environment map); BVH uses a midpoint split
  (no SAH). See `docs/RAYTRACER_ROADMAP.md`.
- A leftover sample type (`Scene/SphereScene.cs`, `MeshUtils`, `Vertex`) may be
  unused scaffolding.

## Ray tracer (implemented)

A Whitted ray tracer (`Rendering/Raytracing`, selected by
`RenderSettings.Engine = Raytrace`, scriptable via `rendering engine=raytrace`)
shares the entire scene model with the rasterizer:
- **Insertion point:** `RenderPipeline.Render` dispatches on `Engine`;
  `SceneRenderer.Render` (buffers, SSAA, profiler, stats, output) is unchanged.
- **Reuse:** `VMath`, `Scene`, `Material`/`Texture2D` sampling, and — crucially —
  the same `Lighting.Shade` BRDF and the same `SurfaceShading` material/texture
  evaluation, so a non-reflective, non-transmissive material renders comparably
  to the raster. The shadow-map system stays rasterizer-specific; the tracer gets
  shadows from shadow rays via the shared `IVisibility` hook.
- **Features:** BVH acceleration; hard + soft (area-light) + transparency-
  attenuated shadows; recursive
  reflections (reflectivity derived from `SpecularColor`+`Shininess`, no new
  Scene field beyond the additive transmission/ior); dielectric refraction
  (`Transmission`/`IndexOfRefraction`, from glTF `KHR_materials_transmission`/
  `_ior`); alpha-test (Mask) cutout pass-through and alpha-Blend compositing to
  match the raster's transparency passes; SSAA shared with the raster.
- **Roadmap / remaining gaps** are tracked in `docs/RAYTRACER_ROADMAP.md`
  (alpha-blend & masked transparency in the tracer, transparent/colored shadows
  through glass, reflections off normal-mapped surfaces, environment map, gamma).

## Build & run

```
dotnet build
rrr.exe scene.rrr
rrr.exe model.obj out.png
```
Debug builds print `Log.Debug` lines (e.g. bounding box); Release omits them.
