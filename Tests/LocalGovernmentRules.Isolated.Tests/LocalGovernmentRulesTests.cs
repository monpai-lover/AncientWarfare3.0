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
        Equal(2, upgraded.SchemaVersion,
            "legacy custom courts upgrade to the local-template schema");
        Equal(1, upgraded.Offices.Count,
            "city offices leave the central template during migration");
        Equal(1, upgraded.LocalTemplates.Count,
            "legacy city offices form one default local template");
        Equal(2, upgraded.LocalTemplates[0].Offices.Count,
            "all legacy city offices migrate together");
        Equal(1, upgraded.LocalTemplates[0].Edges.Count,
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
        Equal(2, importedLegacy.SchemaVersion,
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
        Equal(2, importedInstance.ResolvedSnapshot.SchemaVersion,
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
}
