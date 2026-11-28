using Engine.Graphics;

namespace Engine.Assets;

public sealed class GltfModel : IDisposable
{
    public IReadOnlyList<GltfPrimitive> Primitives { get; }
    public Skeleton? Skeleton { get; }
    public IReadOnlyDictionary<string, int>? Anims;

    public GltfModel(IReadOnlyList<GltfPrimitive> primitives, Skeleton? skeleton)
    {
        Primitives = primitives;
        Skeleton = skeleton;
    }

    public GltfModel(IReadOnlyList<GltfPrimitive> primitives, Skeleton? skeleton, IReadOnlyDictionary<string, int> anims)
    {
        Primitives = primitives;
        Skeleton = skeleton;
        Anims = anims;
    }

    public void Dispose()
    {
        foreach (var p in Primitives) { p.Mesh.Dispose(); p.Texture?.Dispose();}
    }
}