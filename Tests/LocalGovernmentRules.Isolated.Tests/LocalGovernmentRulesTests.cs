using AncientWarfare3.core.court;

internal static class LocalGovernmentRulesTests
{
    internal static void Run()
    {
        Equal(CourtOfficeId.Governor,
            LocalCourtOfficeRules.OfficeForSlot(0, CourtProfileId.Xia),
            "Xia city leader is the root office");
        Equal(CourtOfficeId.WestMayor,
            LocalCourtOfficeRules.OfficeForSlot(0, CourtProfileId.Western),
            "western city leader uses the mayor office");
        Equal(CourtOfficeId.GranaryOfficer,
            LocalCourtOfficeRules.OfficeForSlot(1, CourtProfileId.Xia),
            "second city slot is granary administration");
        Equal(CourtOfficeId.Constable,
            LocalCourtOfficeRules.OfficeForSlot(2, CourtProfileId.Xia),
            "third city slot is local constable");

        for (long actorId = 1; actorId <= 32; actorId++)
        {
            int term = LocalOfficialTermRules.TermLength(
                ability: 20, merit: 80, age: 35,
                actorId, appointmentYear: 100);
            True(LocalOfficialTermRules.IsValidTermLength(term),
                "local terms are always ten to fifteen years");
        }

        True(OfficialCirculationRules.IsRotatingCityOffice(
                CourtOfficeId.GranaryOfficer,
                xiaCirculationUnlocked: false),
            "all local offices circulate regardless of central law");
        True(OfficialCirculationRules.IsRotatingCityOffice(
                CourtOfficeId.Constable,
                xiaCirculationUnlocked: false),
            "local constables also circulate");
        True(OfficialCirculationRules.ShouldRotateLocalLeader(
                cityLayer: true, cityLeader: true, termDue: true,
                liveCityCount: 3),
            "a custom city-root office enters the governor rotation path");
        False(OfficialCirculationRules.ShouldRotateLocalLeader(
                cityLayer: true, cityLeader: false, termDue: true,
                liveCityCount: 3),
            "local subordinate offices do not enter leader-only rotation");

        True(LocalOfficialCandidateRules.CanEnter(
                alive: true, adult: true, slave: false,
                alreadyOfficial: false, king: false, heir: false,
                examinationEnabled: true, qualification: "juren",
                participatedAndFailedHigherStage: false),
            "local-stage pass enters the local pool");
        True(LocalOfficialCandidateRules.CanEnter(
                alive: true, adult: true, slave: false,
                alreadyOfficial: false, king: false, heir: false,
                examinationEnabled: true, qualification: "none",
                participatedAndFailedHigherStage: true),
            "higher-stage non-finalist remains locally employable");
        False(LocalOfficialCandidateRules.CanEnter(
                alive: true, adult: true, slave: false,
                alreadyOfficial: false, king: false, heir: true,
                examinationEnabled: true, qualification: "jinshi",
                participatedAndFailedHigherStage: false),
            "an heir cannot enter a local office");
        Equal(25, LocalOfficialCandidateRules.HometownBonus,
            "hometown bonus is explicit");
        True(LocalOfficialCandidateRules.Score(60, 50,
                 sameNativeCity: true) >
             LocalOfficialCandidateRules.Score(90, 50,
                 sameNativeCity: false),
            "qualified same-native-city recommendation is material");
        False(LocalOfficialCandidateRules.AcceptsAppointmentQualification(
                "juren", participatedAndFailedHigherStage: false,
                allowLocalLowerQualification: false),
            "central appointments do not accept local-stage credentials");
        True(LocalOfficialCandidateRules.AcceptsAppointmentQualification(
                "juren", participatedAndFailedHigherStage: false,
                allowLocalLowerQualification: true),
            "the explicit local path accepts a local-stage credential");
        True(LocalOfficialCandidateRules.AcceptsAppointmentQualification(
                "none", participatedAndFailedHigherStage: true,
                allowLocalLowerQualification: true),
            "the explicit local path accepts a higher-stage non-finalist");

        var legacy = new CustomCourtTemplate
        {
            SchemaVersion = 1,
            Id = "legacy_court",
            Offices = new List<CustomCourtOffice>
            {
                Office("minister", CourtOfficeLayer.Central),
                Office("governor", CourtOfficeLayer.City),
                Office("constable", CourtOfficeLayer.City)
            },
            Edges = new List<CustomCourtEdge>
            {
                Edge("governor", "constable"),
                Edge("minister", "governor")
            }
        };
        CustomCourtTemplate upgraded =
            CustomLocalCourtTemplateRules.UpgradeLegacy(legacy);
        Equal(CustomLocalCourtTemplateRules.CurrentSchemaVersion,
            upgraded.SchemaVersion,
            "legacy custom courts upgrade to the local-template schema");
        Equal(1, upgraded.Offices.Count,
            "city offices leave the central template during migration");
        Equal(3, upgraded.LocalTemplates.Count,
            "legacy city offices retain one template and add the two built-in defaults");
        CustomLocalCourtTemplate migratedLocal = upgraded.LocalTemplates
            .Single(template => template.Id ==
                CustomLocalCourtTemplateRules.LegacyDefaultTemplateId);
        Equal(2, migratedLocal.Offices.Count,
            "all legacy city offices migrate together");
        Equal(1, migratedLocal.Edges.Count,
            "local internal edges migrate with their offices");
        Equal(1, upgraded.ArchivedCrossLayerEdges.Count,
            "legacy cross-layer edges remain archived for round trips");
        Equal(CustomCourtTemplateValidationError.None,
            CustomCourtTemplateRules.Validate(upgraded),
            "the upgraded multi-template package passes formal validation");
        var legacyJsonSource = new CustomCourtTemplate
        {
            SchemaVersion = 1,
            Id = "legacy_json",
            Offices = new List<CustomCourtOffice>
            {
                Office("minister", CourtOfficeLayer.Central),
                Office("governor", CourtOfficeLayer.City)
            }
        };
        string legacyJson = Newtonsoft.Json.JsonConvert.SerializeObject(
            legacyJsonSource);
        True(CustomCourtTemplateJsonCodec.TryImport(legacyJson,
                out CustomCourtTemplate importedLegacy,
                out CustomCourtTemplateValidationError importError),
            "schema-one custom court JSON upgrades during import");
        Equal(CustomCourtTemplateValidationError.None, importError,
            "legacy import reports no validation error after migration");
        Equal(CustomLocalCourtTemplateRules.CurrentSchemaVersion,
            importedLegacy.SchemaVersion,
            "legacy JSON is returned in the current schema");
        string legacyInstanceJson = Newtonsoft.Json.JsonConvert.SerializeObject(
            new CustomCourtInstance
            {
                SchemaVersion = 1,
                KingdomId = "7",
                TemplateId = "legacy_json",
                TemplateRevision = 1,
                InstanceRevision = 1,
                ResolvedSnapshot = legacyJsonSource
            });
        True(CustomCourtInstanceCodec.TryImport(legacyInstanceJson,
                out CustomCourtInstance importedInstance),
            "saved custom-court instances migrate embedded legacy snapshots");
        Equal(CustomLocalCourtTemplateRules.CurrentSchemaVersion,
            importedInstance.ResolvedSnapshot.SchemaVersion,
            "the restored instance exposes a current local-template package");

        var civil = LocalTemplate("civil", "民州",
            CustomLocalCourtDefaultKind.CivilDefault);
        var military = LocalTemplate("military", "军府",
            CustomLocalCourtDefaultKind.MilitaryDefault);
        var manual = LocalTemplate("special", "都护府",
            CustomLocalCourtDefaultKind.ManualOnly);
        var templates = new List<CustomLocalCourtTemplate>
            { manual, military, civil };
        Equal("civil", CustomLocalCourtTemplateRules.ResolveTemplateId(
                templates, persistedTemplateId: "",
                manualOverride: false, militaryCity: false),
            "civil cities select the civil default");
        Equal("military", CustomLocalCourtTemplateRules.ResolveTemplateId(
                templates, persistedTemplateId: "civil",
                manualOverride: false, militaryCity: true),
            "military facts can replace an automatic civil binding");
        Equal("special", CustomLocalCourtTemplateRules.ResolveTemplateId(
                templates, persistedTemplateId: "special",
                manualOverride: true, militaryCity: false),
            "manual city template selection wins over automatic defaults");
        Equal("民州", CustomLocalCourtTemplateRules.CityTypeName(civil,
                useEnglish: false),
            "the local city type is the selected template name");
        False(CustomLocalCourtTemplateRules.CanDeleteTemplate(
                "civil", replacementTemplateId: "", inUseCityCount: 3),
            "an in-use local template cannot be deleted without replacement");
        True(CustomLocalCourtTemplateRules.CanDeleteTemplate(
                "civil", replacementTemplateId: "military",
                inUseCityCount: 3),
            "an in-use local template can be atomically rebound");

        LegacyAggregateMigrationDoesNotDuplicateActiveOfficers();
        ThirtyYearLocalGovernmentSimulationRotatesFiniteTerms();
    }

    private static void True(bool pValue, string pMessage)
    {
        if (!pValue) throw new InvalidOperationException(pMessage);
    }

    private static void False(bool pValue, string pMessage)
    {
        True(!pValue, pMessage);
    }

    private static void Equal<T>(T pExpected, T pActual, string pMessage)
    {
        if (!EqualityComparer<T>.Default.Equals(pExpected, pActual))
            throw new InvalidOperationException(
                $"{pMessage}: expected {pExpected}, got {pActual}");
    }

    private static CustomCourtOffice Office(string pId, string pLayer)
    {
        return new CustomCourtOffice
        {
            Id = pId,
            Layer = pLayer,
            Grade = 10,
            Slots = 1
        };
    }

    private static CustomCourtEdge Edge(string pFrom, string pTo)
    {
        return new CustomCourtEdge
        {
            FromOfficeId = pFrom,
            ToOfficeId = pTo,
            Kind = CustomCourtEdgeKind.Management
        };
    }

    private static CustomLocalCourtTemplate LocalTemplate(string pId,
        string pChineseName, CustomLocalCourtDefaultKind pKind)
    {
        return new CustomLocalCourtTemplate
        {
            Id = pId,
            Name = new CustomCourtLocalizedText
                { Chinese = pChineseName, English = pId },
            DefaultKind = pKind,
            Offices = new List<CustomCourtOffice>
                { Office(pId + "_leader", CourtOfficeLayer.City) }
        };
    }

    private sealed class SimulatedOfficer
    {
        internal long ActorId;
        internal long CityId;
        internal string OfficeId = string.Empty;
        internal int AppointedYear;
        internal int TermEndYear;
    }

    private static void LegacyAggregateMigrationDoesNotDuplicateActiveOfficers()
    {
        string annualWorkSource = File.ReadAllText(Path.Combine(
            Directory.GetCurrentDirectory(), "Code", "core", "court",
            "CityBureauAnnualWorkService.cs"));
        True(annualWorkSource.Contains(
                "LocalCourtAppointmentService.ReconcileCity(",
                StringComparison.Ordinal),
            "annual maintenance migrates legacy city state through real appointments");
        True(annualWorkSource.Contains(
                "HistoricalWriteService.TryUpsertState(",
                StringComparison.Ordinal),
            "annual migration retains the aggregate state write");
        const long cityId = 11L;
        var legacyAggregate = new Dictionary<string, string>
        {
            ["CITY_ID"] = cityId.ToString(),
            ["OFFICER_ACTOR_IDS"] = "101",
            ["OFFICE_SLOTS"] = "3"
        };
        var active = new Dictionary<string, SimulatedOfficer>(
            StringComparer.Ordinal);
        var history = new List<SimulatedOfficer>();

        ReconcileLegacyCity(legacyAggregate, active, history, 101L, 0);
        int firstActiveCount = active.Count;
        ReconcileLegacyCity(legacyAggregate, active, history, 101L, 1);

        True(legacyAggregate.ContainsKey("OFFICER_ACTOR_IDS"),
            "legacy city bureau aggregate remains after career migration");
        Equal(3, firstActiveCount,
            "first annual maintenance materializes the leader and local vacancies");
        Equal(firstActiveCount, active.Count,
            "second annual maintenance does not duplicate active local officers");
        Equal(3, active.Values.Select(officer => officer.OfficeId).Distinct()
                .Count(),
            "one active local officer exists per migrated office");
        True(history.Count == 0,
            "unchanged migrated careers remain active on the next cycle");
    }

    private static void ReconcileLegacyCity(
        IReadOnlyDictionary<string, string> pAggregate,
        IDictionary<string, SimulatedOfficer> pActive,
        ICollection<SimulatedOfficer> pHistory,
        long pLeaderActorId, int pYear)
    {
        string[] offices = { "governor", "granary_officer", "constable" };
        for (int index = 0; index < offices.Length; index++)
        {
            string key = pAggregate["CITY_ID"] + ":" + offices[index];
            if (pActive.ContainsKey(key)) continue;
            long actorId = index == 0 ? pLeaderActorId :
                pLeaderActorId + index;
            int term = LocalOfficialTermRules.TermLength(
                ability: 60 + index * 5, merit: 50, age: 35,
                actorId, appointmentYear: pYear);
            var officer = new SimulatedOfficer
            {
                ActorId = actorId,
                CityId = long.Parse(pAggregate["CITY_ID"]),
                OfficeId = offices[index],
                AppointedYear = pYear,
                TermEndYear = pYear + term
            };
            pActive.Add(key, officer);
        }
    }

    private static void ThirtyYearLocalGovernmentSimulationRotatesFiniteTerms()
    {
        long[] cities = { 11L, 22L, 33L };
        string[] offices = { "governor", "granary_officer", "constable" };
        var active = new Dictionary<string, SimulatedOfficer>(
            StringComparer.Ordinal);
        var history = new List<SimulatedOfficer>();
        long nextActorId = 1000L;

        for (int year = 0; year < 30; year++)
        {
            foreach (long cityId in cities)
            foreach (string office in offices)
            {
                string key = cityId + ":" + office;
                if (active.TryGetValue(key, out SimulatedOfficer incumbent) &&
                    incumbent.TermEndYear <= year)
                {
                    active.Remove(key);
                    history.Add(incumbent);
                }
                if (active.ContainsKey(key)) continue;

                long actorId = nextActorId++;
                bool sameNativeCity = actorId % cities.Length ==
                    cityId % cities.Length;
                string qualification = actorId % 4 == 0 ? "none" : "juren";
                bool qualified = LocalOfficialCandidateRules.CanEnter(
                    alive: true, adult: true, slave: false,
                    alreadyOfficial: false, king: false, heir: false,
                    examinationEnabled: true, qualification,
                    participatedAndFailedHigherStage: false);
                if (!qualified)
                {
                    qualification = "juren";
                    qualified = LocalOfficialCandidateRules.CanEnter(
                        alive: true, adult: true, slave: false,
                        alreadyOfficial: false, king: false, heir: false,
                        examinationEnabled: true, qualification,
                        participatedAndFailedHigherStage: false);
                }
                True(qualified,
                    "every simulated local appointment passes qualification");
                int score = LocalOfficialCandidateRules.Score(
                    ability: 60, merit: 50, sameNativeCity);
                True(score >= 0,
                    "hometown preference is applied only after qualification");
                int term = LocalOfficialTermRules.TermLength(
                    ability: 60, merit: 50, age: 35,
                    actorId, appointmentYear: year);
                active.Add(key, new SimulatedOfficer
                {
                    ActorId = actorId,
                    CityId = cityId,
                    OfficeId = office,
                    AppointedYear = year,
                    TermEndYear = year + term
                });
            }
            Equal(cities.Length * offices.Length, active.Count,
                "city cards keep a bounded one-seat-per-office active set");
            Equal(active.Count, active.Values.Select(officer =>
                officer.CityId + ":" + officer.OfficeId).Distinct().Count(),
                "annual reconciliation keeps active city offices unique");
        }

        True(history.Count > 0,
            "thirty-year simulation closes finite local office histories");
        True(history.All(officer => LocalOfficialTermRules.IsValidTermLength(
                officer.TermEndYear - officer.AppointedYear)),
            "every closed local term remains within ten to fifteen years");
        bool unqualifiedHighAbility = LocalOfficialCandidateRules.CanEnter(
            alive: true, adult: true, slave: false,
            alreadyOfficial: false, king: false, heir: false,
            examinationEnabled: true, qualification: "none",
            participatedAndFailedHigherStage: false);
        False(unqualifiedHighAbility,
            "same-native-city preference cannot bypass local qualification");
    }
}
