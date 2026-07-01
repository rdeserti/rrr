# `.rrr` Scene Script — Manual

A `.rrr` file is an **imperative** script: one command per line, executed in
order. No loops, conditionals, or variables — just a sequence of actions that
build a scene and render it.

```
rrr.exe scene.rrr
```

## Syntax

```
command [subtype] key=value key=value ...
```

- One command per line; blank lines ignored.
- A line ending with a backslash `\` **continues** on the next physical line
  (joined with a space), so long commands can be wrapped.
- `#` starts a comment (to end of line).
- **Every argument is optional** and has a default.
- Value types:
  - number: `60`, `0.1`, `-2.5`
  - vector: `x,y,z` (no spaces) — e.g. `position=0,5,-10`
  - color: `r,g,b` or `r,g,b,a` (0..1), or hex `#rrggbb`
  - boolean: `on`/`off`, `true`/`false`, `yes`/`no`, `1`/`0`
  - string: quote if it contains spaces — `file="my model.obj"`
- Objects and materials are referenced **by name**.
- Angles in **degrees**; coordinates in logical units.
- Paths are resolved relative to the script's folder.
- If no `render` command runs, the scene is rendered at end of file to
  `<scriptname>.bmp`.

---

## `rendering` — global settings

```
rendering engine=raster shading=phong backface=on width=1920 height=1080 background=0.1,0.1,0.15 \
          shadows=on shadowres=2048 shadowpcf=2 shadowcull=on shadowsoft=0 aa=1
```

| key | default | meaning |
|-----|---------|---------|
| `engine` | `raster` | `raster` (shadow-map rasterizer) \| `raytrace` (ray tracer: shadow rays + reflections + refraction) |
| `shading` | `flat` | `wireframe` \| `flat` \| `gouraud` \| `phong` — *raster only* (the ray tracer is always per-pixel) |
| `backface` | `on` | backface culling (raster only) |
| `width` / `height` | `1920` / `1080` | output resolution |
| `background` | `0,0,0` | clear color (also the ray tracer's environment for rays that miss) |
| `aa` (or `supersampling`) | `1` | supersampling factor (1 = off, 2 = 2×2, 3 = 3×3; clamped to 4). Renders at `width·aa × height·aa` then box-averages down. **Both engines.** |
| `linearize` | `on` | gamma — input linearization: decode color inputs (textures, flat colors, background) sRGB→linear before lighting. **Both engines.** |
| `srgb` | `on` | gamma — output reconstruction: encode the final image linear→sRGB. **Both engines.** Independent of `linearize`. |
| `debugshadow` | `off` | debug: output the per-light shadow factor as grayscale (white = lit, black = shadowed) instead of shaded color. **Both engines** — lets you compare the raster shadow map against ray-traced shadows directly. |
| `shadows` | `on` | enable shadows (shadow maps for raster, shadow rays for the tracer) |
| `shadowres` | `1024` | shadow map resolution (square) — *raster only* |
| `shadowpcf` | `1` | PCF kernel radius in texels (0 = hard, 1 = 3×3, …) — *raster only* |
| `shadowcull` | `on` | front-face culling in the shadow pass (anti-acne) — *raster only* |
| `shadowsoft` | `0` | soft-shadow light size; `0` = hard shadows on both engines |
| `shadowsamples` | `16` | *ray tracer only:* shadow rays per light when `shadowsoft > 0` |
| `bounces` | `4` | *ray tracer only:* max reflection/refraction recursion depth |
| `rayshadows` | `on` | *ray tracer only:* cast shadow rays (in addition to the global `shadows`) |

Notes:
- **`engine=raytrace`** runs the ray tracer over the **same** scene/materials/
  textures; non-reflective, non-transmissive materials render comparably to the
  raster. `shading=` is ignored (the tracer is always per-pixel, Phong-quality).
- `shadowsoft` is interpreted per engine: **raster** = PCSS apparent light size
  in shadow-map texels (enables variable-penumbra PCSS, overrides `shadowpcf`,
  kernel capped at 12); **ray tracer** = area-light radius in **world units**
  (point/spot) or angular spread (directional), sampled with `shadowsamples`
  rays. Pair with `aa` to clean residual penumbra noise.
- `shadowcull=off` if thin / single-sided meshes lose their shadows.
- Reflections and refraction are **ray-tracer only**; in the raster the same
  materials render opaque/non-reflective (see `material` below).
- Gamma correction is **on by default** (both `linearize` and `srgb`), so the
  renderer is physically correct out of the box (lighting in linear space,
  brighter mid-tones, softer light/shadow falloff) on both engines. The two
  switches are independent: `linearize=off srgb=off` reproduces the old
  uncorrected look; turning off only one gives an intentionally partial result
  (too dark, or washed out) useful for debugging.

---

## `load` — load a model (OBJ / STL / glTF / GLB)

```
load obj  file="model.obj"  name="hero" material="steel"
load stl  file="part.stl"   name="part"
load gltf file="scene.gltf" name="scene"
load glb  file="scene.glb"  name="scene"
```

| key | default | meaning |
|-----|---------|---------|
| `file` | *(required)* | path to the model |
| `name` | file name | handle for later reference (covers all sub-objects) |
| `material` | *(from file)* | optional material name to assign to all of it |
| `uv` | `none` | `box` (a.k.a. `planar`/`triplanar`) generates texture coords by planar projection — use it to texture a model that has no UVs (e.g. an STL) |
| `uvscale` | `1` | tiles per unit for generated UVs (`uv=box`) |

The subtype (`obj`/`stl`/`gltf`/`glb`) is just a readability hint — the format
is detected from the file extension, so `load file="x.glb"` also works.

`uv=box` projects each triangle onto the world plane perpendicular to its
dominant axis, so a tiling texture maps cleanly without an atlas (mild
distortion on slopes). Example: `load stl file="part.stl" uv=box uvscale=0.5`,
then `material`/`assign` a textured material.

Notes per format:
- **OBJ** — full v/vt/vn/f + MTL materials and textures (see also `material`).
- **STL** — geometry only (no UVs/materials); gets a plain grey material and a
  faceted look. CAD STLs are often Z-up, so you may need
  `rotate world axis=x angle=-90` to stand the model upright. To texture it, add
  `uv=box` so projected texture coordinates are generated.
- **glTF / GLB** — geometry + PBR materials mapped to Blinn-Phong; textures load
  from external files, data-URIs and embedded buffers (PNG, BMP, baseline JPEG).
  **Perspective cameras** and **`KHR_lights_punctual`** lights are imported too:
  an imported camera becomes active (a later `camera` command overrides it), and
  imported lights are added to the scene (they coexist with script lights).
  Physical light intensities are clamped to a usable range — tweak with your own
  `light` commands if needed. Right-handed→left-handed conversion is automatic.

---

## `mesh` — create a primitive

```
mesh plane    name="floor" size=20,20
mesh cube     name="box"   size=1,1,1
mesh sphere   name="ball"  radius=1 segments=48 rings=24
mesh cylinder name="pipe"  radius=0.5 height=2 segments=32
mesh cone     name="tip"   radius=0.5 height=2 segments=32
mesh pyramid  name="pyr"   base=1,1 height=1.5
```

Common keys: `name` (auto if omitted), `material`. Per-primitive size keys:

| primitive | keys (defaults) |
|-----------|-----------------|
| `plane` | `size=1,1` (X,Z) |
| `cube` | `size=1,1,1` |
| `sphere` | `radius=1`, `segments=32`, `rings=16` |
| `cylinder` | `radius=0.5`, `height=2`, `segments=32` |
| `cone` | `radius=0.5`, `height=2`, `segments=32` |
| `pyramid` | `base=1,1` (X,Z), `height=1.5` |

---

## `mesh begin` … `mesh end` — author a mesh by hand

Build an arbitrary mesh vertex-by-vertex, in the spirit of an `.obj` body. Open
a block with `mesh begin`, declare vertices and faces, then close with
`mesh end`. The finished mesh becomes a normal named object.

```
mesh begin name="gem" material="ruby"
  vertex pos=-1,0,-1 uv=0,0 color=1,0,0
  vertex pos=1,0,-1  uv=1,0 color=0,1,0
  vertex pos=1,0,1   uv=1,1 color=0,0,1
  vertex pos=-1,0,1  uv=0,1
  vertex pos=0,2,0                 # apex (uv/normal/color optional)
  face v=1,4,3,2                   # polygon, fan-triangulated
  face v=1,2,5
  face v=2,3,5
  face v=3,4,5
  face v=4,1,5
  quad v=1,2,3,4                   # convenience: exactly 4 indices
mesh end smooth=on
```

- `vertex pos=x,y,z [uv=u,v] [normal=nx,ny,nz] [color=r,g,b[,a]]` — appends one
  **unified vertex**; the optional `uv`/`normal`/`color` share that vertex's
  index. Per-vertex `color` modulates the material albedo (interpolated across
  the face); if only some vertices set it, the rest default to white. Remember
  the `.rrr` rule: **no spaces inside a value** (`pos=1,0,-1`, not `pos= 1, 0`).
- `face v=i,j,k[,l,…]` — a polygon by **1-based** vertex index (negatives count
  back from the current vertex, OBJ-style); fan-triangulated. Author the winding
  **CCW seen from outside** (no auto-flip).
- `quad v=i,j,k,l` — same as `face` but requires exactly four indices.
- `mesh end [smooth=on]` — finalizes. Normals: if `smooth=on`, smooth per-vertex
  normals are generated; else if **every** vertex supplied a `normal=`, those are
  used; otherwise the renderer falls back to per-face (flat) normals.

`name`, `material` on `mesh begin` behave like the primitive `mesh` command.

---

## Transforms — `translate` / `rotate` / `scale`

Applied to a named object (default: most recently created) and **accumulate**.
Use the `world` subtype to transform the whole scene.

```
translate name="ball" by=0,1,0
rotate    name="box"  axis=y angle=205      # axis = x | y | z
rotate    name="box"  euler=90,205,0        # all three at once
scale     name="box"  by=2                  # uniform
scale     name="box"  by=2,1,2              # per-axis
rotate    world       axis=x angle=90       # rotate everything
```

---

## `material` — define or modify a material (by name)

Re-issuing with the same `name` updates it.

```
material name="steel" diffuse=0.8,0.8,0.9 specular=1,1,1 shininess=64
material name="brick" texture="brick_d.png" normalmap="brick_n.png" bumpscale=1
material name="ground" heightmap="ground_h.png" bumpscale=8
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
| `occlusionmap` | grayscale ambient-occlusion map (darkens ambient + lit) |
| `bumpscale` | normal/bump strength (default `1`) |
| `invertgreen` | flip normal-map green channel (DirectX-style) |
| `doublesided` | render both faces, normal flipped toward the viewer (`on`/`off`) |
| `unlit` | output the base color directly, no lighting (`on`/`off`) |
| `alphamode` | `opaque` \| `mask` (alpha-test cutout) \| `blend` (alpha transparency; alpha from the diffuse color/texture). Both engines: the raster uses a sorted back-to-front pass; the ray tracer passes through masked fragments and composites blend src-over. |
| `alphacutoff` | alpha-test threshold for `alphamode=mask` (default `0.5`) |
| `transmission` | *ray tracer only:* fraction of light refracted through the surface, `0`..`1` (default `0` = opaque) |
| `ior` | *ray tracer only:* index of refraction for `transmission > 0` (default `1.5`; `1.0` = straight-through transparency, no bending) |

`map_Bump`/`bump`/`norm` in MTL auto-detect grayscale (→ height) vs colored
(→ normal); `normalmap`/`heightmap` in scripts are explicit.

**Ray-tracer reflections & refraction (material-driven):**
- **Reflectivity** is derived from `specular` + `shininess`: a gloss factor
  ramps from 0 (matte, no reflection) below `shininess ≈ 24` to a sharp mirror
  by `≈ 160`, tinted by `specular` with a Fresnel edge boost. A clean mirror is
  `specular=1,1,1 shininess=256`. Matte materials (`shininess=0`) never reflect,
  so they stay comparable to the raster.
- **Refraction**: set `transmission` > 0 (and an `ior`). The surface is treated
  as a dielectric — Fresnel split into a reflected + refracted ray, recursed up
  to `bounces`. Example glass: `material name="glass" diffuse=0,0,0 transmission=1 ior=1.5`.
- These fields are **ignored by the rasterizer** (which renders the material with
  its opaque base color), so setting them never breaks a raster render.
- glTF import: reflectivity comes from metallic-roughness (`roughness→shininess`,
  `specular = lerp(0.04, baseColor, metallic)`); `transmission`/`ior` come from
  `KHR_materials_transmission` / `KHR_materials_ior`.

---

## `assign` — give a material to an object

```
assign material="steel" to="ball"
```

Defaults: `material` = last defined, `to` = last created object.

---

## `camera`

```
camera comfy                                          # auto-frame the scene
camera name="hero" position=0,5,-12 target=0,1,0 fov=60   # explicit, named
camera name="top"  position=0,20,-0.1 target=0,0,0        # a second camera
```

| key | default | meaning |
|-----|---------|---------|
| `name` | `camera<N>` (or `comfy`) | handle, shown by `listcameras` |
| `position` | `0,2,-5` | eye position |
| `target` | `0,0,0` | look-at point |
| `up` | `0,1,0` | up vector |
| `fov` | `60` | vertical field of view (degrees) |
| `near` / `far` | `0.1` / `1000` | clip planes |

`camera comfy` (or `camera` with no arguments) auto-frames the scene.

**Camera selection.** You can define several cameras; the **most recently
defined** one is the active camera used to render. If at least one camera has
been defined, the renderer uses it and does **not** create a comfy camera — only
a scene with no cameras at all gets an auto-framed comfy one. In Debug builds a
log line reports which camera was chosen (e.g. `Using camera 'top' (of 2
defined).`). Re-issuing `camera name="x" …` updates the camera named `x`.

---

## `listcameras` / `listobjects` — introspection

Print the current scene state to the console (handy while building a scene).
They take no arguments and can appear anywhere in the script.

```
listobjects
listcameras
```

- `listobjects` — one line per object: name, vertex/triangle counts, material.
- `listcameras` — one line per camera: name, position/target/fov/near/far. The
  active camera is marked with `*`.

---

## `light`

```
light point       name="key" position=5,8,-5 color=1,1,1 intensity=1.2 shadows=on
light directional name="sun" direction=-1,-1,-0.5 intensity=0.5
light spot        name="lamp" position=-18,38,16 direction=0.5,-1,-0.4 cone=26 inner=20 intensity=1.6
light comfy                                  # auto-placed point light
```

| subtype | keys |
|---------|------|
| `point` | `position`, `color`, `intensity` |
| `directional` | `direction`, `color`, `intensity` |
| `spot` | `position`, `direction` (default: toward scene center), `cone` (outer half-angle, default `40`), `inner` (default `cone`×0.8), `color`, `intensity` |
| `comfy` | none (auto-placed) |

Every light also accepts `shadows=on|off` (per-light shadow casting,
`Light.CastsShadows`, default on).

---

## `render` — render and save

```
render file="out.png"
```

Defaults: `file` = `<scriptname>.bmp`. You may render multiple times in one
script. If omitted, an automatic render happens at end of file.

---

## Full example

```
# textured model + primitive on a shadowed stage
rendering shading=phong shadows=on shadowpcf=2 shadowcull=on width=1280 height=720 background=0.1,0.1,0.15

load obj file="hero.obj" name="hero"
mesh plane name="floor" size=40,40

material name="floor_mat" diffuse=0.6,0.6,0.6
assign material="floor_mat" to="floor"

translate name="hero" by=0,1,0
rotate    name="hero" axis=y angle=30

light spot name="key" position=-18,38,16 cone=26 inner=20 intensity=1.6 shadows=on
light directional name="fill" direction=1,-1,1 intensity=0.3 shadows=off
camera comfy

render file="hero.png"
```

## Ray-traced example (reflections, glass, soft shadows, AA)

```
# Same scene description — just switch the engine on and author shiny/glass
# materials. Matte materials look the same as on the raster.
rendering engine=raytrace shadows=on shadowsoft=0.6 shadowsamples=24 bounces=8 \
          aa=2 width=1280 height=720 background=0.6,0.7,0.85

mesh plane name="floor" size=20,20
material name="floor" diffuse=0.4,0.4,0.45 specular=0.9,0.9,0.9 shininess=200   # reflective
assign material="floor" to="floor"
translate name="floor" by=0,-1,0

mesh sphere name="glass" radius=1.3 segments=64 rings=32
material name="glass" diffuse=0,0,0 transmission=1 ior=1.5                       # glass
assign material="glass" to="glass"

mesh sphere name="ball" radius=0.9 segments=48 rings=24
material name="ball" diffuse=0.85,0.2,0.18                                       # matte (no reflection)
assign material="ball" to="ball"
translate name="ball" by=2.6,-0.1,0.4

light point name="key" position=4,7,-5 intensity=1.4
camera position=0,2,-7 target=0,0,0 fov=50

render file="raytraced.png"
```
