using System;
using System.Data.SQLite;

namespace AncientWarfare3.core.lineage
{
    public sealed class ConferredPosthumousFactContext
    {
        public long ActorId = -1;
        public string ActorName = "";
        public ConferredPosthumousRole Roles;
        public string HighestOfficeId = "";
        public int HighestOfficeRank;
        public int CivilMerit;
        public int GeneralMerit;
        public int TroopPower;
        public int ServiceYears;
        public int NobleRank;
        public string NobleTitleStyle = "";
        public string NobleTitleName = "";
    }

    public sealed class ConferredPosthumousTitleFactService
    {
        private readonly SQLiteConnection _db;
        private readonly Func<double, int> _yearFromTime;

        public ConferredPosthumousTitleFactService(SQLiteConnection pDb,
            Func<double, int> pYearFromTime)
        {
            _db = pDb ?? throw new ArgumentNullException(nameof(pDb));
            _yearFromTime = pYearFromTime ??
                            throw new ArgumentNullException(nameof(pYearFromTime));
        }

        public bool TryBuild(long pKingdomId, string pStateName,
            string pKingdomColor,
            ConferredPosthumousCandidateRecord pCandidate,
            out RulerTitleFacts pFacts,
            out ConferredPosthumousFactContext pContext)
        {
            pFacts = null;
            pContext = null;
            if (pKingdomId < 0 || pCandidate?.ActorId < 0 ||
                !ConferredPosthumousTitleRules.IsEligibleRole(pCandidate.Roles))
                return false;

            try
            {
                if (!TryReadArchive(pCandidate.ActorId, out ArchiveFacts archive))
                    return false;

                var input = new ConferredPosthumousFactInput
                {
                    ActorId = pCandidate.ActorId,
                    KingdomId = pKingdomId,
                    ShiId = archive.ShiId,
                    ActorName = string.IsNullOrEmpty(archive.ActorName)
                        ? pCandidate.ActorName
                        : archive.ActorName,
                    StateName = pStateName ?? "",
                    KingdomColor = pKingdomColor ?? "",
                    DeathCause = archive.DeathCause,
                    Age = Math.Max(0, archive.DeathYear - archive.BirthYear),
                    HasKnownPatriline = archive.ParentId1 >= 0
                };
                var context = new ConferredPosthumousFactContext
                {
                    ActorId = pCandidate.ActorId,
                    ActorName = input.ActorName,
                    Roles = pCandidate.Roles
                };

                if ((pCandidate.Roles &
                     ConferredPosthumousRole.FormerRuler) != 0)
                    input.ReignFacts = ReadReignFacts(pKingdomId,
                        pCandidate.ActorId, input);

                ReadOfficialFacts(pKingdomId, pCandidate.ActorId,
                    archive.DeathYear, input, context);
                ReadGeneralFacts(pKingdomId, pCandidate.ActorId,
                    archive.DeathYear, input, context);
                ReadNobleFacts(pKingdomId, pCandidate.ActorId, input,
                    context);
                ReadPersonalSnapshot(pCandidate.ActorId, input);

                input.ServiceYears = Math.Max(input.ServiceYears,
                    context.ServiceYears);
                pFacts = ConferredPosthumousFactRules.MapArchivedFacts(input);
                pContext = context;
                return pFacts.ActorId >= 0 && pFacts.KingdomId >= 0;
            }
            catch (Exception)
            {
                pFacts = null;
                pContext = null;
                return false;
            }
        }

        private bool TryReadArchive(long pActorId, out ArchiveFacts pFacts)
        {
            pFacts = default;
            using var command = new SQLiteCommand(
                "SELECT IFNULL(DISPLAY_NAME,''),IFNULL(SHI_ID,-1)," +
                "IFNULL(BIRTH_TIME,0),IFNULL(DEATH_TIME,-1)," +
                "IFNULL(DEATH_CAUSE,''),IFNULL(PARENT_ID_1,-1),IS_ALIVE " +
                "FROM ActorArchive WHERE ID=@actor LIMIT 1", _db);
            command.Parameters.AddWithValue("@actor", pActorId);
            using SQLiteDataReader reader = command.ExecuteReader();
            if (!reader.Read() || ValueInt(reader, 6, 1) != 0) return false;
            double birth = ValueDouble(reader, 2, 0d);
            double death = ValueDouble(reader, 3, -1d);
            pFacts = new ArchiveFacts
            {
                ActorName = ValueString(reader, 0),
                ShiId = ValueLong(reader, 1, -1L),
                BirthYear = _yearFromTime(birth),
                DeathYear = death < 0d ? _yearFromTime(birth) :
                    _yearFromTime(death),
                DeathCause = ValueString(reader, 4),
                ParentId1 = ValueLong(reader, 5, -1L)
            };
            return true;
        }

        private RulerTitleFacts ReadReignFacts(long pKingdomId,
            long pActorId, ConferredPosthumousFactInput pInput)
        {
            using var command = new SQLiteCommand(
                "SELECT REIGN_ID,IFNULL(SHI_ID,-1),IFNULL(DYNASTY_ID,-1)," +
                "IFNULL(MANDATE_PERIOD_ID,-1),IFNULL(HIGHEST_TITLE,0)," +
                "IFNULL(STATE_NAME_SNAPSHOT,''),IFNULL(KINGDOM_COLOR,'')," +
                "START_TIME,END_TIME,IFNULL(END_REASON,'died')," +
                "IFNULL(START_POPULATION,0),IFNULL(END_POPULATION,0)," +
                "IFNULL(START_CITY_COUNT,0),IFNULL(END_CITY_COUNT,0)," +
                "IFNULL(REIGN_INDEX,0),IFNULL(IS_FOUNDER,0)," +
                "IFNULL(WAR_WINS,0),IFNULL(WAR_LOSSES,0)," +
                "IFNULL(LOST_CAPITAL,0),IFNULL(DEATH_CAUSE,'') " +
                "FROM KingdomReign WHERE KINGDOM_ID=@kingdom " +
                "AND KING_ACTOR_ID=@actor " +
                "ORDER BY START_TIME DESC,REIGN_ID DESC LIMIT 1", _db);
            command.Parameters.AddWithValue("@kingdom", pKingdomId);
            command.Parameters.AddWithValue("@actor", pActorId);
            using SQLiteDataReader reader = command.ExecuteReader();
            if (!reader.Read()) return null;
            int startYear = _yearFromTime(ValueDouble(reader, 7, 0d));
            int endYear = _yearFromTime(ValueDouble(reader, 8, 0d));
            int startCities = ValueInt(reader, 12, 0);
            int endCities = ValueInt(reader, 13, 0);
            long mandatePeriodId = ValueLong(reader, 3, -1L);
            return new RulerTitleFacts
            {
                ActorId = pActorId,
                KingdomId = pKingdomId,
                ReignId = ValueLong(reader, 0, -1L),
                ShiId = ValueLong(reader, 1, pInput.ShiId),
                DynastyId = ValueLong(reader, 2, -1L),
                MandatePeriodId = mandatePeriodId,
                ActorName = pInput.ActorName,
                StateName = ValueString(reader, 5),
                KingdomColor = ValueString(reader, 6),
                EndReason = ValueString(reader, 9),
                DeathCause = ValueString(reader, 19),
                HighestTitle = ValueInt(reader, 4, 0),
                Age = pInput.Age,
                StartYear = startYear,
                EndYear = endYear,
                ReignYears = Math.Max(1, endYear - startYear + 1),
                ReignIndex = ValueInt(reader, 14, 0),
                StartPopulation = ValueInt(reader, 10, 0),
                EndPopulation = ValueInt(reader, 11, 0),
                EndCityCount = endCities,
                CityDelta = endCities - startCities,
                IsFounder = ValueInt(reader, 15, 0) != 0,
                WarWins = ValueInt(reader, 16, 0),
                WarLosses = ValueInt(reader, 17, 0),
                LostCapital = ValueInt(reader, 18, 0) != 0,
                OrderDelta = ValueInt(reader, 18, 0) != 0 ? -1 : 0,
                IsMandate = mandatePeriodId >= 0
            };
        }

        private void ReadOfficialFacts(long pKingdomId, long pActorId,
            int pDeathYear, ConferredPosthumousFactInput pInput,
            ConferredPosthumousFactContext pContext)
        {
            using (var command = new SQLiteCommand(
                       "SELECT IFNULL(OFFICE_ID,'')," +
                       "IFNULL(RANK_AT_APPOINTMENT,0)," +
                       "IFNULL(APPOINTED_YEAR,-1),IFNULL(ENDED_YEAR,-1) " +
                       "FROM CourtOfficer WHERE KINGDOM_ID=@kingdom " +
                       "AND ACTOR_ID=@actor ORDER BY RANK_AT_APPOINTMENT DESC," +
                       "APPOINTED_TIME ASC,OFFICER_ID ASC", _db))
            {
                command.Parameters.AddWithValue("@kingdom", pKingdomId);
                command.Parameters.AddWithValue("@actor", pActorId);
                using SQLiteDataReader reader = command.ExecuteReader();
                int firstYear = int.MaxValue;
                int lastYear = -1;
                while (reader.Read())
                {
                    int rank = ValueInt(reader, 1, 0);
                    if (rank > pContext.HighestOfficeRank)
                    {
                        pContext.HighestOfficeRank = rank;
                        pContext.HighestOfficeId = ValueString(reader, 0);
                    }
                    int appointed = ValueInt(reader, 2, -1);
                    int ended = ValueInt(reader, 3, -1);
                    if (appointed >= 0) firstYear = Math.Min(firstYear, appointed);
                    lastYear = Math.Max(lastYear,
                        ended >= 0 ? ended : pDeathYear);
                }
                if (firstYear != int.MaxValue && lastYear >= firstYear)
                    pContext.ServiceYears = Math.Max(1,
                        lastYear - firstYear + 1);
            }

            using var state = new SQLiteCommand(
                "SELECT IFNULL(RANK,0),IFNULL(OFFICE_ID,'')," +
                "IFNULL(MERIT,0),IFNULL(SENIORITY,0) " +
                "FROM OfficialCareerState WHERE ACTOR_ID=@actor " +
                "AND KINGDOM_ID=@kingdom LIMIT 1", _db);
            state.Parameters.AddWithValue("@actor", pActorId);
            state.Parameters.AddWithValue("@kingdom", pKingdomId);
            using SQLiteDataReader stateReader = state.ExecuteReader();
            if (!stateReader.Read())
            {
                pInput.HighestOfficeRank = pContext.HighestOfficeRank;
                return;
            }
            int stateRank = ValueInt(stateReader, 0, 0);
            if (stateRank > pContext.HighestOfficeRank)
            {
                pContext.HighestOfficeRank = stateRank;
                pContext.HighestOfficeId = ValueString(stateReader, 1);
            }
            pContext.CivilMerit = Math.Max(0,
                (int)Math.Round(ValueDouble(stateReader, 2, 0d) * 100d));
            pContext.ServiceYears = Math.Max(pContext.ServiceYears,
                ValueInt(stateReader, 3, 0));
            pInput.HighestOfficeRank = pContext.HighestOfficeRank;
            pInput.CivilMerit = pContext.CivilMerit;
        }

        private void ReadGeneralFacts(long pKingdomId, long pActorId,
            int pDeathYear, ConferredPosthumousFactInput pInput,
            ConferredPosthumousFactContext pContext)
        {
            using var command = new SQLiteCommand(
                "SELECT IFNULL(MERIT_SCORE,0)," +
                "IFNULL(TROOP_POWER_SNAPSHOT,0),IFNULL(APPOINTED_TIME,-1) " +
                "FROM GeneralState WHERE KINGDOM_ID=@kingdom " +
                "AND ACTOR_ID=@actor LIMIT 1", _db);
            command.Parameters.AddWithValue("@kingdom", pKingdomId);
            command.Parameters.AddWithValue("@actor", pActorId);
            using SQLiteDataReader reader = command.ExecuteReader();
            if (!reader.Read()) return;
            pContext.GeneralMerit = ValueInt(reader, 0, 0);
            pContext.TroopPower = ValueInt(reader, 1, 0);
            double appointed = ValueDouble(reader, 2, -1d);
            if (appointed >= 0d)
                pContext.ServiceYears = Math.Max(pContext.ServiceYears,
                    Math.Max(1, pDeathYear - _yearFromTime(appointed) + 1));
            pInput.GeneralMerit = pContext.GeneralMerit;
            pInput.TroopPower = pContext.TroopPower;
        }

        private void ReadNobleFacts(long pKingdomId, long pActorId,
            ConferredPosthumousFactInput pInput,
            ConferredPosthumousFactContext pContext)
        {
            using var command = new SQLiteCommand(
                "SELECT IFNULL(NOBLE_RANK,0),IFNULL(TITLE_STYLE,'')," +
                "IFNULL(TITLE_NAME,'') FROM Enfeoffment " +
                "WHERE KINGDOM_ID=@kingdom AND ACTOR_ID=@actor " +
                "ORDER BY NOBLE_RANK DESC,START_TIME DESC,GRANT_ID DESC LIMIT 1",
                _db);
            command.Parameters.AddWithValue("@kingdom", pKingdomId);
            command.Parameters.AddWithValue("@actor", pActorId);
            using SQLiteDataReader reader = command.ExecuteReader();
            if (!reader.Read()) return;
            pContext.NobleRank = ValueInt(reader, 0, 0);
            pContext.NobleTitleStyle = ValueString(reader, 1);
            pContext.NobleTitleName = ValueString(reader, 2);
            pInput.NobleRank = pContext.NobleRank;
        }

        private void ReadPersonalSnapshot(long pActorId,
            ConferredPosthumousFactInput pInput)
        {
            using var command = new SQLiteCommand(
                "SELECT IFNULL(DIPLOMACY,0),IFNULL(WARFARE,0)," +
                "IFNULL(STEWARDSHIP,0),IFNULL(INTELLIGENCE,0)," +
                "IFNULL(HEALTH,0),IFNULL(COMBAT,0),IFNULL(TRAIT_FLAGS,0) " +
                "FROM ActorTitleFactSnapshot WHERE ACTOR_ID=@actor LIMIT 1", _db);
            command.Parameters.AddWithValue("@actor", pActorId);
            using SQLiteDataReader reader = command.ExecuteReader();
            if (!reader.Read()) return;
            pInput.Diplomacy = ValueInt(reader, 0, 0);
            pInput.Warfare = ValueInt(reader, 1, 0);
            pInput.Stewardship = ValueInt(reader, 2, 0);
            pInput.Intelligence = ValueInt(reader, 3, 0);
            pInput.Health = ValueInt(reader, 4, 0);
            pInput.Combat = ValueInt(reader, 5, 0);
            pInput.Traits = (RulerTraitFlags)ValueLong(reader, 6, 0L);
            pInput.HasPersonalSnapshot = true;
        }

        private struct ArchiveFacts
        {
            public string ActorName;
            public long ShiId;
            public int BirthYear;
            public int DeathYear;
            public string DeathCause;
            public long ParentId1;
        }

        private static string ValueString(SQLiteDataReader pReader, int pIndex)
        {
            return pReader.IsDBNull(pIndex)
                ? ""
                : Convert.ToString(pReader.GetValue(pIndex)) ?? "";
        }

        private static long ValueLong(SQLiteDataReader pReader, int pIndex,
            long pFallback)
        {
            return pReader.IsDBNull(pIndex)
                ? pFallback
                : Convert.ToInt64(pReader.GetValue(pIndex));
        }

        private static int ValueInt(SQLiteDataReader pReader, int pIndex,
            int pFallback)
        {
            return pReader.IsDBNull(pIndex)
                ? pFallback
                : Convert.ToInt32(pReader.GetValue(pIndex));
        }

        private static double ValueDouble(SQLiteDataReader pReader, int pIndex,
            double pFallback)
        {
            return pReader.IsDBNull(pIndex)
                ? pFallback
                : Convert.ToDouble(pReader.GetValue(pIndex));
        }
    }
}
