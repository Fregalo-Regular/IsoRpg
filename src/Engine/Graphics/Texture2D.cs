using Engine.Assets;
using Silk.NET.OpenGL;
using StbImageSharp;

namespace Engine.Graphics;

public enum TexFilter { Nearest, Linear, Trilinear }
public enum TexWrap { Repeat, ClampToEdge }

public sealed unsafe class Texture2D : IDisposable
{
    private readonly GL _gl;
    public uint Handle { get; }
    public int Width { get; }
    public int Height { get; }

    private Texture2D(GL gl, int width, int height, byte* pixels)
    {
        _gl = gl;
        Width = width;
        Height = height;

        Handle = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, Handle);

        // Строки RGBA выровнены на 4 байта, но для RGB-картинок это было бы не так.
        // Явно говорим "без выравнивания" — снимает целый класс багов с перекосом изображения.
        gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);

        gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8,
                      (uint)width, (uint)height, 0,
                      PixelFormat.Rgba, PixelType.UnsignedByte, pixels);

        gl.GenerateMipmap(TextureTarget.Texture2D);

        SetFilter(TexFilter.Trilinear);
        SetWrap(TexWrap.Repeat);
    }

    public static Texture2D FromFile(GL gl, string relativePath)
    {
        using var stream = File.OpenRead(AssetPath.Resolve(relativePath));
        var img = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);

        FlipVertically(img.Data, img.Width, img.Height);

        fixed (byte* p = img.Data)
            return new Texture2D(gl, img.Width, img.Height, p);
    }

    /// Файлы изображений хранят строки сверху вниз, OpenGL ждёт снизу вверх.
    private static void FlipVertically(byte[] data, int width, int height)
    {
        int stride = width * 4;
        var span = data.AsSpan();
        var row = new byte[stride];          // аллокация на загрузке, не в кадре — допустимо

        for (int y = 0; y < height / 2; y++)
        {
            var top = span.Slice(y * stride, stride);
            var bottom = span.Slice((height - 1 - y) * stride, stride);

            top.CopyTo(row);                 // порядок важен: сначала сохраняем top,
            bottom.CopyTo(top);              // иначе затрём его и получим две копии bottom
            row.AsSpan().CopyTo(bottom);
        }
    }

    public static Texture2D FromRgba(GL gl, int width, int height, ReadOnlySpan<byte> rgba)
    {
        fixed (byte* p = rgba)
            return new Texture2D(gl, width, height, p);
    }

    public void SetFilter(TexFilter filter)
    {
        _gl.BindTexture(TextureTarget.Texture2D, Handle);
        int mag = filter == TexFilter.Nearest ? (int)GLEnum.Nearest : (int)GLEnum.Linear;
        int min = filter switch
        {
            TexFilter.Nearest => (int)GLEnum.Nearest,
            TexFilter.Linear  => (int)GLEnum.Linear,
            _                 => (int)GLEnum.LinearMipmapLinear   // трилинейная: между mip-уровнями тоже интерполяция
        };
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, mag);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, min);
    }

    public void SetWrap(TexWrap wrap)
    {
        _gl.BindTexture(TextureTarget.Texture2D, Handle);
        int m = wrap == TexWrap.Repeat ? (int)GLEnum.Repeat : (int)GLEnum.ClampToEdge;
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, m);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, m);
    }

    public void Bind(TextureUnit unit = TextureUnit.Texture0)
    {
        _gl.ActiveTexture(unit);
        _gl.BindTexture(TextureTarget.Texture2D, Handle);
    }

    public void Dispose() => _gl.DeleteTexture(Handle);
}