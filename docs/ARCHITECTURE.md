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
- `ColorSpace` — `SrgbToLinear` / `LinearToSrgb` **placeholders (identity)**;
  call sites exist (texture sampling) for future gamma-correct lighting.
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
  top-left). PBR metallic-roughness → Blinn-Phong (`baseColorFactor`→diffuse,
  `roughnessFactor`→shininess, `emissiveFactor` × `KHR_materials_emissive_strength`,
  `occlusionTexture`, `alphaMode`/`alphaCutoff`, `doubleSided`,
  `KHR_materials_unlit`, base/emissive/normal textures).
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
- `RenderPipeline.Render(fb, db, scene, settings)` — the orchestrator.
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
  emissive; handles point/directional/spot; takes the parallel shadow-map list.
  Optional `occlusion` (scales ambient+lit, not emissive), `twoSided` (flips the
  normal toward the viewer), `unlit` (returns the base color directly).
- `RenderSettings` — shading mode, backface cull, and all shadow knobs (see
  below). Factory helpers `Flat()/Gouraud()/Phong()/WireFrame()`.
- `ShadingMode` — Wireframe/Flat/Gouraud/Phong.
- `ShadowMap` — per-light depth map + `Sample(worldPos, normal, L)` (normal-
  offset bias, small depth bias, PCF or PCSS).
- `ShadowMapRenderer` — `BuildLightMatrix(light, min, max, out worldExtent)`
  (ortho/perspective, tight near/far via `DepthRange` over the bbox corners),
  and `Render(...)` (reuses `Rasterizer` to fill a depth buffer; front-face
  culling).

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
  `RenderObjFile(obj, output)` is the OBJ quick path (comfy camera/light, Flat).
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
                          (Profiler + SceneStats)
                                          ▼
                         RenderPipeline.Render(fb, db, scene, settings)
                          1) BuildShadowMaps (one per shadow-casting light)
                          2) untextured pass  (Flat/Gouraud/Phong shader)
                          3) textured pass    (TextureShader)
                                          ▼
                         Rasterizer.FillTriangle → FrameBuffer
                                          ▼
                         ImageWriter.Save(fb, output)
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
   (→ world texel size for the normal-offset).
2. `ShadowMapRenderer.Render` rasterizes scene depth into a `DepthBuffer`
   (reusing `Rasterizer`), with **front-face culling** (back faces only).
3. The resulting `ShadowMap` is sampled in `Lighting.Shade` per light:
   - **Normal-offset bias**: sample point pushed along the normal by a few world
     texels (× PCF radius, × grazing).
   - Small constant depth bias.
   - **PCF** (`ShadowPcfRadius`) or, if `ShadowSoftness > 0`, **PCSS** (blocker
     search → penumbra estimate → variable-radius PCF; kernel capped at 12).

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
- **Maps are linear data:** normal/height/specular maps are sampled without the
  sRGB placeholder; diffuse/emissive go through `ColorSpace.SrgbToLinear`
  (currently identity).

## Known limitations

- `Scene.GetBoundingBox()` uses **local** positions; `comfy` camera/light and
  shadow framing ignore object transforms / the script world matrix.
- Point-light shadows are a single perspective map (a cone), not an
  omnidirectional cube map (intentionally dropped for the rasterizer).
- PCSS visible penumbra is bounded by the kernel cap (12 texels) and the
  shadow-map resolution; cost grows with the kernel.
- `ColorSpace` is a no-op placeholder (no gamma-correct lighting yet).
- A leftover sample type (`Scene/SphereScene.cs`, `MeshUtils`, `Vertex`) may be
  unused scaffolding.

## Extending with the ray tracer (planned)

The clean insertion point is the **`App` funnel** and `RenderSettings`:
- Add an engine selector (e.g. `RenderSettings.Engine = Raster | Raytrace`),
  set from the CLI and from a `.rrr` `rendering engine=...` argument.
- Implement a ray tracer that consumes the same `Scene` (+ `Camera`, `Lights`,
  `Material`/`Texture2D`) and produces a `FrameBuffer`.
- `SceneRenderer.Render` dispatches to rasterizer or ray tracer based on the
  engine; profiling, stats and image output stay shared.
- Reuse: `VMath`, `Scene`, `Material`/`Texture2D` sampling, `Lighting` math
  (diffuse/specular/emissive), `ImageWriter`. Shadows in a ray tracer come for
  free from shadow rays (the shadow-map system is rasterizer-specific).

## Build & run

```
dotnet build
rrr.exe scene.rrr
rrr.exe model.obj out.png
```
Debug builds print `Log.Debug` lines (e.g. bounding box); Release omits them.
