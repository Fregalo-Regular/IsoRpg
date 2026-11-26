using System.Numerics;
using Engine.Assets;
using Engine.Graphics;

namespace Engine.ECS;

public interface ISparseSet { bool Has(int e); void Remove(int e); }

public sealed class SparseSet<T> : ISparseSet where T : struct
{
    private readonly Dictionary<int, int> _sparse = new();
    private readonly List<int> _dense = new();
    private T[] _data = new T[64];
    public bool Has(int e) => _sparse.ContainsKey(e);
    public ref T Add(int e)
    {
        if (_sparse.TryGetValue(e, out int i)) return ref _data[i];
        if (_dense.Count == _data.Length) Array.Resize(ref _data, _data.Length * 2);
        _sparse[e] = _dense.Count; _dense.Add(e);
        return ref _data[_dense.Count - 1];
    }
    public ref T Get(int e) => ref _data[_sparse[e]];
    public IEnumerable<int> Entities() => _dense;
    public void Remove(int e)
    {
        if (!_sparse.TryGetValue(e, out int i)) return;
        int last = _dense.Count - 1;
        if (i != last) { var le = _dense[last]; _dense[i] = le; _data[i] = _data[last]; _sparse[le] = i; }
        _dense.RemoveAt(last); _sparse.Remove(e);
    }
}

public sealed class World
{
    private int _next;
    private readonly Dictionary<Type, ISparseSet> _sets = new();
    public int Create() => _next++;
    public SparseSet<T> Set<T>() where T : struct
    {
        if (!_sets.TryGetValue(typeof(T), out var s)) { s = new SparseSet<T>(); _sets[typeof(T)] = s; }
        return (SparseSet<T>)s;
    }
    public ref T Add<T>(int e) where T : struct => ref Set<T>().Add(e);
    public bool Has<T>(int e) where T : struct => Set<T>().Has(e);
    public ref T Get<T>(int e) where T : struct => ref Set<T>().Get(e);
    public void Destroy(int e) { foreach (var s in _sets.Values) s.Remove(e); }
}