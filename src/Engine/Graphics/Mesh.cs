using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;

namespace Engine.Graphics;

[StructLayout(LayoutKind.Sequential)]
public struct Vertex
{
    public Vector3 Position;   // 0   loc 0
    public Vector3 Color;      // 12  loc 1
    public Vector2 UV;         // 24  loc 2
    public Vector3 Normal;     // 32  loc 3
    public Vector4 Joints;     // 44  loc 4
    public Vector4 Weights;    // 60  loc 5
}                              // stride = 76

public sealed unsafe class Mesh : IDisposable
{
    private readonly GL _gl;
    private readonly uint _vao, _vbo, _ebo;
    private readonly uint _vertexCount, _indexCount;

    public Mesh(GL gl, ReadOnlySpan<Vertex> vertices, ReadOnlySpan<ushort> indices = default)
    {
        _gl = gl;
        _vertexCount = (uint)vertices.Length;
        _indexCount = (uint)indices.Length;

        _vao = gl.GenVertexArray();
        gl.BindVertexArray(_vao);   // VAO первым: привязка EBO запомнится внутри него

        _vbo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        fixed (Vertex* p = vertices)
            gl.BufferData(BufferTargetARB.ArrayBuffer,
                          (nuint)(vertices.Length * sizeof(Vertex)), p, BufferUsageARB.StaticDraw);

        if (!indices.IsEmpty)
        {
            _ebo = gl.GenBuffer();
            gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
            fixed (ushort* p = indices)
                gl.BufferData(BufferTargetARB.ElementArrayBuffer,
                              (nuint)(indices.Length * sizeof(ushort)), p, BufferUsageARB.StaticDraw);
        }

        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, (uint)sizeof(Vertex), (void*)0);
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, (uint)sizeof(Vertex), (void*)(3 * sizeof(float)));
        gl.EnableVertexAttribArray(2);
        gl.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false,
                       (uint)sizeof(Vertex), (void*)(6 * sizeof(float)));
        gl.EnableVertexAttribArray(3);
        gl.VertexAttribPointer(3, 3, VertexAttribPointerType.Float, false,
                       (uint)sizeof(Vertex), (void*)(8 * sizeof(float)));
        gl.EnableVertexAttribArray(4);
        gl.VertexAttribPointer(4, 4, VertexAttribPointerType.Float, false, (uint)sizeof(Vertex), (void*)(11 * sizeof(float)));
        gl.EnableVertexAttribArray(5);
        gl.VertexAttribPointer(5, 4, VertexAttribPointerType.Float, false, (uint)sizeof(Vertex), (void*)(15 * sizeof(float)));

        gl.BindVertexArray(0);
    }

    public void Draw()
    {
        _gl.BindVertexArray(_vao);
        if (_indexCount > 0)
            _gl.DrawElements(PrimitiveType.Triangles, _indexCount, DrawElementsType.UnsignedShort, (void*)0);
        else
            _gl.DrawArrays(PrimitiveType.Triangles, 0, _vertexCount);
    }

    public void Dispose()
    {
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteBuffer(_vbo);
        if (_ebo != 0) _gl.DeleteBuffer(_ebo);
    }
}