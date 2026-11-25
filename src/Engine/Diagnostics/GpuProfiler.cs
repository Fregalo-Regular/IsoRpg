using Silk.NET.OpenGL;

namespace Engine.Diagnostics;

/// Замер времени GPU через timer query. Результат читается с задержкой в 2 кадра,
/// чтобы НИКОГДА не блокировать CPU ожиданием GPU.
public sealed class GpuProfiler : IDisposable
{
    private readonly GL _gl;
    private readonly uint[] _queries = new uint[3];
    private int _index;
    private int _framesDone;

    public double LastMs { get; private set; }

    public GpuProfiler(GL gl)
    {
        _gl = gl;
        for (int i = 0; i < _queries.Length; i++) _queries[i] = gl.GenQuery();
    }

    public void Begin() => _gl.BeginQuery(QueryTarget.TimeElapsed, _queries[_index]);

    public void End()
    {
        _gl.EndQuery(QueryTarget.TimeElapsed);
        _framesDone++;

        if (_framesDone >= _queries.Length)
        {
            int read = (_index + 1) % _queries.Length;   // слот, записанный 2 кадра назад

            _gl.GetQueryObject(_queries[read], GLEnum.QueryResultAvailable, out int ready);
            if (ready != 0)
            {
                // TIME_ELAPSED возвращает наносекунды. int вмещает до ~2.1 с — для кадра с запасом.
                _gl.GetQueryObject(_queries[read], GLEnum.QueryResult, out int ns);
                LastMs = ns / 1_000_000.0;
            }
        }

        _index = (_index + 1) % _queries.Length;
    }

    public void Dispose()
    {
        foreach (uint q in _queries) _gl.DeleteQuery(q);
    }
}