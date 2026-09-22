using Assimp;

namespace NekoConverter.Core.Engines.Mesh;

/// <summary>
/// Трёхмерные модели через библиотеку Assimp.
///
/// Assimp — единственная практичная библиотека, которая читает десятки форматов
/// трёхмерной графики. Пакет AssimpNetter приносит и управляемую обёртку, и нативные
/// библиотеки под каждую платформу, поэтому ничего не нужно собирать самому.
///
/// Проверено, что работает в NativeAOT: несмотря на предупреждения обрезки,
/// импорт и экспорт проходят полностью.
///
/// Что важно знать о качестве: Assimp — это конвертер геометрии, а не редактор.
/// Он переносит вершины, полигоны, нормали и разметку материалов, но НЕ гарантирует
/// сохранение анимации, скелета и шейдерных материалов. Для обмена моделями этого
/// достаточно, для переноса игровой сцены — нет.
/// </summary>
public static class MeshConverter
{
    /// <summary>Наш идентификатор формата → идентификатор формата экспорта в Assimp.</summary>
    private static readonly Dictionary<string, string> ExportFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        // STL и PLY пишем в двоичном виде: текстовый вариант в разы больше
        // при том же содержимом.
        ["stl"] = "stlb",
        ["obj"] = "obj",
        ["ply"] = "plyb",
        ["dae"] = "collada",
        ["gltf"] = "gltf2",
        ["glb"] = "glb2",
        ["fbx"] = "fbx",
        ["3mf"] = "3mf",
        ["3ds"] = "3ds",
    };

    public static bool CanWrite(string formatId) => ExportFormats.ContainsKey(formatId.TrimStart('.'));

    public static void Convert(string inputPath, string outputPath, string targetFormatId)
    {
        var target = targetFormatId.TrimStart('.').ToLowerInvariant();

        if (!ExportFormats.TryGetValue(target, out var exportFormat))
        {
            throw new NotSupportedException($"Assimp is not configured to write the \"{targetFormatId}\" format.");
        }

        var fullOutput = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullOutput);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var context = new AssimpContext();

        Scene scene;

        try
        {
            // Triangulate обязателен: STL и OBJ работают только с треугольниками,
            // а исходник может содержать четырёхугольники и многоугольники.
            scene = context.ImportFile(inputPath, PostProcessSteps.Triangulate);
        }
        catch (AssimpException ex)
        {
            throw new InvalidDataException($"Could not read the model: {ex.Message}", ex);
        }

        if (scene.MeshCount == 0)
        {
            throw new InvalidDataException("The file contains no 3D mesh.");
        }

        if (!context.ExportFile(scene, fullOutput, exportFormat))
        {
            throw new InvalidOperationException(
                $"Assimp could not write the model in the \"{target}\" format.");
        }

        if (!File.Exists(fullOutput) || new FileInfo(fullOutput).Length == 0)
        {
            throw new InvalidOperationException("Assimp reported success but no file was created.");
        }
    }

    /// <summary>
    /// Краткая сводка по модели: сколько сеток, вершин, полигонов.
    ///
    /// Подписи намеренно на латинице и в общепринятых сокращениях (mesh, vert,
    /// face, mat, anim): их одинаково читают в любой стране, а переводить
    /// технические сокращения — только путать.
    /// </summary>
    public static string Describe(string inputPath)
    {
        using var context = new AssimpContext();
        var scene = context.ImportFile(inputPath, PostProcessSteps.Triangulate);

        var vertices = scene.Meshes.Sum(m => m.VertexCount);
        var faces = scene.Meshes.Sum(m => m.FaceCount);

        return $"mesh {scene.MeshCount} · vert {vertices} · face {faces} · " +
               $"mat {scene.MaterialCount} · anim {scene.AnimationCount}";
    }
}
