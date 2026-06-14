using rrr.Scene;
using System;
using System.Collections.Generic;

namespace rrr.App;

/// <summary>
/// Counts the contents of a scene and gives a rough estimate of the memory
/// held by its geometry and textures. The estimate covers raw buffer sizes
/// only (it ignores .NET object overhead and vertex de-duplication), so treat
/// it as an order-of-magnitude figure.
/// </summary>
public sealed class SceneStats
{
    public int Cameras;
    public int Lights;
    public int Objects;
    public int Vertices;
    public int Triangles;
    public int Textures;
    public long EstimatedBytes;

    public static SceneStats From(Scene.Scene scene)
    {
        SceneStats stats = new SceneStats
        {
            Cameras = scene.Camera != null ? 1 : 0,
            Lights = scene.Lights.Count,
            Objects = scene.Objects.Count
        };

        long bytes = 0;

        foreach (SceneObject obj in scene.Objects)
        {
            Mesh? mesh = obj.Mesh;
            if (mesh == null)
                continue;

            stats.Vertices += mesh.Positions.Count;
            stats.Triangles += mesh.Triangles.Count;

            bytes += (long)mesh.Positions.Count * 12; // Vector3f
            bytes += (long)mesh.Normals.Count * 12;   // Vector3f
            bytes += (long)mesh.UVs.Count * 8;         // Vector2f
            bytes += (long)mesh.Triangles.Count * 36;  // Triangle = 9 ints
        }

        // Distinct textures across both scene materials and object materials.
        HashSet<Texture2D> seen = new HashSet<Texture2D>();

        foreach (Material material in scene.Materials)
            AccountTexture(material, seen, stats, ref bytes);

        foreach (SceneObject obj in scene.Objects)
            AccountTexture(obj.Material, seen, stats, ref bytes);

        stats.EstimatedBytes = bytes;
        return stats;
    }

    private static void AccountTexture(
        Material? material,
        HashSet<Texture2D> seen,
        SceneStats stats,
        ref long bytes)
    {
        if (material == null)
            return;

        AccountOne(material.DiffuseTexture, seen, stats, ref bytes);
        AccountOne(material.SpecularTexture, seen, stats, ref bytes);
        AccountOne(material.EmissiveTexture, seen, stats, ref bytes);
        AccountOne(material.NormalTexture, seen, stats, ref bytes);
    }

    private static void AccountOne(
        Texture2D? texture,
        HashSet<Texture2D> seen,
        SceneStats stats,
        ref long bytes)
    {
        if (texture != null && seen.Add(texture))
        {
            stats.Textures++;
            bytes += (long)texture.Width * texture.Height * 4;
        }
    }

    public void Dump()
    {
        Console.WriteLine();
        Console.WriteLine("===== SCENE STATS =====");
        Console.WriteLine($"{"Cameras",-12}{Cameras}");
        Console.WriteLine($"{"Lights",-12}{Lights}");
        Console.WriteLine($"{"Objects",-12}{Objects}");
        Console.WriteLine($"{"Vertices",-12}{Vertices}");
        Console.WriteLine($"{"Triangles",-12}{Triangles}");
        Console.WriteLine($"{"Textures",-12}{Textures}");
        Console.WriteLine($"{"Est. memory",-12}{FormatBytes(EstimatedBytes)}");
        Console.WriteLine();
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";

        if (bytes < 1024 * 1024)
            return $"{bytes / 1024.0:F1} KB";

        if (bytes < 1024L * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024.0):F1} MB";

        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
    }
}
