# RRR — 3D Raster Renderer

RRR is a small, dependency-free 3D renderer written in C# (.NET 8) that runs
from the command line. It loads 3D models, lights and shades them, and writes
the result to an image file. Everything — image decoding/encoding, math,
rasterization, shading — is implemented from scratch, with **no external
libraries**, so there are no licensing or update concerns.

The renderer is built in stages. The software rasterizer is the current core;
a ray tracer and a GPU backend are planned.

```
rrr.exe model.obj [output.bmp]     # quick render of a single model
rrr.exe scene.rrr                  # run a scene script
```

## Features

- **Software rasterizer** with a z-buffer, barycentric rasterization with
  incremental edge functions and per-scanline spans, and a **multi-threaded**
  fill stage (horizontal bands) that is deterministic regardless of thread
  count.
- **Shading modes:** Flat, Gouraud, Phong (per-pixel), plus Wireframe.
- **Lighting:** point and directional lights, Lambert diffuse + ambient +
  Blinn-Phong specular.
- **Materials & maps** (per-pixel, perspective-correct interpolation):
  - Diffuse color / **diffuse map**
  - Specular color + exponent / **specular map**
  - **Emissive** color / **emissive map**
  - **Normal maps** (tangent-space) and **height/bump maps**, with automatic
    detection of grayscale (height) vs. colored (normal) maps, and an
    `invertgreen` flag for DirectX-style normal maps
  - Solid color fallback (or a stable random color when no material is present)
- **Texture sampling:** nearest and bilinear filtering, repeat/clamp wrapping.
- **Procedural primitives:** plane, cube, sphere, cylinder, cone, pyramid.
- **OBJ + MTL loading**, including material libraries and texture paths
  resolved relative to the model.
- **Scene scripting** via the `.rrr` format (see below).
- **Profiling & scene statistics** printed after every render.

## Supported formats

**Models (input)**

| Format                                | Status       |
|---------------------------------------|--------------|
| Wavefront OBJ (`.obj`) + MTL (`.mtl`) | ✅ Supported |

**Images — textures (input)**

| Format | Notes                                                                                                                |
|--------|----------------------------------------------------------------------------------------------------------------------|
| BMP    | 24-bit BGR and 32-bit BGRA, uncompressed (`BI_RGB`)                                                                  |
| PNG    | 8-bit grayscale / gray+alpha / RGB / RGBA, and 1/2/4/8-bit palette (with `tRNS`); 16-bit and interlaced are rejected |

**Images — output**

| Format | Notes      |
|--------|------------|
| BMP    | 32-bit     |
| PNG    | 8-bit RGBA |

Everything is converted internally to an RGBA32 frame buffer.

## Roadmap

Planned next:

- **Ray tracer** (RRR is designed in stages: rasterizer → ray tracer → GPU).
- **STL** model import.
- **glTF** model import.
- Shadow maps.

## Architecture

The code is organized into namespaces:

- `Core` — frame buffer, depth buffer, colors, pixel packing, profiler.
- `VMath` — vectors (2/3/4) and 4×4 matrices.
- `Imaging` — image readers/writers (BMP, PNG).
- `Scene` — scene graph, meshes, camera, lights, materials, primitives.
- `Importers` — OBJ and MTL importers.
- `Rendering` — the rasterization and shading pipeline.
- `Scripting` — the `.rrr` scene-script language.
- `App` — CLI entry points, shared renderer, logging, scene statistics.

---

# `.rrr` Scene Script — User Manual

A `.rrr` file is an **imperative** script: one command per line, executed in
order. There are no loops, conditionals, or variables — just a sequence of
actions that build a scene and render it.

Run it with:

```
rrr.exe scene.rrr
```

## Syntax

```
command [subtype] key=value key=value ...
```

- One command per line. Blank lines are ignored.
- `#` starts a comment (rest of the line is ignored).
- **Every argument is optional** and has a sensible default.
- Values:
  - number: `60`, `0.1`, `-2.5`
  - vector: `x,y,z` (e.g. `position=0,5,-10`) — no spaces
  - color: `r,g,b` or `r,g,b,a` (0..1), or hex `#rrggbb`
  - boolean: `on`/`off`, `true`/`false`, `yes`/`no`, `1`/`0`
  - string: quote it if it contains spaces — `file="my model.obj"`
- Objects and materials are referenced **by name**.
- Angles are in **degrees**; coordinates are in logical units.

If the script never issues a `render` command, the scene is rendered at the
end to `<scriptname>.bmp`.

## Commands

### `rendering` — global render settings
```
rendering shading=phong backface=on width=1920 height=1080 background=0.1,0.1,0.15
```
| key                | default         | meaning                                       |
|--------------------|-----------------|-----------------------------------------------|
| `shading`          | `flat`          | `wireframe` \| `flat` \| `gouraud` \| `phong` |
| `backface`         | `on`            | backface culling                              |
| `width` / `height` | `1920` / `1080` | output resolution                             |
| `background`       | `0,0,0`         | clear color                                   |

### `load obj` — load a model
```
load obj file="model.obj" name="hero" material="steel"
```
| key | default | meaning |
|-----|---------|---------|
| `file` | *(required)* | path to the `.obj` |
| `name` | file name | handle used to reference it later |
| `material` | *(from MTL)* | optional override material name |

### `mesh` — create a primitive
```
mesh plane    name="floor" size=20,20
mesh cube     name="box"   size=1,1,1
mesh sphere   name="ball"  radius=1 segments=48 rings=24
mesh cylinder name="pipe"  radius=0.5 height=2 segments=32
mesh cone     name="tip"   radius=0.5 height=2 segments=32
mesh pyramid  name="pyr"   base=1,1 height=1.5
```
Common keys: `name` (auto-generated if omitted), `material`. Each primitive has
its own size keys with defaults (radius `1`, height `2`, etc.).

### Transforms — `translate` / `rotate` / `scale`
Applied to a named object (default: the most recently created one), and they
**accumulate**. Use the `world` subtype to transform the whole scene.
```
translate name="ball" by=0,1,0
rotate    name="box"  axis=y angle=205      # axis = x | y | z
rotate    name="box"  euler=90,205,0        # or all three at once
scale     name="box"  by=2                  # uniform
scale     name="box"  by=2,1,2              # per-axis
rotate    world       axis=x angle=90       # rotate everything
```

### `material` — define or modify a material (by name)
```
material name="steel" diffuse=0.8,0.8,0.9 specular=1,1,1 shininess=64
material name="brick" texture="brick_d.png" normalmap="brick_n.png" bumpscale=1
material name="lava"  emissive=1,0.5,0.1 emissivemap="lava_e.png"
```
| key | meaning |
|-----|---------|
| `diffuse` | diffuse color |
| `specular` | specular color |
| `shininess` | Blinn-Phong exponent (`0` = no specular) |
| `emissive` | self-illumination color |
| `texture` | diffuse map |
| `specmap` | specular map |
| `emissivemap` | emissive map |
| `normalmap` | tangent-space normal map |
| `heightmap` | grayscale height/bump map |
| `bumpscale` | normal/bump strength (default `1`) |
| `invertgreen` | flip the normal-map green channel (DirectX-style) |

Re-issuing `material` with the same `name` updates it.

### `assign` — give a material to an object
```
assign material="steel" to="ball"
```
Defaults: `material` = last defined, `to` = last created object.

### `camera`
```
camera comfy                                 # auto-frame the scene
camera position=0,5,-12 target=0,1,0 fov=60  # explicit
```
| key | default | meaning |
|-----|---------|---------|
| `position` | `0,2,-5` | eye position |
| `target` | `0,0,0` | look-at point |
| `up` | `0,1,0` | up vector |
| `fov` | `60` | vertical field of view (degrees) |
| `near` / `far` | `0.1` / `1000` | clip planes |

`camera comfy` (or `camera` with no arguments) frames the scene automatically.

### `light`
```
light point       name="key" position=5,8,-5 color=1,1,1 intensity=1.2
light directional name="sun" direction=-1,-1,-0.5 intensity=0.5
light comfy                                  # auto-placed point light
```
| subtype | keys |
|---------|------|
| `point` | `position`, `color`, `intensity` |
| `directional` | `direction`, `color`, `intensity` |
| `comfy` | none (auto-placed) |

### `render` — render and save
```
render file="out.png"
```
Defaults: `file` = `<scriptname>.bmp`. You may render multiple times in one
script. If omitted, an automatic render happens at end of file.

## Example

```
# A textured model and a primitive on a lit stage
rendering shading=phong backface=on width=1280 height=720 background=0.1,0.1,0.15

load obj file="hero.obj" name="hero"
mesh plane name="floor" size=40,40

material name="floor_mat" diffuse=0.6,0.6,0.6
assign material="floor_mat" to="floor"

translate name="hero" by=0,1,0
rotate    name="hero" axis=y angle=30

light point name="key" position=8,12,-8 intensity=1.5
light directional name="fill" direction=1,-1,1 intensity=0.3
camera comfy

render file="hero.png"
```
