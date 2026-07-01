# RRR — Raytracer Roadmap

Roadmap for adding a **ray tracing engine** alongside the existing rasterizer.
Target unchanged: .NET 8, single executable, no external dependencies.

## Guiding principle

**One surface model, two visibility rasterizers.** The raster engine resolves
shadows with shadow maps; the ray tracer resolves them with shadow rays. Both
feed the *same* `Lighting.Shade` BRDF and the *same* material/texture
evaluation. A scene with non-reflective materials + textures is therefore
**comparable by construction**, not by coincidence.

## Constraints

- `Core`, `Scene`, `VMath` are **not modified**. All new code lives under
  `Rendering/` (plus a couple of lines in `Scripting`).
- The `Engine` selector lives in `RenderSettings` (namespace `Rendering`).
- **Reflectivity is derived from `Material.SpecularColor` + `Shininess`**
  (no new `Material` field): a reflective weight active above a shininess
  threshold, tinted by the specular color, with an optional Fresnel-Schlick
  term. Opaque materials (`shininess = 0`) stay bit-comparable with the raster.

## Insertion points (all outside Core / Scene / VMath)

| File | Change |
|------|--------|
| `Rendering/RenderSettings.cs` | `enum RenderEngine { Raster, Raytrace }`, `Engine` property + ray-tracer knobs (`MaxBounces`, `RayShadows`, `Supersampling`…) |
| `Rendering/RenderPipeline.cs` | `Render(...)` dispatches to `RayTracer.Render(fb, scene, settings)` when `Engine == Raytrace` |
| `Scripting/SceneScript.cs` (`DoRendering`) | parse `engine=raster\|raytrace` + ray-tracer args |
| `Rendering/Lighting.cs` | refactor so the BRDF accepts a **visibility hook** (`shadow` per light), reused by both shadow-map and shadow-ray paths |
| **new** `Rendering/Raytracing/` | `Ray`, `RayHit`, `RayScene`, `Bvh`, `RayTracer`, `RayCamera`, shared surface helper |

The `SceneRenderer.Render` funnel is **unchanged**: profiler, `SceneStats`,
`ImageWriter`, background are already shared.

## Phases

### P0 — Plumbing & dispatch (no real rendering) ✅ done
- `RenderEngine` enum + `Engine` in `RenderSettings`; `DoRendering` reads `engine=`.
- `RenderPipeline.Render` dispatches to a `RayTracer.Render` stub that fills the
  `FrameBuffer` with the background color.
- **Done when:** every existing `.rrr` runs with `rendering engine=raytrace`
  and produces an image (background), with zero regression on the raster path.

### P1 — Primary rays + first hit ✅ done
- `RayCamera`: per-pixel rays from the existing `Camera` (basis from
  Position/Target/Up, Fov, aspect) — consistent with the raster LH projection.
- `RayScene`: all triangles in **world space** (applying `SceneObject.Transform`
  exactly like `ProjectTriangle`), referencing material + vertex attributes.
- **Möller–Trumbore** intersection; brute-force nearest hit.
- Provisional shading with `Material.DiffuseColor` only.
- **Done when:** silhouette/camera match the raster.

### P2 — BVH + parallelism ✅ done
- Pure C# BVH (midpoint split on largest centroid axis, median fallback),
  flat node array, allocation-free iterative traversal (`stackalloc` stack).
- `Parallel.For` over framebuffer rows (already added in P1).
- Also added `IntersectAny` (shadow-ray any-hit) ready for P4.
- **Measured:** 2306 tris 2353 ms → 44 ms (~53×); 65538 tris in 173 ms
  (≈390× vs the linear scan extrapolation). Output bit-identical to brute force.

### P3 — Lighting & material parity (no shadows/reflections) ✅ done
- Barycentric interpolation of normal/uv/color/tangent/handedness at the hit
  (`RayTriangle`/`RayHit` extended).
- **Shared surface helper** `Rendering/SurfaceShading.cs` extracted from
  `TextureShader`: albedo (diffuse map + tint + sRGB placeholder),
  specular/emissive maps, normal/height map + handedness, occlusion, alpha-mask,
  two-sided, unlit — *identical logic*. `TextureShader.Shade` now delegates to
  it, so the two engines can never diverge.
- Ray tracer calls `Lighting.Shade` via the helper with `shadowMaps = null`
  (shadow = 1, no shadows yet).
- **Verified:** untextured sphere ≈ identical to raster Phong (shadows off);
  textured glTF model samples diffuse/UV/vertex-color/lighting identically;
  raster normal/height mapping unregressed by the refactor.
- *Known gap:* alpha-mask hits currently fall through to the background in the
  ray tracer (no pass-through retrace yet) — handled in P6.

### P4 — Shadows via shadow rays ⭐ ✅ done
- `Lighting.Shade` refactored around a zero-alloc generic visibility hook
  (`IVisibility` + `ShadeCore<TVis>`): the raster wraps shadow maps in
  `ShadowMapVisibility` (behavior unchanged), the tracer injects
  `RayShadowVisibility`. Single shared BRDF.
- `RayShadowVisibility`: per-light, gated by `Light.CastsShadows` +
  `RenderSettings.RayShadows`; shadow ray toward the light (finite for
  point/spot, infinite for directional) via the BVH `IntersectAny`;
  normal-offset bias scaled to the scene diagonal; back-facing early-out.
- **Verified:** hard shadow matches the raster shadow-map placement/shape (raster
  edge is PCF-soft, the tracer's is hard — soft shadows are P6); raster
  unregressed by the `Lighting` refactor.

### P5 — Reflections (recursive Whitted) ⭐⭐ ✅ done
- Recursive `Trace(ray, depth)` up to `RenderSettings.MaxBounces` (`bounces=`).
- Reflectivity derived from the material with **no new Scene field**: a `Gloss`
  factor `smoothstep(24, 160, Shininess)` gates the reflection (0 below the
  threshold), and Fresnel-Schlick reflectance with `F0 = SpecularColor` tints it
  and boosts grazing angles. `kr = gloss · fresnel`, blended
  `local·(1-kr) + reflected·kr` per channel.
- Reflection rays that miss return the captured **background**.
- **Verified:** glossy floor + mirror sphere reflect the scene; matte spheres
  (`shininess = 0` ⇒ `gloss = 0`) show no reflection and re-render bit-identical
  to P4 — parity with the raster preserved.
- *Authoring:* `material ... specular=1,1,1 shininess=256` = sharp mirror;
  higher shininess = sharper/stronger; `shininess < 24` = matte (no reflection).
- *glTF mapping (pragmatic):* the importer derives the reflectivity inputs from
  metallic-roughness so reflections match glTF intent — `roughness → shininess`,
  `SpecularColor = lerp(0.04, baseColor, metallic)` (metals reflect tinted,
  dielectrics get a ~0.04 Fresnel edge). Diffuse is only halved for metals (not
  zeroed) so they stay visible in the raster, which has no reflections to fill
  them. Verified: dielectric/textured glTF unregressed (diffuse unchanged when
  `metallic = 0`).

### P6 — Polish
- **Anti-aliasing ✅ done** — SSAA via a single engine-agnostic routine
  `Rendering/Supersampler.cs`: the funnel (`SceneRenderer.Render`) renders into a
  buffer scaled up by `RenderSettings.Supersampling` (clamped 1–4, default 1 =
  off) and box-downsamples by arithmetic mean. Script: `supersampling=` or the
  `aa=` alias. Anti-aliases both the rasterizer and the ray tracer identically
  (silhouettes, floor edges, and the ray tracer's hard shadow edges).
- **Soft shadows ✅ done** — `RayShadowVisibility` casts `ShadowSamples` shadow
  rays toward a disk around the light when `ShadowSoftness > 0` (area-light
  radius in world units for point/spot, angular spread for directional);
  unoccluded fraction = penumbra. Fibonacci disk pattern rotated by a per-point
  hash to turn banding into noise. Script: `shadowsoft=`, `shadowsamples=`.
  Softness 0 (default) = single hard ray (identical to P4). Pairs well with
  `aa=` to clean residual penumbra noise.
- **Transparency / refraction ✅ done** — two additive `Material` fields
  (`Transmission` 0..1, `IndexOfRefraction` default 1.5), imported from glTF
  `KHR_materials_transmission` + `KHR_materials_ior`, authored in rrr via
  `transmission=` / `ior=`. The ray tracer treats transmissive materials as a
  dielectric: Fresnel-Schlick split into a reflected + a refracted ray (Snell,
  total-internal-reflection handled), recursed to `MaxBounces`, blended with the
  local shading by transmission (`ior = 1` ⇒ straight-through transparency). The
  **rasterizer ignores both fields** (verified: glass renders as its opaque
  base color, no impact).
- **Alpha transparency parity ✅ done** — the tracer now matches the raster's
  transparency passes: **Mask** (alpha-test cutout) rays pass through below-cutoff
  fragments to the surface behind (no bounce consumed, capped at 64 steps);
  **Blend** fragments composite src-over (`local·a + behind·(1-a)`) via a
  continuation ray, recursing front-to-back so stacked transparents layer
  correctly. Verified identical to the raster for both. Refraction
  (`Transmission`) still takes priority over Blend on the same material.
- **Transparent shadows ✅ done** — shadow rays now compute a **transmittance**
  in [0,1] instead of a binary block: opaque scenes keep the cheap any-hit
  fast path (`RayScene.HasTransparentMaterials` gate), otherwise the ray walks
  through transparent surfaces multiplying their transmittance (glass passes
  `Transmission`, alpha-blend passes `1 - alpha`, masked cutouts pass fully below
  the cutoff) until an opaque hit or the light. Works for hard and soft (per
  sample) shadows. Verified: glass casts ~no shadow, an opaque twin casts a solid
  one, a masked cutout casts a shadow only where opaque (light through the holes).
- **Normal-mapped reflections ✅ done** — `SurfaceShading.Shade` now outputs the
  perturbed shading normal (`out shadingNormal`); the ray tracer reflects and
  refracts off it instead of the interpolated normal, so a normal/height-mapped
  mirror reflects its bumps. No regression on un-mapped surfaces (the perturbed
  normal equals the interpolated one when there is no map). Verified with a
  height-mapped mirror floor (reflections break up along the bump pattern).
- **Gamma correction ✅ done** — real piecewise sRGB in `Core/ColorSpace`, the
  two directions gated independently (`SrgbToLinearEnabled` / `LinearToSrgbEnabled`,
  from `RenderSettings.LinearizeInput` / `EncodeSrgb`, script `linearize=` /
  `srgb=`). Color inputs (diffuse/emissive textures **and** flat diffuse/emissive
  colors **and** background) decode sRGB→linear; lighting, reflection, refraction
  and the SSAA downsample run in linear; the final image encodes linear→sRGB once
  after the downsample (`SceneRenderer.EncodeToSrgb`). Both engines, **on by
  default**; a disabled direction is an immediate identity (no per-channel pow).
  `linearize=off srgb=off` = old uncorrected look. *Caveat:* 8-bit linear
  intermediate can band in deep shadows (float target = future work).
- *Remaining:* colored/tinted shadows (transmittance is scalar, not per-channel)
  and caustics (no light focusing through refraction); environment map for
  reflection-ray misses; SAH BVH; float render target (HDR + band-free gamma).
- **Docs ✅ done** — `docs/ARCHITECTURE.md` has a `Rendering/Raytracing` section,
  the implemented "Ray tracer" summary, updated Material/Lighting/RenderSettings/
  SceneRenderer/GltfImporter entries, engine dispatch in the data-flow diagram,
  and ray-tracer entries in Known limitations. `docs/RRR_SCRIPT.md` documents
  `engine=`, `aa=`/`supersampling=`, `bounces=`, `rayshadows=`, `shadowsamples=`,
  the per-engine `shadowsoft=` meaning, `transmission=`/`ior=`, the reflectivity
  model, and a ray-traced example.

## Final `.rrr` syntax (unchanged + one arg)

```
rendering engine=raytrace shading=phong shadows=on width=1920 height=1080
# materials, textures, lights, camera: identical to today
```

`shading=` is ignored under `raytrace` (the model is always per-pixel), so
existing scripts remain valid as-is.

## Open items (revisit later)

- **Raster shadow — contact crescent.** The big umbra leak is fixed (linear
  light-space depth). A *small* bright crescent can still remain right under a
  sphere where the occluder/receiver gap is ~0 (the degenerate contact case:
  same distance ⇒ any bias uncovers a tiny neighborhood). Tighten via higher
  `ShadowMapResolution`, a contact-aware (distance-shrinking) bias, or a
  receiver-plane / second-depth midpoint scheme. The ray tracer is exact here.
- **Third engine — GPU (future).** Direction chosen: **hybrid raster + RT**
  (rasterized G-buffer for form + attached shadows; ray-traced cast shadows and
  reflections, composited). Requires a GPU API binding (e.g. Silk.NET / OpenTK /
  ComputeSharp) — the one accepted exception to the no-dependencies rule. Not
  started; CPU engines remain the focus for now.
