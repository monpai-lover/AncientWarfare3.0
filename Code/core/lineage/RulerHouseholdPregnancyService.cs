using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.db;
using AncientWarfare3.core.performance;
using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.lineage
{
    internal static class RulerHouseholdPregnancyService
    {
        private const int KingdomsPerAuthorityCycle = 1;
        private static readonly MonthlyAuthorityWorkQueue<Kingdom>
            MonthlyWork = new MonthlyAuthorityWorkQueue<Kingdom>();
        private static readonly Dictionary<long, long> OwnerCursors =
            new Dictionary<long, long>();

        internal static int PendingMonthlyWorkForDiagnostics =>
            MonthlyWork.PendingCount;

        private static SQLiteConnection DB =>
            LineageArchiveManager.Instance?.OperatingDB;
        private static bool Ready => DB != null &&
                                     LineageArchiveManager.Instance
                                         .InitializeSuccessful;

        internal static Actor ResolveManagedFather(Actor pMother,
            out RulerHouseholdConceptionKind pKind)
        {
            pKind = RulerHouseholdConceptionKind.None;
            Actor spouse = pMother?.lover;
            bool mutual = IsLiveActor(spouse) && spouse.lover == pMother;
            if (mutual)
            {
                pKind = RulerHouseholdConceptionKind.PrincipalWife;
                return spouse;
            }
            if (!Ready || pMother?.data == null) return null;
            try
            {
                var query = new RulerHouseholdQuery(DB);
                if (!query.TryReadActiveByPartner(pMother.data.id,
                        out RulerHouseholdRecord row) ||
                    row.Kind != RulerHouseholdKind.Consort)
                    return null;
                Actor owner = FindActor(row.RulerActorId);
                if (!IsLiveActor(owner) || owner.kingdom?.data == null ||
                    pMother.kingdom != owner.kingdom ||
                    owner.kingdom.id != row.RecipientKingdomId)
                    return null;
                pKind = RulerHouseholdConceptionKind.Consort;
                return owner;
            }
            catch
            {
                return null;
            }
        }

        internal static void RecordConception(Actor pMother, Actor pFather,
            RulerHouseholdConceptionKind pKind)
        {
            if (pMother?.data == null || pFather?.data == null ||
                pKind == RulerHouseholdConceptionKind.None)
                return;
            pMother.data.set(LineageKeys.DYNASTIC_PREGNANCY_FATHER_ID,
                pFather.data.id);
            pMother.data.set(LineageKeys.DYNASTIC_PREGNANCY_KIND,
                RulerHouseholdPregnancyRules.KindId(pKind));
        }

        internal static bool TryDeliverConsortPregnancy(Actor pMother)
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession ||
                pMother?.data == null ||
                !TryReadStoredConception(pMother, out Actor father,
                    out RulerHouseholdConceptionKind kind) ||
                kind != RulerHouseholdConceptionKind.Consort)
                return false;

            pMother.birthEvent();
            BabyMaker.makeBaby(pMother, father, ActorSex.None,
                pCloneTraits: false, 0, null, pAddToFamily: true);
            float chance = .5f;
            int additionalBirthRolls = Math.Max(0,
                (int)pMother.stats["birth_rate"]);
            for (int index = 0; index < additionalBirthRolls; index++)
            {
                if (!Randy.randomChance(chance)) break;
                BabyMaker.makeBaby(pMother, father, ActorSex.None,
                    pCloneTraits: false, 0, null, pAddToFamily: true);
                chance *= .85f;
            }
            return true;
        }

        internal static void ApplyBirthLegitimacy(Actor pBaby,
            Actor pParent1, Actor pParent2)
        {
            if (pBaby?.data == null) return;
            Actor mother = pParent1?.isSexFemale() == true
                ? pParent1
                : pParent2?.isSexFemale() == true
                    ? pParent2
                    : null;
            Actor father = mother == pParent1 ? pParent2 : pParent1;
            bool legitimate = true;
            if (mother?.data != null && father?.data != null &&
                TryReadStoredConception(mother, out Actor storedFather,
                    out RulerHouseholdConceptionKind kind) &&
                storedFather?.data?.id == father.data.id)
                legitimate = RulerHouseholdPregnancyRules
                    .IsLegitimateBirth(kind);
            pBaby.data.set(LineageKeys.BIRTH_LEGITIMACY, legitimate);
        }

        internal static void ClearConception(Actor pMother)
        {
            if (pMother?.data == null) return;
            pMother.data.removeLong(
                LineageKeys.DYNASTIC_PREGNANCY_FATHER_ID);
            pMother.data.removeString(LineageKeys.DYNASTIC_PREGNANCY_KIND);
        }

        public static void Reset()
        {
            MonthlyWork.Clear();
            OwnerCursors.Clear();
        }

        public static void ProcessAuthorityCycle()
        {
            if (!IsAuthority() || !Ready || World.world?.kingdoms == null)
                return;
            int monthKey = RulerHouseholdPregnancyRules.ToMonthKey(
                Date.getCurrentYear(), Date.getCurrentMonth());
            MonthlyWork.ScheduleMonth(monthKey,
                MonthlyKingdomSnapshotService.Get(monthKey));
            MonthlyWork.Drain(KingdomsPerAuthorityCycle,
                (queuedMonthKey, kingdom) =>
            {
                long benchmark = RecentFeatureBenchmark.Begin();
                try { ProcessKingdomMonth(kingdom, queuedMonthKey); }
                catch { }
                finally
                {
                    RecentFeatureBenchmark.End(
                        RecentFeatureBenchmarkRules.MonthRulerHouseholdIndex,
                        benchmark);
                }
            });
        }

        private static void ProcessKingdomMonth(Kingdom pKingdom,
            int pMonthKey)
        {
            if (!IsAuthority() || !Ready || pKingdom?.data == null ||
                pKingdom.isRekt())
                return;
            var query = new RulerHouseholdQuery(DB);
            OwnerCursors.TryGetValue(pKingdom.id, out long ownerCursor);
            IReadOnlyList<long> ownerIds =
                query.ReadActiveOwnerIdsByRecipient(pKingdom.id,
                    ownerCursor, 8);
            if (ownerIds.Count == 0 && ownerCursor >= 0L)
                ownerIds = query.ReadActiveOwnerIdsByRecipient(pKingdom.id,
                    -1L, 8);
            if (ownerIds.Count == 0) return;
            int ownerIndex = 0;
            Actor ruler = FindActor(ownerIds[ownerIndex]);
            OwnerCursors[pKingdom.id] = ownerIds[ownerIndex];
            if (!IsLiveActor(ruler) || ruler.kingdom != pKingdom) return;
            IReadOnlyList<RulerHouseholdRecord> rows =
                query.ReadActiveByOwner(ruler.data.id, 9);
            if (rows.Count == 0) return;
            int started = 0;
            int startIndex = RulerHouseholdPregnancyRules
                .RotatingCandidateIndex(pMonthKey, ruler.data.id,
                    rows.Count);
            int startLimit = RulerHouseholdPregnancyRules
                .PregnancyStartsForMonth(rows.Count);
            for (int offset = 0; offset < rows.Count &&
                 started < startLimit; offset++)
            {
                int index = (startIndex + offset) % rows.Count;
                RulerHouseholdRecord row = rows[index];
                if (row.Kind != RulerHouseholdKind.Consort) continue;
                Actor mother = FindActor(row.PartnerActorId);
                if (!CanStartPregnancy(mother, ruler, pKingdom)) continue;
                BabyHelper.babyMakingStart(mother);
                if (!mother.addStatusEffect("pregnant",
                        NobleHeirPregnancyRules.TenMonthPregnancySeconds))
                    continue;
                mother.subspecies.counterReproduction();
                started++;
            }
        }

        private static bool TryReadStoredConception(Actor pMother,
            out Actor pFather, out RulerHouseholdConceptionKind pKind)
        {
            pFather = null;
            pKind = RulerHouseholdConceptionKind.None;
            if (pMother?.data == null) return false;
            pMother.data.get(LineageKeys.DYNASTIC_PREGNANCY_FATHER_ID,
                out long fatherId, -1L);
            pMother.data.get(LineageKeys.DYNASTIC_PREGNANCY_KIND,
                out string kindId, "");
            pKind = RulerHouseholdPregnancyRules.ParseKind(kindId);
            pFather = FindActor(fatherId);
            return pKind != RulerHouseholdConceptionKind.None &&
                   pFather?.data != null;
        }

        private static bool CanStartPregnancy(Actor pMother, Actor pFather,
            Kingdom pKingdom)
        {
            if (!IsLiveActor(pMother) || !IsLiveActor(pFather) ||
                pMother.kingdom != pKingdom ||
                pFather.kingdom != pKingdom ||
                !pMother.isSexFemale() || !pFather.isSexMale() ||
                !pMother.isAdult() || !pFather.isAdult() ||
                !pMother.isBreedingAge() || !pFather.isBreedingAge() ||
                pMother.hasStatus("pregnant") || pMother.isFighting() ||
                pFather.isFighting() || !pFather.canBreed() ||
                !pFather.canProduceBabies())
                return false;
            if (!FamilyExpansionService.NeedsExpansion(pMother, pFather))
                return false;
            City city = pMother.city;
            if (city?.data == null || city.isRekt() ||
                city.kingdom != pKingdom || city.isInDanger())
                return false;
            return BabyHelper.canMakeBabies(pMother) &&
                   !BabyHelper.isMetaLimitsReached(pMother) &&
                   WorldLawLibrary.world_law_civ_babies.isEnabled();
        }

        private static bool IsLiveActor(Actor pActor)
        {
            return pActor?.data != null && pActor.isAlive() &&
                   !pActor.isRekt();
        }

        private static Actor FindActor(long pActorId)
        {
            if (pActorId < 0L) return null;
            try { return World.world?.units?.get(pActorId); }
            catch { return null; }
        }

        private static bool IsAuthority()
        {
            return !AW3MultiplayerReplicaScope.IsApplying &&
                   !AW3MultiplayerReplicaScope.IsReplicaSession;
        }
    }
}
