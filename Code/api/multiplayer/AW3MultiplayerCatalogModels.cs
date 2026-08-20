using System;
using System.Globalization;
using System.Text;

namespace AncientWarfare3.api.multiplayer
{
    public enum AW3WindowCategory : byte
    {
        Domestic = 0,
        DiplomacyAndWar = 1,
        Realm = 2,
        Mandate = 3,
        Records = 4
    }

    [Flags]
    public enum AW3WindowContextRequirement : ushort
    {
        None = 0,
        Country = 1,
        TargetCountry = 2,
        Actor = 4,
        TargetActor = 8,
        City = 16,
        Shi = 32,
        School = 64,
        Office = 128,
        AnySubject = 256
    }

    public enum AW3WindowKind : byte
    {
        LineageOverview = 0,
        ShiBranchList = 1,
        FamilyTree = 2,
        History = 3,
        KingdomRoster = 4,
        PolicyTree = 5,
        AncestryAnalysis = 6,
        MandateDynasty = 7,
        MandateCycle = 8,
        MandateDecisions = 9,
        VassalRelations = 10,
        WarTargets = 11,
        Court = 12,
        CourtAppointment = 13,
        CourtDisposition = 14,
        CourtAuxiliaryLaws = 15,
        InheritanceLaws = 16,
        School = 17,
        SchoolRoster = 18,
        NameDecision = 19,
        ConferredPosthumous = 20,
        CentralPower = 21,
        Feudatories = 22,
        DiplomacyConversations = 23,
        DiplomaticWarDeclaration = 24,
        DiplomaticMarriage = 25,
        CivilServiceExam = 26,
        RulerHousehold = 27,
        HouseholdOffer = 28,
        Supporters = 29,
        VirtualTitles = 30,
        MilitaryGovernorate = 31,
        CustomCourtWorkflow = 32,
        BanditAmnestySettlement = 33
    }

    public enum AW3WindowOpenStatus : byte
    {
        Opened = 0,
        InvalidContext = 1,
        NotFound = 2,
        Unavailable = 3
    }

    public sealed class AW3WindowDescriptor
    {
        public AW3WindowDescriptor(AW3WindowKind kind, string windowId,
            string titleKey, string iconPath, AW3WindowCategory category,
            AW3WindowContextRequirement requirements,
            bool countryLauncher, int payloadSchemaVersion)
        {
            if (!Enum.IsDefined(typeof(AW3WindowKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (!Enum.IsDefined(typeof(AW3WindowCategory), category))
                throw new ArgumentOutOfRangeException(nameof(category));
            if (payloadSchemaVersion <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(payloadSchemaVersion));
            Kind = kind;
            WindowId = AW3CatalogValidation.Token(windowId,
                nameof(windowId), 128);
            TitleKey = AW3CatalogValidation.Display(titleKey,
                nameof(titleKey), 128);
            IconPath = AW3CatalogValidation.Path(iconPath,
                nameof(iconPath));
            Category = category;
            Requirements = requirements;
            CountryLauncher = countryLauncher;
            PayloadSchemaVersion = payloadSchemaVersion;
        }

        public AW3WindowKind Kind { get; }
        public string WindowId { get; }
        public string TitleKey { get; }
        public string IconPath { get; }
        public AW3WindowCategory Category { get; }
        public AW3WindowContextRequirement Requirements { get; }
        public bool CountryLauncher { get; }
        public int PayloadSchemaVersion { get; }
    }

    public sealed class AW3WindowOpenRequest
    {
        private AW3WindowOpenRequest(AW3WindowKind kind, long countryId,
            long targetCountryId, long actorId, long targetActorId,
            long cityId, long shiId, string key, string schoolId,
            string officeId)
        {
            if (!Enum.IsDefined(typeof(AW3WindowKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            Kind = kind;
            CountryId = countryId;
            TargetCountryId = targetCountryId;
            ActorId = actorId;
            TargetActorId = targetActorId;
            CityId = cityId;
            ShiId = shiId;
            Key = key ?? string.Empty;
            SchoolId = schoolId ?? string.Empty;
            OfficeId = officeId ?? string.Empty;
        }

        public AW3WindowKind Kind { get; }
        public long CountryId { get; }
        public long TargetCountryId { get; }
        public long ActorId { get; }
        public long TargetActorId { get; }
        public long CityId { get; }
        public long ShiId { get; }
        public string Key { get; }
        public string SchoolId { get; }
        public string OfficeId { get; }

        public static AW3WindowOpenRequest Empty(AW3WindowKind kind) =>
            new AW3WindowOpenRequest(kind, -1L, -1L, -1L, -1L, -1L,
                -1L, string.Empty, string.Empty, string.Empty);

        public static AW3WindowOpenRequest ForCountry(AW3WindowKind kind,
            long countryId)
        {
            Positive(countryId, nameof(countryId));
            return new AW3WindowOpenRequest(kind, countryId, -1L, -1L,
                -1L, -1L, -1L, string.Empty, string.Empty, string.Empty);
        }

        public static AW3WindowOpenRequest ForCountries(AW3WindowKind kind,
            long countryId, long targetCountryId)
        {
            Positive(countryId, nameof(countryId));
            Positive(targetCountryId, nameof(targetCountryId));
            return new AW3WindowOpenRequest(kind, countryId,
                targetCountryId, -1L, -1L, -1L, -1L, string.Empty,
                string.Empty, string.Empty);
        }

        public static AW3WindowOpenRequest ForActor(AW3WindowKind kind,
            long actorId, long countryId = -1L)
        {
            Positive(actorId, nameof(actorId));
            OptionalPositive(countryId, nameof(countryId));
            return new AW3WindowOpenRequest(kind, countryId, -1L,
                actorId, -1L, -1L, -1L, string.Empty, string.Empty,
                string.Empty);
        }

        public static AW3WindowOpenRequest ForCountryActor(
            AW3WindowKind kind, long countryId, long actorId)
        {
            Positive(countryId, nameof(countryId));
            Positive(actorId, nameof(actorId));
            return new AW3WindowOpenRequest(kind, countryId, -1L,
                actorId, -1L, -1L, -1L, string.Empty, string.Empty,
                string.Empty);
        }

        public static AW3WindowOpenRequest ForCountryTargetActor(
            AW3WindowKind kind, long countryId, long targetActorId)
        {
            Positive(countryId, nameof(countryId));
            Positive(targetActorId, nameof(targetActorId));
            return new AW3WindowOpenRequest(kind, countryId, -1L, -1L,
                targetActorId, -1L, -1L, string.Empty, string.Empty,
                string.Empty);
        }

        public static AW3WindowOpenRequest ForCity(AW3WindowKind kind,
            long cityId)
        {
            Positive(cityId, nameof(cityId));
            return new AW3WindowOpenRequest(kind, -1L, -1L, -1L, -1L,
                cityId, -1L, string.Empty, string.Empty, string.Empty);
        }

        public static AW3WindowOpenRequest ForShi(AW3WindowKind kind,
            long shiId)
        {
            Positive(shiId, nameof(shiId));
            return new AW3WindowOpenRequest(kind, -1L, -1L, -1L, -1L,
                -1L, shiId, string.Empty, string.Empty, string.Empty);
        }

        public static AW3WindowOpenRequest ForActorShi(AW3WindowKind kind,
            long actorId, long shiId)
        {
            Positive(actorId, nameof(actorId));
            Positive(shiId, nameof(shiId));
            return new AW3WindowOpenRequest(kind, -1L, -1L, actorId,
                -1L, -1L, shiId, string.Empty, string.Empty,
                string.Empty);
        }

        public static AW3WindowOpenRequest ForKey(AW3WindowKind kind,
            string key)
        {
            string normalized = AW3CatalogValidation.Display(key,
                nameof(key), 64);
            return new AW3WindowOpenRequest(kind, -1L, -1L, -1L, -1L,
                -1L, -1L, normalized, string.Empty, string.Empty);
        }

        public static AW3WindowOpenRequest ForSchool(AW3WindowKind kind,
            string schoolId)
        {
            string key = AW3CatalogValidation.Token(schoolId,
                nameof(schoolId), 128);
            return new AW3WindowOpenRequest(kind, -1L, -1L, -1L, -1L,
                -1L, -1L, string.Empty, key, string.Empty);
        }

        public static AW3WindowOpenRequest ForOffice(AW3WindowKind kind,
            long countryId, string officeId)
        {
            Positive(countryId, nameof(countryId));
            string key = AW3CatalogValidation.Token(officeId,
                nameof(officeId), 128);
            return new AW3WindowOpenRequest(kind, countryId, -1L, -1L,
                -1L, -1L, -1L, string.Empty, string.Empty, key);
        }

        public bool IsValidFor(AW3WindowDescriptor descriptor)
        {
            if (descriptor == null || descriptor.Kind != Kind) return false;
            AW3WindowContextRequirement required = descriptor.Requirements;
            if (Has(required, AW3WindowContextRequirement.Country) &&
                CountryId <= 0) return false;
            if (Has(required, AW3WindowContextRequirement.TargetCountry) &&
                TargetCountryId <= 0) return false;
            if (Has(required, AW3WindowContextRequirement.Actor) &&
                ActorId <= 0) return false;
            if (Has(required, AW3WindowContextRequirement.TargetActor) &&
                TargetActorId <= 0) return false;
            if (Has(required, AW3WindowContextRequirement.City) &&
                CityId <= 0) return false;
            if (Has(required, AW3WindowContextRequirement.Shi) &&
                ShiId <= 0) return false;
            if (Has(required, AW3WindowContextRequirement.School) &&
                string.IsNullOrEmpty(SchoolId)) return false;
            if (Has(required, AW3WindowContextRequirement.Office) &&
                string.IsNullOrEmpty(OfficeId)) return false;
            if (Has(required, AW3WindowContextRequirement.AnySubject) &&
                CountryId <= 0 && ActorId <= 0 && CityId <= 0 &&
                ShiId <= 0 && string.IsNullOrEmpty(Key)) return false;
            return true;
        }

        private static bool Has(AW3WindowContextRequirement value,
            AW3WindowContextRequirement flag) => (value & flag) == flag;

        private static void Positive(long value, string parameter)
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(parameter);
        }

        private static void OptionalPositive(long value, string parameter)
        {
            if (value != -1L && value <= 0)
                throw new ArgumentOutOfRangeException(parameter);
        }
    }

    public sealed class AW3WindowOpenResult
    {
        public AW3WindowOpenResult(AW3WindowOpenStatus status,
            AW3WindowKind kind)
        {
            if (!Enum.IsDefined(typeof(AW3WindowOpenStatus), status))
                throw new ArgumentOutOfRangeException(nameof(status));
            if (!Enum.IsDefined(typeof(AW3WindowKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            Status = status;
            Kind = kind;
        }

        public AW3WindowOpenStatus Status { get; }
        public AW3WindowKind Kind { get; }
        public bool Opened => Status == AW3WindowOpenStatus.Opened;
    }

    public enum AW3CommandKind : byte
    {
        ConfigurePolicy = 0,
        SetPolicyClass = 1,
        StartPolicyNode = 2,
        TogglePolicyNodeLock = 3,
        StartCoreFabrication = 4,
        StartTargetedDecision = 5,
        StartMandateDecision = 6,
        AppointCourtOfficer = 7,
        SetCourtDisposition = 8,
        ChangeCourtAuxiliaryLaw = 9,
        ChangeInheritanceLaw = 10,
        RelocateFeudatory = 11,
        ReclaimFeudatoryCity = 12,
        AbolishFeudatory = 13,
        CreateDiplomacyProposal = 14,
        RespondDiplomacyProposal = 15,
        StartSpyNetwork = 16,
        StartForgeDocuments = 17,
        DeclareWar = 18,
        ConferPosthumousTitle = 19,
        RenameClan = 20,
        ChangeEra = 21,
        SetArmyRallyPoint = 22,
        SetArmyTargetCity = 23,
        SetArmyPosture = 24,
        CancelArmyOrder = 25,
        SubmitCivilServiceRanking = 26,
        RenameSurname = 27,
        GrantVirtualNobleTitle = 28,
        EditVirtualNobleTitle = 29,
        DeleteVirtualNobleTitle = 30,
        CreateMilitaryGovernorate = 31,
        DesignateMilitaryGovernorateSuccessor = 32,
        ReplaceMilitaryGovernorateGovernor = 33,
        ApplyCustomCourtTemplate = 34,
        GrantBanditAmnesty = 35,
        CommitDomesticHousehold = 36,
        FillCentralCourtVacancies = 37
    }

    public enum AW3CommandStatus : byte
    {
        Accepted = 0,
        Rejected = 1,
        Pending = 2
    }

    public enum AW3CommandError : byte
    {
        None = 0,
        InvalidRequest = 1,
        Unauthorized = 2,
        NotFound = 3,
        StaleState = 4,
        IllegalTarget = 5,
        InsufficientResources = 6,
        Cooldown = 7,
        Conflict = 8,
        ProviderUnavailable = 9,
        ExecutionFailed = 10
    }

    public sealed class AW3CommandDescriptor
    {
        public AW3CommandDescriptor(AW3CommandKind kind,
            AW3WindowCategory category,
            AW3WindowContextRequirement requirements,
            int payloadSchemaVersion)
        {
            if (!Enum.IsDefined(typeof(AW3CommandKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (!Enum.IsDefined(typeof(AW3WindowCategory), category))
                throw new ArgumentOutOfRangeException(nameof(category));
            if (payloadSchemaVersion <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(payloadSchemaVersion));
            Kind = kind;
            Category = category;
            Requirements = requirements;
            PayloadSchemaVersion = payloadSchemaVersion;
        }

        public AW3CommandKind Kind { get; }
        public AW3WindowCategory Category { get; }
        public AW3WindowContextRequirement Requirements { get; }
        public int PayloadSchemaVersion { get; }
    }

    public sealed class AW3CommandRequest
    {
        private const int MaximumJsonPayloadElements = 8192;

        private AW3CommandRequest(AW3CommandKind kind, long countryId,
            long targetCountryId, long actorId, long targetActorId,
            long cityId, long secondaryId, string key, string secondaryKey,
            string reasonKey, string text, string payload, bool boolValue,
            bool secondaryBoolValue, int intValue)
        {
            if (!Enum.IsDefined(typeof(AW3CommandKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (countryId <= 0)
                throw new ArgumentOutOfRangeException(nameof(countryId));
            Kind = kind;
            CountryId = countryId;
            TargetCountryId = targetCountryId;
            ActorId = actorId;
            TargetActorId = targetActorId;
            CityId = cityId;
            SecondaryId = secondaryId;
            Key = key ?? string.Empty;
            SecondaryKey = secondaryKey ?? string.Empty;
            ReasonKey = reasonKey ?? string.Empty;
            Text = text ?? string.Empty;
            Payload = payload ?? string.Empty;
            BoolValue = boolValue;
            SecondaryBoolValue = secondaryBoolValue;
            IntValue = intValue;
        }

        public AW3CommandKind Kind { get; }
        public long CountryId { get; }
        public long TargetCountryId { get; }
        public long ActorId { get; }
        public long TargetActorId { get; }
        public long CityId { get; }
        public long SecondaryId { get; }
        public string Key { get; }
        public string SecondaryKey { get; }
        public string ReasonKey { get; }
        public string Text { get; }
        public string Payload { get; }
        public bool BoolValue { get; }
        public bool SecondaryBoolValue { get; }
        public int IntValue { get; }
        public bool IsValid => true;

        public static AW3CommandRequest ConfigurePolicy(long countryId,
            bool enabled, bool aiEnabled) => Create(
            AW3CommandKind.ConfigurePolicy, countryId,
            boolValue: enabled, secondaryBoolValue: aiEnabled);

        public static AW3CommandRequest SetPolicyClass(long countryId,
            string classId) => Create(AW3CommandKind.SetPolicyClass,
            countryId, key: Token(classId, nameof(classId)));

        public static AW3CommandRequest StartPolicyNode(long countryId,
            string nodeId, bool force) => Create(
            AW3CommandKind.StartPolicyNode, countryId,
            key: Token(nodeId, nameof(nodeId)), boolValue: force);

        public static AW3CommandRequest TogglePolicyNodeLock(long countryId,
            string nodeId) => Create(AW3CommandKind.TogglePolicyNodeLock,
            countryId, key: Token(nodeId, nameof(nodeId)));

        public static AW3CommandRequest StartCoreFabrication(long countryId,
            long cityId, string projectType) => Create(
            AW3CommandKind.StartCoreFabrication, countryId,
            cityId: Positive(cityId, nameof(cityId)),
            key: Token(projectType, nameof(projectType)));

        public static AW3CommandRequest StartTargetedDecision(long countryId,
            long targetCountryId, string decisionId) => Create(
            AW3CommandKind.StartTargetedDecision, countryId,
            targetCountryId: Positive(targetCountryId,
                nameof(targetCountryId)),
            key: Token(decisionId, nameof(decisionId)));

        public static AW3CommandRequest StartMandateDecision(long countryId,
            string decisionId) => Create(
            AW3CommandKind.StartMandateDecision, countryId,
            key: Token(decisionId, nameof(decisionId)));

        public static AW3CommandRequest AppointCourtOfficer(long countryId,
            long actorId, string officeId,
            long expectedIncumbentActorId = -1L,
            string layer = "central", long cityId = -1L) => Create(
            AW3CommandKind.AppointCourtOfficer, countryId,
            actorId: Positive(actorId, nameof(actorId)),
            targetActorId: Optional(expectedIncumbentActorId,
                nameof(expectedIncumbentActorId)),
            cityId: Optional(cityId, nameof(cityId)),
            key: Token(officeId, nameof(officeId)),
            secondaryKey: Token(layer, nameof(layer)));

        public static AW3CommandRequest FillCentralCourtVacancies(
            long countryId) => Create(
            AW3CommandKind.FillCentralCourtVacancies, countryId);
        public static AW3CommandRequest SetCourtDisposition(long countryId,
            long actorId, string dispositionId, int intParameter,
            long cityId, string operationKey) =>
            Create(AW3CommandKind.SetCourtDisposition, countryId,
                actorId: Positive(actorId, nameof(actorId)),
                key: Token(dispositionId, nameof(dispositionId)),
                secondaryKey: Token(operationKey, nameof(operationKey)),
                cityId: Optional(cityId, nameof(cityId)),
                intValue: NonNegative(intParameter,
                    nameof(intParameter)));

        public static AW3CommandRequest ChangeCourtAuxiliaryLaw(
            long countryId, string lawId, int value) => Create(
            AW3CommandKind.ChangeCourtAuxiliaryLaw, countryId,
            key: Token(lawId, nameof(lawId)), intValue: value);

        public static AW3CommandRequest ChangeInheritanceLaw(long countryId,
            string lawId) => Create(AW3CommandKind.ChangeInheritanceLaw,
            countryId, key: Token(lawId, nameof(lawId)));

        public static AW3CommandRequest RelocateFeudatory(long countryId,
            long feudatoryId) => Create(AW3CommandKind.RelocateFeudatory,
            countryId, secondaryId: Positive(feudatoryId,
                nameof(feudatoryId)));

        public static AW3CommandRequest ReclaimFeudatoryCity(long countryId,
            long feudatoryId, long cityId) => Create(
            AW3CommandKind.ReclaimFeudatoryCity, countryId,
            cityId: Positive(cityId, nameof(cityId)),
            secondaryId: Positive(feudatoryId, nameof(feudatoryId)));

        public static AW3CommandRequest AbolishFeudatory(long countryId,
            long feudatoryId) => Create(AW3CommandKind.AbolishFeudatory,
            countryId, secondaryId: Positive(feudatoryId,
                nameof(feudatoryId)));

        public static AW3CommandRequest CreateDiplomacyProposal(
            long countryId, long targetCountryId, string proposalType,
            long actorId = -1L, long targetActorId = -1L,
            long selectionTargetCountryId = -1L,
            string detailId = "") => Create(
            AW3CommandKind.CreateDiplomacyProposal, countryId,
            targetCountryId: Positive(targetCountryId,
                nameof(targetCountryId)), actorId: Optional(actorId,
                nameof(actorId)), targetActorId: Optional(targetActorId,
                nameof(targetActorId)), secondaryId: Optional(
                selectionTargetCountryId,
                nameof(selectionTargetCountryId)),
            key: Token(proposalType, nameof(proposalType)),
            secondaryKey: string.IsNullOrWhiteSpace(detailId)
                ? ""
                : Token(detailId, nameof(detailId)));

        public static AW3CommandRequest CreateWarPeaceProposal(
            long countryId, long targetCountryId, string proposalType,
            long warId, string payload) => Create(
            AW3CommandKind.CreateDiplomacyProposal, countryId,
            targetCountryId: Positive(targetCountryId,
                nameof(targetCountryId)),
            secondaryId: Positive(warId, nameof(warId)),
            key: Token(proposalType, nameof(proposalType)),
            payload: JsonPayload(payload, nameof(payload)));

        public static AW3CommandRequest RespondDiplomacyProposal(
            long countryId, long targetCountryId, long proposalId,
            bool accept, long actorId = -1L) => Create(
            AW3CommandKind.RespondDiplomacyProposal,
            countryId, targetCountryId: Positive(targetCountryId,
                nameof(targetCountryId)), secondaryId: Positive(proposalId,
                nameof(proposalId)), actorId: Optional(actorId,
                nameof(actorId)), boolValue: accept);

        public static AW3CommandRequest StartSpyNetwork(long countryId,
            long targetCountryId) => Create(AW3CommandKind.StartSpyNetwork,
            countryId, targetCountryId: Positive(targetCountryId,
                nameof(targetCountryId)));

        public static AW3CommandRequest StartForgeDocuments(long countryId,
            long targetCountryId, long cityId, string projectType) => Create(
            AW3CommandKind.StartForgeDocuments, countryId,
            targetCountryId: Positive(targetCountryId,
                nameof(targetCountryId)), cityId: Positive(cityId,
                nameof(cityId)), key: Token(projectType,
                nameof(projectType)));

        public static AW3CommandRequest DeclareWar(long countryId,
            long targetCountryId, long cityId, string goalType,
            string warType, string reasonKey, string displayText) => Create(
            AW3CommandKind.DeclareWar, countryId,
            targetCountryId: Positive(targetCountryId,
                nameof(targetCountryId)),
            cityId: Optional(cityId, nameof(cityId)),
            key: Token(goalType, nameof(goalType)),
            secondaryKey: Token(warType, nameof(warType)),
            reasonKey: Token(reasonKey, nameof(reasonKey)),
            text: DisplayText(displayText, nameof(displayText)));

        public static AW3CommandRequest ConferPosthumousTitle(long countryId,
            long actorId, string titleId) => Create(
            AW3CommandKind.ConferPosthumousTitle, countryId,
            actorId: Positive(actorId, nameof(actorId)),
            key: Token(titleId, nameof(titleId)));

        public static AW3CommandRequest RenameClan(long countryId,
            long shiId, string clanName) => Create(AW3CommandKind.RenameClan,
            countryId, secondaryId: Positive(shiId, nameof(shiId)),
            text: DisplayText(clanName, nameof(clanName)));

        public static AW3CommandRequest RenameSurname(long countryId,
            long actorId, string familyName) => Create(
            AW3CommandKind.RenameSurname, countryId,
            actorId: Positive(actorId, nameof(actorId)),
            text: DisplayText(familyName, nameof(familyName)));

        public static AW3CommandRequest GrantVirtualNobleTitle(
            long countryId, long actorId, string titleText,
            bool hereditary = true) => Create(
            AW3CommandKind.GrantVirtualNobleTitle, countryId,
            actorId: Positive(actorId, nameof(actorId)),
            text: DisplayText(titleText, nameof(titleText)),
            boolValue: hereditary);

        public static AW3CommandRequest EditVirtualNobleTitle(
            long countryId, long titleId, string titleText,
            bool formalTitle = false) => Create(
            AW3CommandKind.EditVirtualNobleTitle, countryId,
            secondaryId: Positive(titleId, nameof(titleId)),
            text: DisplayText(titleText, nameof(titleText)),
            boolValue: formalTitle);

        public static AW3CommandRequest DeleteVirtualNobleTitle(
            long countryId, long titleId, bool formalTitle = false) => Create(
            AW3CommandKind.DeleteVirtualNobleTitle, countryId,
            secondaryId: Positive(titleId, nameof(titleId)),
            boolValue: formalTitle);

        public static AW3CommandRequest CreateMilitaryGovernorate(
            long countryId, long cityId, long actorId) => Create(
            AW3CommandKind.CreateMilitaryGovernorate, countryId,
            cityId: Positive(cityId, nameof(cityId)),
            actorId: Positive(actorId, nameof(actorId)));

        public static AW3CommandRequest DesignateMilitaryGovernorateSuccessor(
            long countryId, long subjectCountryId, long actorId) => Create(
            AW3CommandKind.DesignateMilitaryGovernorateSuccessor, countryId,
            targetCountryId: Positive(subjectCountryId,
                nameof(subjectCountryId)),
            actorId: Positive(actorId, nameof(actorId)));

        public static AW3CommandRequest ReplaceMilitaryGovernorateGovernor(
            long countryId, long subjectCountryId, long governorActorId) => Create(
            AW3CommandKind.ReplaceMilitaryGovernorateGovernor, countryId,
            targetCountryId: Positive(subjectCountryId,
                nameof(subjectCountryId)),
            actorId: Positive(governorActorId, nameof(governorActorId)));

        public static AW3CommandRequest ApplyCustomCourtTemplate(
            long countryId, string templateId, int templateRevision,
            string templateHash, long expectedInstanceRevision,
            string migrationMode = "preserve") => Create(
            AW3CommandKind.ApplyCustomCourtTemplate, countryId,
            secondaryId: Positive(expectedInstanceRevision,
                nameof(expectedInstanceRevision)),
            key: Token(templateId, nameof(templateId)),
            secondaryKey: Token(templateHash, nameof(templateHash)),
            reasonKey: Token(migrationMode, nameof(migrationMode)),
            intValue: NonNegative(templateRevision,
                nameof(templateRevision)));

        public static AW3CommandRequest GrantBanditAmnesty(
            long banditCountryId, long originCountryId, string rewardKind,
            string officeId, string titleText, bool hereditary) => Create(
            AW3CommandKind.GrantBanditAmnesty, banditCountryId,
            targetCountryId: Positive(originCountryId,
                nameof(originCountryId)),
            key: Token(rewardKind, nameof(rewardKind)),
            secondaryKey: officeId?.Trim() ?? string.Empty,
            text: titleText?.Trim() ?? string.Empty,
            boolValue: hereditary);

        public static AW3CommandRequest ChangeEra(long countryId,
            string eraName) => Create(AW3CommandKind.ChangeEra, countryId,
            text: DisplayText(eraName, nameof(eraName)));

        public static AW3CommandRequest SetArmyRallyPoint(long countryId,
            long armyId, long cityId) => Create(
            AW3CommandKind.SetArmyRallyPoint, countryId,
            cityId: Positive(cityId, nameof(cityId)),
            secondaryId: Positive(armyId, nameof(armyId)));

        public static AW3CommandRequest SetArmyTargetCity(long countryId,
            long armyId, long cityId) => Create(
            AW3CommandKind.SetArmyTargetCity, countryId,
            cityId: Positive(cityId, nameof(cityId)),
            secondaryId: Positive(armyId, nameof(armyId)));

        public static AW3CommandRequest SetArmyPosture(long countryId,
            long armyId, string postureId) => Create(
            AW3CommandKind.SetArmyPosture, countryId,
            secondaryId: Positive(armyId, nameof(armyId)),
            key: Token(postureId, nameof(postureId)));

        public static AW3CommandRequest CancelArmyOrder(long countryId,
            long armyId) => Create(AW3CommandKind.CancelArmyOrder,
            countryId, secondaryId: Positive(armyId, nameof(armyId)));

        public static AW3CommandRequest SubmitCivilServiceRanking(
            long countryId, long sessionId, long firstCandidateId,
            long secondCandidateId = -1L, long thirdCandidateId = -1L) =>
            Create(AW3CommandKind.SubmitCivilServiceRanking, countryId,
                actorId: Positive(firstCandidateId,
                    nameof(firstCandidateId)),
                targetActorId: Optional(secondCandidateId,
                    nameof(secondCandidateId)),
                cityId: Optional(thirdCandidateId,
                    nameof(thirdCandidateId)),
                secondaryId: Positive(sessionId, nameof(sessionId)));

        public static AW3CommandRequest CommitDomesticHousehold(
            long countryId, long actorId, long expectedRulerActorId,
            string kindId) => Create(
            AW3CommandKind.CommitDomesticHousehold, countryId,
            actorId: Positive(actorId, nameof(actorId)),
            targetActorId: Positive(expectedRulerActorId,
                nameof(expectedRulerActorId)),
            key: Token(kindId, nameof(kindId)));

        private static AW3CommandRequest Create(AW3CommandKind kind,
            long countryId, long targetCountryId = -1L,
            long actorId = -1L, long targetActorId = -1L,
            long cityId = -1L, long secondaryId = -1L,
            string key = "", string secondaryKey = "",
            string reasonKey = "", string text = "", string payload = "",
            bool boolValue = false, bool secondaryBoolValue = false,
            int intValue = 0) => new AW3CommandRequest(kind,
            Positive(countryId, nameof(countryId)), targetCountryId,
            actorId, targetActorId, cityId, secondaryId, key, secondaryKey,
            reasonKey, text, payload, boolValue, secondaryBoolValue,
            intValue);

        private static long Positive(long value, string parameter)
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(parameter);
            return value;
        }

        private static long Optional(long value, string parameter)
        {
            if (value != -1L && value <= 0)
                throw new ArgumentOutOfRangeException(parameter);
            return value;
        }

        private static int NonNegative(int value, string parameter)
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(parameter);
            return value;
        }

        private static string Token(string value, string parameter) =>
            AW3CatalogValidation.Token(value, parameter, 128);

        private static string DisplayText(string value, string parameter) =>
            AW3CatalogValidation.Display(value, parameter, 64);

        private static string JsonPayload(string value, string parameter) =>
            AW3CatalogValidation.Display(value, parameter,
                MaximumJsonPayloadElements);
    }

    public sealed class AW3CommandResult
    {
        public AW3CommandResult(AW3CommandStatus status,
            AW3CommandError error, string messageKey, long affectedId,
            int detailCode = -1)
        {
            if (!Enum.IsDefined(typeof(AW3CommandStatus), status))
                throw new ArgumentOutOfRangeException(nameof(status));
            if (!Enum.IsDefined(typeof(AW3CommandError), error))
                throw new ArgumentOutOfRangeException(nameof(error));
            if ((status == AW3CommandStatus.Accepted ||
                 status == AW3CommandStatus.Pending) !=
                (error == AW3CommandError.None))
                throw new ArgumentException(
                    "Command status and error must agree.");
            if (affectedId != -1L && affectedId <= 0)
                throw new ArgumentOutOfRangeException(nameof(affectedId));
            if (detailCode < -1)
                throw new ArgumentOutOfRangeException(nameof(detailCode));
            Status = status;
            Error = error;
            MessageKey = string.IsNullOrWhiteSpace(messageKey)
                ? string.Empty
                : AW3CatalogValidation.Token(messageKey,
                    nameof(messageKey), 128);
            AffectedId = affectedId;
            DetailCode = detailCode;
        }

        public AW3CommandStatus Status { get; }
        public AW3CommandError Error { get; }
        public string MessageKey { get; }
        public long AffectedId { get; }
        public int DetailCode { get; }
        public bool Accepted => Status == AW3CommandStatus.Accepted;

        public static AW3CommandResult Success(string messageKey = "",
            long affectedId = -1L, int detailCode = -1) =>
            new AW3CommandResult(
            AW3CommandStatus.Accepted, AW3CommandError.None, messageKey,
            affectedId, detailCode);

        public static AW3CommandResult Rejected(AW3CommandError error,
            string messageKey, long affectedId = -1L,
            int detailCode = -1)
        {
            if (error == AW3CommandError.None)
                throw new ArgumentOutOfRangeException(nameof(error));
            return new AW3CommandResult(AW3CommandStatus.Rejected, error,
                messageKey, affectedId, detailCode);
        }

        public static AW3CommandResult Pending(string messageKey) =>
            new AW3CommandResult(AW3CommandStatus.Pending,
                AW3CommandError.None, messageKey, -1L);
    }

    internal static class AW3CatalogValidation
    {
        private static readonly Encoding Utf8 =
            new UTF8Encoding(false, true);

        internal static string Token(string value, string parameter,
            int maximumBytes)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Token is required.", parameter);
            string normalized = value.Trim();
            if (Utf8.GetByteCount(normalized) > maximumBytes)
                throw new ArgumentException("Token is too long.", parameter);
            for (var index = 0; index < normalized.Length; index++)
            {
                char character = normalized[index];
                if (!(char.IsLetterOrDigit(character) || character == '_' ||
                      character == '-' || character == '.'))
                    throw new ArgumentException("Token is invalid.",
                        parameter);
            }
            return normalized;
        }

        internal static string Display(string value, string parameter,
            int maximumElements)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Text is required.", parameter);
            string normalized = value.Trim();
            if (StringInfo.ParseCombiningCharacters(normalized).Length >
                    maximumElements ||
                Utf8.GetByteCount(normalized) > maximumElements * 4)
                throw new ArgumentException("Text is too long.", parameter);
            for (var index = 0; index < normalized.Length; index++)
                if (char.IsControl(normalized[index]))
                    throw new ArgumentException("Text is invalid.",
                        parameter);
            return normalized;
        }

        internal static string Path(string value, string parameter)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Path is required.", parameter);
            string normalized = value.Trim().Replace('\\', '/');
            if (normalized.Length > 256 || normalized.Contains(".."))
                throw new ArgumentException("Path is invalid.", parameter);
            return normalized;
        }
    }
}
