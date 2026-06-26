# RRR — 3D Renderer

RRR is a small, dependency-free 3D renderer written in C# (.NET 8) that runs
from the command line. It loads 3D models, lights, shades and shadows them, and
writes the result to an image file. Everything — image decoding/encoding, math,
rasterization, shading, shadow mapping — is implemented from scratch, with **no
external libraries**, so there are no licensing or update concerns.

The renderer is built in stages. The **software rasterizer is complete**; a ray
tracer (an optional engine, selectable from the CLI and from scripts) is the
next milestone, followed by a GPU backend.

```
rrr.exe model.obj [output.bmp]     # quick render of a single model
rrr.exe model.stl                  # also .gltf / .glb
rrr.exe scene.rrr                  # run a scene script
```

See [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for the internals and
[`docs/RRR_SCRIPT.md`](docs/RRR_SCRIPT.md) for the full `.rrr` scripting manual.

## Features

- **Software rasterizer** with a z-buffer, barycentric rasterization with
  incremental edge functions and per-scanline spans, perspective-correct
  attribute interpolation, and a **multi-threaded** fill stage (horizontal
  bands) that is deterministic regardless of thread count.
- **Shading modes:** Flat, Gouraud, Phong (per-pixel), plus Wireframe.
- **Lights:** point, directional, and **spot** (cone with soft inner/outer
  edge). Lambert diffuse + ambient + Blinn-Phong specular.
- **Shadow mapping:**
  - Directional (orthographic), spot (perspective, matched to the cone), and
    point (single perspective map aimed at the scene — not omnidirectional).
  - **PCF** soft edges (configurable kernel).
  - **PCSS** contact-hardening soft shadows (variable penumbra), optional.
  - **Normal-offset bias** + **front-face culling** in the shadow pass to
    suppress acne without peter-panning; tight per-light near/far for depth
    precision.
  - Per-light shadow toggle; global enable/quality settings.
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
  - **Per-vertex colors** (modulate the albedo)
  - Solid-color fallback (or a stable random color when no material is present)
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

- **Ray tracer** — an optional engine, selectable from the CLI and `.rrr`,
  consuming the same `Scene`.
- A few more scripting commands.
- GPU backend.

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
