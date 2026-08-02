using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal enum ChronicleTextExportSource
    {
        Person,
        Kingdom,
        City
    }

    internal sealed class ChronicleTextExportRequest
    {
        public ChronicleTextExportRequest(ChronicleTextExportSource pSource,
            long pContextId, string pDisplayName, string pSaveDirectory)
        {
            Source = pSource;
            ContextId = pContextId;
            DisplayName = pDisplayName ?? string.Empty;
            SaveDirectory = pSaveDirectory ?? string.Empty;
        }

        public ChronicleTextExportSource Source { get; private set; }
        public long ContextId { get; private set; }
        public string DisplayName { get; private set; }
        public string SaveDirectory { get; private set; }
    }

    internal sealed class ChronicleTextExportResult
    {
        private ChronicleTextExportResult(bool pSucceeded, string pPath,
            string pError)
        {
            Succeeded = pSucceeded;
            Path = pPath ?? string.Empty;
            Error = pError ?? string.Empty;
        }

        public bool Succeeded { get; private set; }
        public string Path { get; private set; }
        public string Error { get; private set; }

        public static ChronicleTextExportResult Success(string pPath)
        {
            return new ChronicleTextExportResult(true, pPath, string.Empty);
        }

        public static ChronicleTextExportResult Failure(string pError)
        {
            return new ChronicleTextExportResult(false, string.Empty, pError);
        }
    }

    internal sealed class ChronicleTextExportEvent
    {
        public ChronicleTextExportEvent(string pChronicleDate, string pText)
        {
            ChronicleDate = pChronicleDate ?? string.Empty;
            Text = pText ?? string.Empty;
        }

        public string ChronicleDate { get; private set; }
        public string Text { get; private set; }
    }

    internal sealed class ChronicleTextExportPeriod
    {
        public ChronicleTextExportPeriod(string pTitle, string pStartDate,
            string pEndDate, IList<ChronicleTextExportEvent> pEvents)
        {
            Title = pTitle ?? string.Empty;
            StartDate = pStartDate ?? string.Empty;
            EndDate = pEndDate ?? string.Empty;
            Events = pEvents == null
                ? new List<ChronicleTextExportEvent>()
                : new List<ChronicleTextExportEvent>(pEvents);
        }

        public string Title { get; private set; }
        public string StartDate { get; private set; }
        public string EndDate { get; private set; }
        public List<ChronicleTextExportEvent> Events { get; private set; }
    }

    internal sealed class ChronicleTextExportDynasty
    {
        public ChronicleTextExportDynasty(string pName, string pStartDate,
            string pEndDate, IList<ChronicleTextExportPeriod> pReigns)
        {
            Name = pName ?? string.Empty;
            StartDate = pStartDate ?? string.Empty;
            EndDate = pEndDate ?? string.Empty;
            Reigns = pReigns == null
                ? new List<ChronicleTextExportPeriod>()
                : new List<ChronicleTextExportPeriod>(pReigns);
        }

        public string Name { get; private set; }
        public string StartDate { get; private set; }
        public string EndDate { get; private set; }
        public List<ChronicleTextExportPeriod> Reigns { get; private set; }
    }

    internal sealed class ChronicleTextExportSnapshot
    {
        private ChronicleTextExportSnapshot(
            IList<ChronicleTextExportEvent> pEvents,
            IList<ChronicleTextExportDynasty> pDynasties,
            IList<ChronicleTextExportPeriod> pPeriods)
        {
            Events = pEvents == null ? new List<ChronicleTextExportEvent>()
                : new List<ChronicleTextExportEvent>(pEvents);
            Dynasties = pDynasties == null
                ? new List<ChronicleTextExportDynasty>()
                : new List<ChronicleTextExportDynasty>(pDynasties);
            Periods = pPeriods == null ? new List<ChronicleTextExportPeriod>()
                : new List<ChronicleTextExportPeriod>(pPeriods);
        }

        public List<ChronicleTextExportEvent> Events { get; private set; }
        public List<ChronicleTextExportDynasty> Dynasties { get; private set; }
        public List<ChronicleTextExportPeriod> Periods { get; private set; }

        public static ChronicleTextExportSnapshot ForPerson(
            IList<ChronicleTextExportEvent> pEvents)
        {
            return new ChronicleTextExportSnapshot(pEvents, null, null);
        }

        public static ChronicleTextExportSnapshot ForKingdom(
            IList<ChronicleTextExportDynasty> pDynasties)
        {
            return new ChronicleTextExportSnapshot(null, pDynasties, null);
        }

        public static ChronicleTextExportSnapshot ForCity(
            IList<ChronicleTextExportPeriod> pPeriods)
        {
            return new ChronicleTextExportSnapshot(null, null, pPeriods);
        }
    }
}
