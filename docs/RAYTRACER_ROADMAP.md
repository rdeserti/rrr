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

### P0 — Plumbing & dispatch (no real rendering) ✅ in progress
- `RenderEngine` enum + `Engine` in `RenderSettings`; `DoRendering` reads `engine=`.
- `RenderPipeline.Render` dispatches to a `RayTracer.Render` stub that fills the
  `FrameBuffer` with the background color.
- **Done when:** every existing `.rrr` runs with `rendering engine=raytrace`
  and produces an image (background), with zero regression on the raster path.

### P1 — Primary rays + first hit
- `RayCamera`: per-pixel rays from the existing `Camera` (basis from
  Position/Target/Up, Fov, aspect) — consistent with the raster LH projection.
- `RayScene`: all triangles in **world space** (applying `SceneObject.Transform`
  exactly like `ProjectTriangle`), referencing material + vertex attributes.
- **Möller–Trumbore** intersection; brute-force nearest hit.
- Provisional shading with `Material.DiffuseColor` only.
- **Done when:** silhouette/camera match the raster.

### P2 — BVH + parallelism
- Pure C# BVH (median split, SAH if needed), iterative traversal.
- `Parallel.For` over framebuffer rows/tiles (same banding pattern as the raster).
- **Done when:** real scenes (glTF) render in reasonable time.

### P3 — Lighting & material parity (no shadows/reflections)
- Barycentric interpolation of normal/uv/color/tangent at the hit.
- **Shared surface helper** extracted from `TextureShader`: albedo (diffuse map
  + tint + sRGB placeholder), specular/emissive maps, normal/height map +
  handedness, occlusion, alpha-mask, two-sided, unlit — *identical logic*.
- Call `Lighting.Shade` with `shadow = 1` (no shadows yet).
- **Done when (key requirement):** scene with non-reflective materials/textures
  ≈ identical to the raster.

### P4 — Shadows via shadow rays ⭐
- Per light with `CastsShadows`: shadow ray from the hit toward the light
  (finite distance for point/spot, infinity for directional), normal-offset bias.
- Visibility feeds the `Lighting.Shade` hook (occluded ⇒ 0).
- **Done when:** hard shadows compare directly with the raster shadow maps.
  *(Soft shadows → P6.)*

### P5 — Reflections (recursive Whitted) ⭐⭐
- Reflectivity derived from `SpecularColor` + `Shininess` (threshold + tint,
  optional Fresnel-Schlick).
- Recursive reflection ray up to `MaxBounces`; blend reflected color with the
  local contribution.
- Reflection rays that miss return the **background** (environment consistent
  with the raster).
- **Done when:** the headline feature works — mirrors / glossy surfaces reflect
  the scene and each other.

### P6 — Polish
- Anti-aliasing (N×N supersampling or jitter), soft shadows (sampled area light,
  reusing `ShadowSoftness`), transparency/refraction (stretch, reuses
  `AlphaMode.Blend` + a derived IOR), gamma (once `ColorSpace` stops being
  identity).
- Update `docs/ARCHITECTURE.md` (ray-tracer section) and `docs/RRR_SCRIPT.md`
  (`engine=`).

## Final `.rrr` syntax (unchanged + one arg)

```
rendering engine=raytrace shading=phong shadows=on width=1920 height=1080
# materials, textures, lights, camera: identical to today
```

`shading=` is ignored under `raytrace` (the model is always per-pixel), so
existing scripts remain valid as-is.
