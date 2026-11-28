using System.IO;

namespace Engine.Assets;

public static class AssetPath
{
    public static string Resolve(string relativePath)
    {
        // Ищем файл, идя вверх по дереву каталогов от BaseDirectory и CurrentDirectory.
        // Это делает загрузку ассетов независимой от CWD и настроек запуска в IDE.
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var dir = start;
            while (!string.IsNullOrEmpty(dir))
            {
                var candidate = Path.Combine(dir, relativePath);
                if (File.Exists(candidate)) return candidate;
                
                var parent = Directory.GetParent(dir);
                if (parent == null) break;
                dir = parent.FullName;
            }
        }

        // Fallback: если не нашли, возвращаем путь как есть — пусть File.ReadAllText 
        // выбросит исключение с тем путём, где он пытался искать (легче дебажить).
        return Path.IsPathRooted(relativePath) 
            ? relativePath 
            : Path.Combine(Directory.GetCurrentDirectory(), relativePath);
    }
}