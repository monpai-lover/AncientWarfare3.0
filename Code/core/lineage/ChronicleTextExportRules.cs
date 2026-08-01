using System;
using System.IO;
using System.Text;

namespace AncientWarfare3.core.lineage
{
    internal static class ChronicleTextExportRules
    {
        private const string ExportDirectoryName = "aw3_exports";
        private const string ChronicleDirectoryName = "chronicles";

        public static string ResolveExportDirectory(string pSaveDirectory)
        {
            if (string.IsNullOrWhiteSpace(pSaveDirectory))
                throw new ArgumentException("Save directory is required.",
                    nameof(pSaveDirectory));
            return Path.Combine(Path.GetFullPath(pSaveDirectory),
                ExportDirectoryName, ChronicleDirectoryName);
        }

        public static string ResolveUniqueFilePath(
            ChronicleTextExportRequest pRequest, DateTime pNow)
        {
            if (pRequest == null)
                throw new ArgumentNullException(nameof(pRequest));
            string source = SourceFilePart(pRequest.Source);
            string displayName = SanitizeFilePart(pRequest.DisplayName);
            string stamp = pNow.ToString("yyyyMMdd_HHmmss_fff");
            string directory = ResolveExportDirectory(pRequest.SaveDirectory);
            string stem = source + "_" + displayName + "_" +
                          Math.Max(0L, pRequest.ContextId) + "_" + stamp;
            string candidate = Path.Combine(directory, stem + ".txt");
            int duplicate = 1;
            while (File.Exists(candidate) || File.Exists(candidate + ".tmp"))
            {
                candidate = Path.Combine(directory, stem + "_" + duplicate +
                    ".txt");
                duplicate++;
            }
            return candidate;
        }

        public static string Format(ChronicleTextExportRequest pRequest,
            ChronicleTextExportSnapshot pSnapshot, DateTime pNow)
        {
            if (pRequest == null)
                throw new ArgumentNullException(nameof(pRequest));
            pSnapshot = pSnapshot ?? ChronicleTextExportSnapshot.ForPerson(null);
            var builder = new StringBuilder();
            builder.AppendLine("AW3 编年史导出");
            builder.AppendLine("类型：" + SourceName(pRequest.Source));
            builder.AppendLine("对象：" + StripRichText(pRequest.DisplayName));
            builder.AppendLine("对象 ID：" + pRequest.ContextId);
            builder.AppendLine("导出时间：" + pNow.ToString("yyyy-MM-dd HH:mm:ss"));
            builder.AppendLine();

            if (pRequest.Source == ChronicleTextExportSource.Kingdom)
                AppendKingdom(builder, pSnapshot);
            else if (pRequest.Source == ChronicleTextExportSource.City)
                AppendPeriods(builder, pSnapshot.Periods, string.Empty);
            else
                AppendEvents(builder, pSnapshot.Events, string.Empty);
            return builder.ToString();
        }

        public static ChronicleTextExportResult Publish(string pPath,
            string pText)
        {
            string temporary = null;
            try
            {
                if (string.IsNullOrWhiteSpace(pPath))
                    return ChronicleTextExportResult.Failure(
                        "Export file path is required.");
                string destination = Path.GetFullPath(pPath);
                string directory = Path.GetDirectoryName(destination);
                if (string.IsNullOrWhiteSpace(directory))
                    return ChronicleTextExportResult.Failure(
                        "Export directory is invalid.");
                Directory.CreateDirectory(directory);
                temporary = destination + ".tmp";
                if (File.Exists(temporary)) File.Delete(temporary);
                File.WriteAllText(temporary, pText ?? string.Empty,
                    new UTF8Encoding(true));
                File.Move(temporary, destination);
                return ChronicleTextExportResult.Success(destination);
            }
            catch (Exception error)
            {
                return ChronicleTextExportResult.Failure(
                    string.IsNullOrWhiteSpace(error.Message)
                        ? "Unable to publish chronicle export."
                        : error.Message);
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(temporary) &&
                    File.Exists(temporary))
                    File.Delete(temporary);
            }
        }

        private static void AppendKingdom(StringBuilder pBuilder,
            ChronicleTextExportSnapshot pSnapshot)
        {
            foreach (ChronicleTextExportDynasty dynasty in pSnapshot.Dynasties)
            {
                if (dynasty == null) continue;
                pBuilder.AppendLine(FormatHeading(dynasty.Name,
                    dynasty.StartDate, dynasty.EndDate));
                AppendPeriods(pBuilder, dynasty.Reigns, "  ");
            }
        }

        private static void AppendPeriods(StringBuilder pBuilder,
            System.Collections.Generic.IList<ChronicleTextExportPeriod> pPeriods,
            string pIndent)
        {
            if (pPeriods == null) return;
            foreach (ChronicleTextExportPeriod period in pPeriods)
            {
                if (period == null) continue;
                pBuilder.AppendLine(pIndent + FormatHeading(period.Title,
                    period.StartDate, period.EndDate));
                AppendEvents(pBuilder, period.Events, pIndent + "  ");
            }
        }

        private static void AppendEvents(StringBuilder pBuilder,
            System.Collections.Generic.IList<ChronicleTextExportEvent> pEvents,
            string pIndent)
        {
            if (pEvents == null) return;
            foreach (ChronicleTextExportEvent historyEvent in pEvents)
            {
                if (historyEvent == null) continue;
                string date = StripRichText(historyEvent.ChronicleDate);
                string text = StripRichText(historyEvent.Text);
                pBuilder.AppendLine(pIndent + date + "  " + text);
            }
        }

        private static string FormatHeading(string pName, string pStart,
            string pEnd)
        {
            string name = StripRichText(pName);
            string start = StripRichText(pStart);
            string end = StripRichText(pEnd);
            if (string.IsNullOrEmpty(start) && string.IsNullOrEmpty(end))
                return name;
            if (string.IsNullOrEmpty(end) || string.Equals(start, end,
                    StringComparison.Ordinal))
                return name + "（" + start + "）";
            if (string.IsNullOrEmpty(start)) return name + "（" + end + "）";
            return name + "（" + start + " - " + end + "）";
        }

        private static string SourceName(ChronicleTextExportSource pSource)
        {
            if (pSource == ChronicleTextExportSource.Kingdom) return "国家";
            if (pSource == ChronicleTextExportSource.City) return "城市";
            return "人物";
        }

        private static string SourceFilePart(ChronicleTextExportSource pSource)
        {
            if (pSource == ChronicleTextExportSource.Kingdom) return "kingdom";
            if (pSource == ChronicleTextExportSource.City) return "city";
            return "person";
        }

        private static string SanitizeFilePart(string pValue)
        {
            string value = string.IsNullOrWhiteSpace(pValue) ? "unnamed" :
                pValue.Trim();
            var builder = new StringBuilder(value.Length);
            foreach (char character in value)
            {
                if (character < 32 || "<>:\"/\\|?*".IndexOf(character) >= 0)
                    continue;
                builder.Append(character);
            }
            string result = builder.ToString().Trim().Trim('.');
            return string.IsNullOrWhiteSpace(result) ? "unnamed" : result;
        }

        private static string StripRichText(string pValue)
        {
            if (string.IsNullOrEmpty(pValue)) return string.Empty;
            var builder = new StringBuilder(pValue.Length);
            bool insideTag = false;
            foreach (char character in pValue)
            {
                if (character == '<')
                {
                    insideTag = true;
                    continue;
                }
                if (character == '>')
                {
                    insideTag = false;
                    continue;
                }
                if (!insideTag) builder.Append(character);
            }
            return builder.ToString();
        }
    }
}
