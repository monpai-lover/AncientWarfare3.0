using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace AncientWarfare3.core.naming
{
    public static class AWNamingResourceLoader
    {
        public static IReadOnlyList<AWWordLibraryAsset> LoadWordLibraries(
            string pDirectory, Action<string> pWarning = null)
        {
            if (string.IsNullOrWhiteSpace(pDirectory) ||
                !Directory.Exists(pDirectory))
            {
                pWarning?.Invoke("AW3 naming word-library directory is missing: " +
                                 (pDirectory ?? "<null>"));
                return Array.Empty<AWWordLibraryAsset>();
            }

            var result = new List<AWWordLibraryAsset>();
            foreach (string file in Directory.GetFiles(pDirectory, "*.txt",
                         SearchOption.AllDirectories)
                     .OrderBy(pPath => pPath, StringComparer.Ordinal))
            {
                try
                {
                    result.Add(new AWWordLibraryAsset(
                        Path.GetFileNameWithoutExtension(file),
                        File.ReadAllLines(file)));
                }
                catch (Exception error)
                {
                    pWarning?.Invoke("AW3 naming failed to load word library '" +
                                     file + "': " + error.Message);
                }
            }
            return result;
        }

        public static IReadOnlyList<AWNameGeneratorAsset> LoadGenerators(
            string pDirectory, Action<string> pWarning = null)
        {
            if (string.IsNullOrWhiteSpace(pDirectory) ||
                !Directory.Exists(pDirectory))
            {
                pWarning?.Invoke("AW3 naming generator directory is missing: " +
                                 (pDirectory ?? "<null>"));
                return Array.Empty<AWNameGeneratorAsset>();
            }

            var result = new List<AWNameGeneratorAsset>();
            foreach (string file in Directory.GetFiles(pDirectory, "*.json",
                         SearchOption.AllDirectories)
                     .OrderBy(pPath => pPath, StringComparer.Ordinal))
            {
                try
                {
                    List<GeneratorDto> generators = JsonConvert.DeserializeObject<
                        List<GeneratorDto>>(File.ReadAllText(file));
                    if (generators == null) continue;
                    foreach (GeneratorDto generator in generators)
                    {
                        AWNameGeneratorAsset asset = Convert(generator, file,
                            pWarning);
                        if (asset != null) result.Add(asset);
                    }
                }
                catch (Exception error)
                {
                    pWarning?.Invoke("AW3 naming failed to load generator file '" +
                                     file + "': " + error.Message);
                }
            }
            return result;
        }

        private static AWNameGeneratorAsset Convert(GeneratorDto pGenerator,
            string pFile, Action<string> pWarning)
        {
            if (pGenerator == null || string.IsNullOrWhiteSpace(pGenerator.id))
            {
                pWarning?.Invoke("AW3 naming skipped a generator without id in '" +
                                 pFile + "'.");
                return null;
            }

            var templates = new List<AWNameTemplate>();
            foreach (TemplateDto template in pGenerator.templates ??
                     new List<TemplateDto>())
            {
                AWNameTemplate parsed = ConvertTemplate(template, pGenerator.id,
                    pWarning);
                if (parsed != null) templates.Add(parsed);
            }
            if (templates.Count == 0)
            {
                pWarning?.Invoke("AW3 naming skipped generator '" +
                                 pGenerator.id + "' because it has no valid templates.");
                return null;
            }

            AWNameTemplate fallback = ConvertTemplate(pGenerator.default_template,
                pGenerator.id + ":default", pWarning) ??
                                      AWNameTemplate.Create("#NO_NAME#", 1f);
            return new AWNameGeneratorAsset(pGenerator.id, templates, fallback,
                pGenerator.parameter_getter);
        }

        private static AWNameTemplate ConvertTemplate(TemplateDto pTemplate,
            string pOwner, Action<string> pWarning)
        {
            if (pTemplate == null || pTemplate.format == null) return null;
            try
            {
                return AWNameTemplate.Create(pTemplate.format,
                    pTemplate.weight ?? 1f);
            }
            catch (AWInvalidNameTemplateException error)
            {
                pWarning?.Invoke("AW3 naming skipped invalid template in '" +
                                 pOwner + "': " + error.Message);
                return null;
            }
        }

        private sealed class GeneratorDto
        {
            public string id { get; set; }
            public string parameter_getter { get; set; }
            public TemplateDto default_template { get; set; }
            public List<TemplateDto> templates { get; set; }
        }

        private sealed class TemplateDto
        {
            public string format { get; set; }
            public float? weight { get; set; }
        }
    }

    internal static class AWNamingContent
    {
        private static string _modPath;

        public static void Initialize(string pModPath)
        {
            _modPath = pModPath ?? string.Empty;
            string wordDirectory = Path.Combine(_modPath,
                "word_libraries", "default");
            string generatorDirectory = Path.Combine(_modPath,
                "name_generators", "default");

            AWWordLibraryManager.Instance.ReplaceAll(
                AWNamingResourceLoader.LoadWordLibraries(wordDirectory,
                    ModClass.LogWarning));
            AWWordLibraryManager.Instance.InstallChineseNameLegacyAliases();
            AWNameGeneratorLibrary.Initialize(generatorDirectory,
                ModClass.LogWarning);
            ModClass.LogInfo("AW3 integrated naming loaded: generators=" +
                             AWNameGeneratorLibrary.Count + " word_libraries=" +
                             AWWordLibraryManager.Instance.Count + ".");
        }

        public static void Reload()
        {
            Initialize(_modPath);
        }
    }
}
