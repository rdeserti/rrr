# RRR — 3D Renderer

RRR is a small, dependency-free 3D renderer written in C# (.NET 8) that runs
from the command line. It loads 3D models, lights, shades and shadows them, and
writes the result to an image file. Everything — image decoding/encoding, math,
rasterization, **ray tracing**, shading, shadow mapping — is implemented from
scratch, with **no external libraries**, so there are no licensing or update
concerns.

It ships **two complete rendering engines** that consume the same scene,
materials and textures and are selectable per render (CLI or `.rrr` script):

- a **software rasterizer** (shadow maps), and
- a **ray tracer** (`engine=raytrace`) with shadow rays, reflections and
  refraction.

A non-reflective, non-transmissive scene renders comparably on both. A GPU
backend (hybrid rasterizer + ray tracing) is the planned next step.

```
rrr.exe model.obj [output.bmp]     # quick render of a single model
rrr.exe model.stl                  # also .gltf / .glb
rrr.exe scene.rrr                  # run a scene script (chooses the engine)
```

See [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for the internals and
[`docs/RRR_SCRIPT.md`](docs/RRR_SCRIPT.md) for the full `.rrr` scripting manual.

## Features

- **Software rasterizer** with a z-buffer, barycentric rasterization with
  incremental edge functions and per-scanline spans, perspective-correct
  attribute interpolation, and a **multi-threaded** fill stage (horizontal
  bands) that is deterministic regardless of thread count.
- **Shading modes:** Flat, Gouraud, Phong (per-pixel), plus Wireframe.
- **Ray tracer** (`engine=raytrace`), sharing the same scene/materials/textures:
  - **BVH** acceleration (pure C#, iterative traversal), Möller–Trumbore
    intersection, multi-threaded over pixels.
  - **Shadow rays** — hard, **soft** (area-light sampling), and **transparent**
    (attenuated through glass / alpha-blend / masked cutouts).
  - **Reflections** (recursive Whitted), reflectivity derived from
    specular + shininess, with a Fresnel edge; **normal-mapped** surfaces reflect
    their detail.
  - **Refraction** (dielectric: Snell + Fresnel + total-internal-reflection),
    from `transmission`/`ior` (and glTF `KHR_materials_transmission`/`_ior`).
  - Alpha-blend compositing and masked cutout pass-through; misses return the
    background as the environment.
  - Uses the **same** `Lighting` BRDF and material/texture evaluation as the
    rasterizer, so opaque, non-reflective scenes are directly comparable.
- **Lights:** point, directional, and **spot** (cone with soft inner/outer
  edge). Lambert diffuse + ambient + Blinn-Phong specular.
- **Shadow mapping:**
  - Directional (orthographic), spot (perspective, matched to the cone), and
    point (single perspective map aimed at the scene — not omnidirectional).
  - **PCF** soft edges (configurable kernel).
  - **PCSS** contact-hardening soft shadows (variable penumbra), optional.
  - **Linear light-space depth** (uniform precision) + normal-offset bias +
    **front-face culling** in the shadow pass to suppress acne without
    peter-panning or umbra leaks.
  - Per-light shadow toggle; global enable/quality settings.
  - *(Ray-traced shadows — hard/soft/transparent — are available via the ray
    tracer, see above.)*
- **Materials & maps** (per-pixel, perspective-correct):
  - Diffuse color / **diffuse map**
  - Specular color + exponent / **specular map**
  - **Emissive** color / **emissive map**
  - **Normal maps** (tangent-space) and **height/bump maps**, with automatic
    grayscale-vs-normal detection and an `invertgreen` flag for DirectX-style
    normal maps
  - **Ambient-occlusion maps**, **alpha-test** (mask + cutoff), **alpha
    blending** (sorted back-to-front transparency), **double-sided** materials,
    and **unlit** materials
  - **Transmission / index of refraction** (ray-tracer refraction; ignored by
    the rasterizer)
  - **Per-vertex colors** (modulate the albedo)
  - Solid-color fallback (or a stable random color when no material is present)
- **Anti-aliasing:** supersampling (SSAA), engine-agnostic (`aa=2`/`3`).
- **Gamma-correct rendering** (on by default): sRGB→linear decode of color
  inputs, lighting in linear space, linear→sRGB encode of the output; each
  direction independently switchable.
- **Texture sampling:** nearest / bilinear, repeat / clamp wrapping.
- **Generated UVs:** box/triplanar projection (`load … uv=box`) to texture
  models that ship without texture coordinates (e.g. STL).
- **Procedural primitives:** plane, cube, sphere, cylinder, cone, pyramid, plus
  **hand-authored meshes** (`mesh begin … vertex/face/quad … mesh end`).
- **Model loading:** Wavefront **OBJ + MTL**, **STL** (binary & ASCII), and
  **glTF 2.0** (`.gltf` and `.glb`, PBR materials mapped to Blinn-Phong —
  including alpha-test, double-sided, occlusion, unlit and emissive-strength —
  plus perspective cameras and `KHR_lights_punctual` lights), behind a single
  extension-dispatched importer.
- **Scene scripting** via the imperative `.rrr` format.
- **Profiling & scene statistics** printed after every render.

## Supported formats

**Models (input):** Wavefront OBJ (`.obj`) + MTL (`.mtl`), STL (`.stl`, binary
and ASCII), glTF 2.0 (`.gltf` and `.glb`).

**Textures (input):**
- BMP — 24-bit BGR and 32-bit BGRA, uncompressed (`BI_RGB`).
- PNG — 8-bit grayscale / gray+alpha / RGB / RGBA, and 1/2/4/8-bit palette
  (with `tRNS`). 16-bit and interlaced are rejected.
- JPEG — baseline and **progressive** (8-bit, Huffman), grayscale and YCbCr with
  any chroma subsampling. Arithmetic/12-bit/lossless are rejected.
- glTF textures may be embedded (bufferView / data-URI) or external files; all
  three image codecs above apply.

**Images (output):** BMP (32-bit), PNG (8-bit RGBA).

Everything is converted internally to an RGBA32 frame buffer.

## Roadmap

- **Ray tracer** — ✅ done (BVH, shadows, reflections, refraction, transparency).
- **GPU backend** — planned: a **hybrid** rasterizer + ray tracing engine
  (rasterized G-buffer for form/attached shadows; ray-traced cast shadows and
  reflections). Would require a GPU API binding — the one accepted exception to
  the no-dependencies rule.
- Minor: contact-shadow crescent in the raster shadow map at ~zero occluder gap
  (see `docs/RAYTRACER_ROADMAP.md`); a few more scripting commands.

## Quick example (`.rrr`)

```
rendering shading=phong shadows=on shadowpcf=2 width=1280 height=720 background=0.1,0.1,0.15

load obj file="hero.obj" name="hero"
mesh plane name="floor" size=40,40

material name="floor_mat" diffuse=0.6,0.6,0.6
assign material="floor_mat" to="floor"

rotate name="hero" axis=y angle=30
light spot name="key" position=-18,38,16 cone=26 inner=20 intensity=1.6
camera comfy

render file="hero.png"
```

## Ray-traced example (`.rrr`)

```
rendering engine=raytrace shadows=on shadowsoft=0.5 shadowsamples=24 bounces=8 \
          aa=2 width=1280 height=720 background=0.6,0.7,0.85

mesh plane name="floor" size=20,20
material name="floor" diffuse=0.4,0.4,0.45 specular=0.9,0.9,0.9 shininess=200  # reflective
assign material="floor" to="floor"
translate name="floor" by=0,-1,0

mesh sphere name="glass" radius=1.3 segments=64 rings=32
material name="glass" diffuse=0,0,0 transmission=1 ior=1.5                      # glass
assign material="glass" to="glass"

light point name="key" position=4,7,-5 intensity=1.4
camera position=0,2,-7 target=0,0,0 fov=50
render file="raytraced.png"
```

See [`demos/`](demos/) for the full two-engine tech demo (`techdemo_raster.rrr`
and `techdemo_raytrace.rrr`).
