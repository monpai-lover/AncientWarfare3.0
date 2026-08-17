using System;
using System.IO;
using System.Text;

namespace AncientWarfare3.core.court
{
    public static class CustomCourtTemplateStoreRules
    {
        public static bool ShouldReplaceAtomically(bool writeSucceeded,
            bool validationSucceeded)
        {
            return writeSucceeded && validationSucceeded;
        }
    }

    public sealed class CustomCourtTemplateStore
    {
        private readonly string _rootPath;

        public CustomCourtTemplateStore(string rootPath)
        {
            _rootPath = Path.GetFullPath(rootPath ?? string.Empty);
        }

        public static bool TryImport(string json,
            out CustomCourtTemplate template,
            out CustomCourtTemplateValidationError error)
        {
            return CustomCourtTemplateJsonCodec.TryImport(json, out template,
                out error);
        }

        public bool TrySave(CustomCourtTemplate template,
            out CustomCourtTemplateValidationError error)
        {
            error = CustomCourtTemplateRules.Validate(template);
            if (error != CustomCourtTemplateValidationError.None ||
                !IsSafeRoot() || template == null)
                return false;

            string path;
            if (!TryGetPath(template.Id, out path))
            {
                error = CustomCourtTemplateValidationError.InvalidTemplateId;
                return false;
            }
            Directory.CreateDirectory(_rootPath);
            string temporary = path + ".tmp";
            try
            {
                File.WriteAllText(temporary,
                    CustomCourtTemplateJsonCodec.Export(template),
                    new UTF8Encoding(false));
                CustomCourtTemplate roundTrip;
                CustomCourtTemplateValidationError roundTripError;
                bool valid = CustomCourtTemplateJsonCodec.TryImport(
                    File.ReadAllText(temporary, Encoding.UTF8),
                    out roundTrip, out roundTripError);
                if (!CustomCourtTemplateStoreRules.ShouldReplaceAtomically(
                    true, valid) || !string.Equals(
                        CustomCourtTemplateJsonCodec.Hash(template),
                        CustomCourtTemplateJsonCodec.Hash(roundTrip),
                        StringComparison.Ordinal))
                {
                    error = roundTripError;
                    return false;
                }

                if (File.Exists(path))
                    File.Replace(temporary, path, null);
                else
                    File.Move(temporary, path);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }

        public bool TryLoad(string templateId, out CustomCourtTemplate template,
            out CustomCourtTemplateValidationError error)
        {
            template = null;
            error = CustomCourtTemplateValidationError.InvalidTemplateId;
            string path;
            if (!TryGetPath(templateId, out path) || !File.Exists(path))
                return false;
            try
            {
                return CustomCourtTemplateJsonCodec.TryImport(
                    File.ReadAllText(path, Encoding.UTF8), out template,
                    out error);
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private bool TryGetPath(string templateId, out string path)
        {
            path = null;
            if (!CustomCourtTemplateRules.IsValidTemplateId(templateId) ||
                !IsSafeRoot())
                return false;
            string candidate = Path.GetFullPath(Path.Combine(
                _rootPath, templateId + ".json"));
            string prefix = _rootPath.TrimEnd(Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;
            path = candidate;
            return true;
        }

        private bool IsSafeRoot()
        {
            if (string.IsNullOrEmpty(_rootPath))
                return false;
            string existing = _rootPath;
            while (!Directory.Exists(existing))
            {
                string parent = Path.GetDirectoryName(existing);
                if (string.IsNullOrEmpty(parent) || parent == existing)
                    break;
                existing = parent;
            }
            return (File.GetAttributes(existing) & FileAttributes.ReparsePoint) == 0;
        }
    }
}
