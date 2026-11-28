using System.Reflection;
using System.Numerics;
using SharpGLTF.Schema2;
using Silk.NET.OpenGL;
using Engine.Graphics;
using EngineMesh = Engine.Graphics.Mesh;
using GltfPrimitiveType = SharpGLTF.Schema2.PrimitiveType;
using SharpGLTF.Memory;
using StbImageSharp;
using System;

namespace Engine.Assets;

public sealed record GltfPrimitive(
    EngineMesh Mesh, Matrix4x4 Transform, Vector3 BoundsMin, Vector3 BoundsMax,
    Texture2D? Texture = null);


public static class GltfLoader
{
    /// Грузит ТОЛЬКО геометрию: позиции, нормали, UV, индексы.
    /// Материалы и текстуры glTF — отдельная задача, скиннинг — следующая сессия.
    public static GltfModel Load(GL gl, string relativePath)
    {
        var model = ModelRoot.Load(AssetPath.Resolve(relativePath));

        var result = new List<GltfPrimitive>();

        foreach (var node in model.LogicalNodes)
        {
            if (node.Mesh is null) continue;

            foreach (var prim in node.Mesh.Primitives)
            {
                if (prim.DrawPrimitiveType != GltfPrimitiveType.TRIANGLES) continue;

                // Явные проверки вместо молчаливого NRE: ошибка должна называть причину.
                var posAcc = prim.GetVertexAccessor("POSITION")
                    ?? throw new InvalidDataException($"{relativePath}: примитив без POSITION");

                var positions = ToArray(prim.GetVertexAccessor("POSITION").AsVector3Array());
                var uvAcc = prim.GetVertexAccessor("TEXCOORD_0");
                Vector2[]? uvs = uvAcc is null ? null : ToArray(uvAcc.AsVector2Array());

                var jAcc = prim.GetVertexAccessor("JOINTS_0");
                var wAcc = prim.GetVertexAccessor("WEIGHTS_0");
                Vector4[]? joints  = jAcc is null ? null : ToArray(jAcc.AsVector4Array());
                Vector4[]? weights = wAcc is null ? null : ToArray(wAcc.AsVector4Array());

                var idxAcc = prim.GetIndexAccessor();
                var indices = idxAcc is null ? null : idxAcc.AsIndicesArray();

                if (positions.Length > ushort.MaxValue)
                    throw new NotSupportedException(
                        $"{relativePath}: {positions.Length} вершин, нужны 32-битные индексы");

                var vertices = new Vertex[positions.Length];
                var min = new Vector3(float.MaxValue);
                var max = new Vector3(float.MinValue);

                // Неиндексированная геометрия: индексы 0..N-1
                var idx = new ushort[indices?.Count ?? positions.Length];
                for (ushort i = 0; i < idx.Length; i++)
                    idx[i] = (ushort)(indices is null ? i : indices[i]);

                var nrmAcc = prim.GetVertexAccessor("NORMAL");
                Vector3[] normals;
                if (nrmAcc is null)
                {
                    Console.WriteLine($"[glTF] {relativePath}: NORMAL отсутствует, генерируем из геометрии");
                    normals = GenerateNormals(positions, idx);
                }
                else
                {
                    normals = ToArray(nrmAcc.AsVector3Array());
                }

                for (int i = 0; i < positions.Length; i++)
                {
                    vertices[i] = new Vertex
                    {
                        Position = positions[i],
                        Color = Vector3.One,
                        UV = uvs is null ? Vector2.Zero : uvs[i],
                        Normal = normals is null ? Vector3.UnitY : normals[i],
                        Joints  = joints  is null ? Vector4.Zero : joints[i],
                        Weights = weights is null ? new Vector4(1f, 0f, 0f, 0f) : weights[i],
                    };
                    min = Vector3.Min(min, positions[i]);
                    max = Vector3.Max(max, positions[i]);
                }
                var transform = node.Skin is null ? node.WorldMatrix : Matrix4x4.Identity;

                Texture2D? tex = null;
                var mat = prim.Material;
                if (mat != null)
                {
                    var bc = mat.Channels.FirstOrDefault(c => c.Key == "BaseColor");
                    if (bc.Texture?.PrimaryImage is { } img)
                    {
                        var decoded = ImageResult.FromMemory(img.Content.Content.ToArray(),
                                                             ColorComponents.RedGreenBlueAlpha);
                        tex = Texture2D.FromRgba(gl, decoded.Width, decoded.Height, decoded.Data);
                    }
                }
                result.Add(new GltfPrimitive(new EngineMesh(gl, vertices, idx), transform, min, max, tex));
            }
        }
        Skeleton? skeleton = null;
        if (model.LogicalSkins.Count > 0)
        {
            skeleton = new Skeleton(model, model.LogicalSkins[0]);
            skeleton.UpdateBindPose();
            skeleton.BindVsLibrary();
        }

        var anims = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in model.LogicalAnimations)
            if (!string.IsNullOrEmpty(a.Name)) anims[a.Name] = a.LogicalIndex;

        Console.WriteLine($"[glTF] {relativePath}: {result.Count} примитивов, {anims.Count} анимаций");
        return new GltfModel(result, skeleton, anims);
    }

    private static void Dump(string label, object? o)
    {
        Console.WriteLine($"=== {label}: {(o is null ? "NULL" : o.GetType().Name)} ===");
        if (o is null) return;
        foreach (var p in o.GetType().GetProperties())
        {
            if (p.GetIndexParameters().Length > 0 || p.PropertyType == typeof(string)) continue;
            string v;
            try { v = p.GetValue(o)?.ToString() ?? "NULL"; }
            catch (Exception e) { v = $"<{e.GetType().Name}>"; }
            Console.WriteLine($"  {p.PropertyType.Name,-26} {p.Name,-26} = {v}");
        }
    }

    private static object? FirstBy(object o, string contains)
    {
        foreach (var p in o.GetType().GetProperties())
        {
            if (p.GetIndexParameters().Length > 0 || p.PropertyType == typeof(string)) continue;
            if (!p.Name.Contains(contains, StringComparison.OrdinalIgnoreCase)) continue;
            if (p.GetValue(o) is System.Collections.IEnumerable seq)
                foreach (var item in seq) return item;
        }
        return null;
    }

    public static void ProbeSkinning(string relativePath)
    {
        var model = ModelRoot.Load(AssetPath.Resolve(relativePath));
        Dump("ModelRoot", model);

        var skin = FirstBy(model, "Skin");
        Dump("Skin", skin);
        if (skin != null) Dump("Skin.Joint", FirstBy(skin, "Joint"));

        var anim = FirstBy(model, "Animation");
        Dump("Animation", anim);
        if (anim == null) return;
        var ch = FirstBy(anim, "Channel");
        Dump("Channel", ch);
        if (ch != null) Dump("Sampler", FirstBy(ch, "Sampler"));
    }

    /// Сглаженные нормали из геометрии. Площадь треугольника работает весом —
    /// крупные грани влияют сильнее, что обычно даёт хороший результат.
    private static Vector3[] GenerateNormals(Vector3[] pos, ushort[] idx)
    {
        var n = new Vector3[pos.Length];

        for (int i = 0; i + 2 < idx.Length; i += 3)
        {
            var a = pos[idx[i]];
            var b = pos[idx[i + 1]];
            var c = pos[idx[i + 2]];
            var face = Vector3.Cross(b - a, c - a);   // НЕ нормализуем: длина = 2×площадь

            n[idx[i]] += face;
            n[idx[i + 1]] += face;
            n[idx[i + 2]] += face;
        }

        for (int i = 0; i < n.Length; i++)
            n[i] = n[i].LengthSquared() > 1e-12f ? Vector3.Normalize(n[i]) : Vector3.UnitY;

        return n;
    }

    private static T[] ToArray<T>(IAccessorArray<T> src) where T : unmanaged
    {
        var dst = new T[src.Count];
        for (int i = 0; i < src.Count; i++) dst[i] = src[i];
        return dst;
    }

    public static void ProbeChannelMethods(ModelRoot model)
    {
        var ch = model.LogicalAnimations[0].Channels[0];
        var s = ch.GetTranslationSampler();
        Dump("TranslationSampler", s);
        foreach (var m in s.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (m.IsSpecialName) continue;
            Console.WriteLine($"  {m.ReturnType.Name} {m.Name}({string.Join(", ", m.GetParameters().Select(x => x.ParameterType.Name))})");
        }

        var ja = model.LogicalMeshes[0].Primitives[0].GetVertexAccessor("JOINTS_0");
        foreach (var m in ja!.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
            if (m.Name.StartsWith("As")) Console.WriteLine($"  ACC {m.ReturnType.Name} {m.Name}()");
    }

    public static void ProbeMaterial(ModelRoot model)
    {
        var prim = model.LogicalMeshes[0].Primitives[0];
        Dump("Primitive", prim);
        var mat = prim.Material;
        Dump("Material", mat);
        if (mat != null) foreach (var ch in mat.Channels) Dump("Channel:" + ch.Key, ch);
    }

    public static void ProbeAnims(string path)
    {
        var m = ModelRoot.Load(AssetPath.Resolve(path));
        foreach (var a in m.LogicalAnimations)
            Console.WriteLine($"{path} anim {a.LogicalIndex} name={a.GetType().GetProperty("Name")?.GetValue(a)} dur={a.Duration:F2}");
    }
}