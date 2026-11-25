using System.Numerics;
using System.Runtime.CompilerServices;
using Engine.Graphics;
using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;

namespace Engine.Diagnostics;

public sealed unsafe class ImGuiLayer : IDisposable
{
    private readonly GL _gl;
    private ShaderProgram _shader = null!;
    private uint _vao, _vbo, _ebo, _fontTex;
    private int _vtxCapacity, _idxCapacity;

    /// UI хочет забрать мышь себе (курсор над окном ImGui).
    public bool WantMouse => ImGui.GetIO().WantCaptureMouse;
    public bool WantKeyboard => ImGui.GetIO().WantCaptureKeyboard;
    private Vector2 _mouseScale = Vector2.One;

    public ImGuiLayer(GL gl, IInputContext input)
    {
        _gl = gl;

        ImGui.CreateContext();
        var io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
        io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
        ImGui.StyleColorsDark();

        CreateFontTexture();
        _shader = new ShaderProgram(gl, "assets/shaders/imgui.vert", "assets/shaders/imgui.frag");
        CreateBuffers();
        HookInput(input);
    }

    // ── 1. Шрифт ────────────────────────────────────────────────────────────
    private void CreateFontTexture()
    {
        var io = ImGui.GetIO();

        // Consolas: моноширинный (цифры не "прыгают" при обновлении),
        // есть кириллица, идёт в комплекте с Windows.
        const string fontPath = @"C:\Windows\Fonts\consola.ttf";

        if (File.Exists(fontPath))
            io.Fonts.AddFontFromFileTTF(fontPath, 13f, default, io.Fonts.GetGlyphRangesCyrillic());
        else
            io.Fonts.AddFontDefault();

        io.Fonts.GetTexDataAsRGBA32(out byte* pixels, out int w, out int h);

        _fontTex = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, _fontTex);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba,
                       (uint)w, (uint)h, 0, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);

        io.Fonts.SetTexID((IntPtr)_fontTex);
    }

    // ── 2. Буферы и формат вершины ──────────────────────────────────────────
    private void CreateBuffers()
    {
        _vao = _gl.GenVertexArray();
        _gl.BindVertexArray(_vao);

        _vbo = _gl.GenBuffer();
        _ebo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo); // EBO запоминается внутри VAO

        // ImDrawVert = { Vector2 pos; Vector2 uv; uint col; } = 20 байт
        uint stride = (uint)Unsafe.SizeOf<ImDrawVert>();
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, (void*)0);
        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, (void*)8);
        _gl.EnableVertexAttribArray(2);
        // цвет — 4 БАЙТА, normalized=true превращает 0..255 в 0.0..1.0 прямо на железе
        _gl.VertexAttribPointer(2, 4, VertexAttribPointerType.UnsignedByte, true, stride, (void*)16);

        _gl.BindVertexArray(0);
    }

    private void EnsureCapacity(int vertices, int indices)
    {
        if (vertices <= _vtxCapacity && indices <= _idxCapacity) return;

        _vtxCapacity = Math.Max(vertices + 4096, _vtxCapacity * 2);
        _idxCapacity = Math.Max(indices + 8192, _idxCapacity * 2);

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        _gl.BufferData(BufferTargetARB.ArrayBuffer,
                       (nuint)(_vtxCapacity * Unsafe.SizeOf<ImDrawVert>()), null, BufferUsageARB.DynamicDraw);
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
        _gl.BufferData(BufferTargetARB.ElementArrayBuffer,
                       (nuint)(_idxCapacity * sizeof(ushort)), null, BufferUsageARB.DynamicDraw);
    }

    // ── 3. Ввод ─────────────────────────────────────────────────────────────
    private void HookInput(IInputContext input)
    {
        foreach (var mouse in input.Mice)
        {
            mouse.MouseDown += (_, b) => ImGui.GetIO().AddMouseButtonEvent((int)b, true);
            mouse.MouseUp += (_, b) => ImGui.GetIO().AddMouseButtonEvent((int)b, false);
            mouse.MouseMove += (_, p) =>
                ImGui.GetIO().AddMousePosEvent(p.X * _mouseScale.X, p.Y * _mouseScale.Y);
            mouse.Scroll += (_, w) => ImGui.GetIO().AddMouseWheelEvent(w.X, w.Y);
        }

        foreach (var kb in input.Keyboards)
        {
            kb.KeyChar += (_, c) => ImGui.GetIO().AddInputCharacter(c);
            kb.KeyDown += (_, k, _) => SendKey(kb, k, true);
            kb.KeyUp += (_, k, _) => SendKey(kb, k, false);
        }
    }

    private static void SendKey(IKeyboard kb, Key key, bool down)
    {
        var io = ImGui.GetIO();
        var mapped = Translate(key);
        if (mapped != ImGuiKey.None) io.AddKeyEvent(mapped, down);

        // модификаторы ImGui ждёт отдельными "клавишами"
        io.AddKeyEvent(ImGuiKey.ModCtrl, kb.IsKeyPressed(Key.ControlLeft) || kb.IsKeyPressed(Key.ControlRight));
        io.AddKeyEvent(ImGuiKey.ModShift, kb.IsKeyPressed(Key.ShiftLeft) || kb.IsKeyPressed(Key.ShiftRight));
        io.AddKeyEvent(ImGuiKey.ModAlt, kb.IsKeyPressed(Key.AltLeft) || kb.IsKeyPressed(Key.AltRight));
    }

    private static ImGuiKey Translate(Key key) => key switch
    {
        Key.Tab => ImGuiKey.Tab,
        Key.Left => ImGuiKey.LeftArrow,
        Key.Right => ImGuiKey.RightArrow,
        Key.Up => ImGuiKey.UpArrow,
        Key.Down => ImGuiKey.DownArrow,
        Key.PageUp => ImGuiKey.PageUp,
        Key.PageDown => ImGuiKey.PageDown,
        Key.Home => ImGuiKey.Home,
        Key.End => ImGuiKey.End,
        Key.Insert => ImGuiKey.Insert,
        Key.Delete => ImGuiKey.Delete,
        Key.Backspace => ImGuiKey.Backspace,
        Key.Space => ImGuiKey.Space,
        Key.Enter or Key.KeypadEnter => ImGuiKey.Enter,
        Key.Escape => ImGuiKey.Escape,
        Key.ControlLeft => ImGuiKey.LeftCtrl,
        Key.ShiftLeft => ImGuiKey.LeftShift,
        Key.AltLeft => ImGuiKey.LeftAlt,
        >= Key.A and <= Key.Z => ImGuiKey.A + (key - Key.A),
        >= Key.Number0 and <= Key.Number9 => ImGuiKey._0 + (key - Key.Number0),
        _ => ImGuiKey.None
    };

    // ── 4. Кадр ─────────────────────────────────────────────────────────────
    public void BeginFrame(double dt, Vector2D<int> windowSize, Vector2D<int> fb)
    {
        _mouseScale = new Vector2(fb.X / (float)Math.Max(1, windowSize.X),
                                  fb.Y / (float)Math.Max(1, windowSize.Y));
        var io = ImGui.GetIO();
        io.DisplaySize = new Vector2(fb.X, fb.Y);
        io.DisplayFramebufferScale = Vector2.One;
        io.DeltaTime = (float)Math.Max(dt, 1.0 / 1000.0);
        ImGui.NewFrame();
    }

    public void EndFrame(Vector2D<int> fb)
    {
        _gl.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill);
        ImGui.Render();
        var dd = ImGui.GetDrawData();
        if (dd.CmdListsCount == 0) return;

        // ImGui — 2D-оверлей: ни глубины, ни отсечения граней, зато альфа-блендинг
        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.CullFace);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendEquation(BlendEquationModeEXT.FuncAdd);
        _gl.BlendFuncSeparate(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha,
                              BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
        _gl.Enable(EnableCap.ScissorTest);
        _gl.Viewport(0, 0, (uint)fb.X, (uint)fb.Y);

        // пиксельные координаты (0,0 = левый верх) → NDC
        float l = dd.DisplayPos.X, r = dd.DisplayPos.X + dd.DisplaySize.X;
        float t = dd.DisplayPos.Y, b = dd.DisplayPos.Y + dd.DisplaySize.Y;
        var projection = Matrix4x4.CreateOrthographicOffCenter(l, r, b, t, -1f, 1f);

        _shader.Use();
        _shader.SetUniform("uProjection", projection);
        _shader.SetUniform("uTexture", 0);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindVertexArray(_vao);

        for (int n = 0; n < dd.CmdListsCount; n++)
        {
            var list = dd.CmdLists[n];
            int vtxCount = list.VtxBuffer.Size;
            int idxCount = list.IdxBuffer.Size;

            // Грузим каждый список в НАЧАЛО буфера: не нужно возиться с накопленными
            // смещениями. Для отладочного UI цена — ноль.
            EnsureCapacity(vtxCount, idxCount);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
            _gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0,
                              (nuint)(vtxCount * Unsafe.SizeOf<ImDrawVert>()), (void*)list.VtxBuffer.Data);
            _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
            _gl.BufferSubData(BufferTargetARB.ElementArrayBuffer, 0,
                              (nuint)(idxCount * sizeof(ushort)), (void*)list.IdxBuffer.Data);

            for (int i = 0; i < list.CmdBuffer.Size; i++)
            {
                var cmd = list.CmdBuffer[i];

                var clip = cmd.ClipRect;
                float x1 = (clip.X - dd.DisplayPos.X) * dd.FramebufferScale.X;
                float y1 = (clip.Y - dd.DisplayPos.Y) * dd.FramebufferScale.Y;
                float x2 = (clip.Z - dd.DisplayPos.X) * dd.FramebufferScale.X;
                float y2 = (clip.W - dd.DisplayPos.Y) * dd.FramebufferScale.Y;
                if (x2 <= x1 || y2 <= y1) continue;

                // GL scissors считает Y снизу вверх, ImGui — сверху вниз
                _gl.Scissor((int)x1, (int)(fb.Y - y2), (uint)(x2 - x1), (uint)(y2 - y1));
                _gl.BindTexture(TextureTarget.Texture2D, (uint)(nint)cmd.TextureId);

                _gl.DrawElementsBaseVertex(PrimitiveType.Triangles, cmd.ElemCount,
                    DrawElementsType.UnsignedShort,
                    (void*)(cmd.IdxOffset * sizeof(ushort)), (int)cmd.VtxOffset);
            }
        }

        _gl.Disable(EnableCap.ScissorTest);
        _gl.Disable(EnableCap.Blend);
        _gl.BindVertexArray(0);
    }

    public void Dispose()
    {
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteBuffer(_ebo);
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteTexture(_fontTex);
        _shader.Dispose();
        ImGui.DestroyContext();
    }
}