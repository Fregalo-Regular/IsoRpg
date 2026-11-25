using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace Engine.Platform;

/// Базовый класс приложения: окно, GL-контекст, ввод, события кадра.
/// Наследуйся и переопределяй On* — вся платформенная рутина спрятана здесь.
public abstract class EngineHost
{
    private IWindow _window = null!;
    private IInputContext _input = null!;

    protected GL Gl { get; private set; } = null!;
    protected IInputContext Input => _input;
    protected Vector2D<int> FramebufferSize { get; private set; }
    protected Vector2D<int> WindowSize => _window.Size;

    protected bool VSync
    {
        get => _window.VSync;
        set => _window.VSync = value;
    }

    protected void Exit() => _window.Close();

    public void Run(string title, int width, int height)
    {
        var options = WindowOptions.Default with
        {
            Title = title,
            Size = new Vector2D<int>(width, height),
            VSync = true,
            // Core 4.5: доступ к debug-выводу и DSA. Debug-флаг даёт нам сообщения драйвера.
            API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core,
                                  ContextFlags.Debug, new APIVersion(4, 5)),
        };

        _window = Window.Create(options);
        _window.Load += OnLoadInternal;
        _window.Update += dt => OnUpdate(dt);
        _window.Render += dt => OnRender(dt);
        _window.FramebufferResize += size =>
        {
            FramebufferSize = size;
            if (Gl is not null) Gl.Viewport(size);   // событие может прийти раньше Load
            OnResize(size);
        };
        _window.Closing += () => { OnShutdown(); _input?.Dispose(); };

        _window.Run();
        _window.Dispose();
    }

    private unsafe void OnLoadInternal()
    {
        Gl = GL.GetApi(_window);
    
        Gl.Enable(EnableCap.DebugOutput);
        Gl.Enable(EnableCap.DebugOutputSynchronous);
        Gl.DebugMessageCallback((source, type, id, severity, length, message, userParam) =>
        {
            var text = System.Runtime.InteropServices.Marshal.PtrToStringAnsi((nint)message, (int)length);
            if (severity is GLEnum.DebugSeverityHigh or GLEnum.DebugSeverityMedium)
                Console.WriteLine($"[GL] {severity}: {text}");
        }, null);

        _input = _window.CreateInput();
        FramebufferSize = _window.FramebufferSize;
        Gl.Viewport(FramebufferSize);

        Console.WriteLine($"OpenGL {Gl.GetStringS(StringName.Version)} | " +
                          $"{Gl.GetStringS(StringName.Renderer)}");
        OnLoad();
    }

    protected virtual void OnLoad() { }
    protected virtual void OnUpdate(double dt) { }
    protected virtual void OnRender(double dt) { }
    protected virtual void OnResize(Vector2D<int> size) { }
    protected virtual void OnShutdown() { }
}