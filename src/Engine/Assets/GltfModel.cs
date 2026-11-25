using Engine.Graphics;

namespace Engine.Assets;

public sealed class GltfModel : IDisposable
{
    public IReadOnlyList<GltfPrimitive> Primitives { get; }
    public Skeleton? Skeleton { get; }

    public GltfModel(IReadOnlyList<GltfPrimitive> primitives, Skeleton? skeleton)
    {
        Primitives = primitives;
        Skeleton = skeleton;
    }

    public void Dispose()
    {
        foreach (var p in Primitives) p.Mesh.Dispose();
    }
}