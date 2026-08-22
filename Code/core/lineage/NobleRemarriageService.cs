using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.db;
using AncientWarfare3.core.schools;

namespace AncientWarfare3.core.lineage
{
    internal static class NobleRemarriageService
    {
        private const int MaximumTitledSubjects = 16;
        private const int MinimumCandidateAgeYears = 16;
        private const int WorldTimePerYear = 60;

        private static SQLiteConnection DB =>
            LineageArchiveManager.Instance?.OperatingDB;
        private static bool Ready => DB != null &&
                                     LineageArchiveManager.Instance
                                         .InitializeSuccessful;

        public static void MarkDirty(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return;
            pKingdom.data.set(LineageKeys.NOBLE_REMARRIAGE_DIRTY, true);
        }

        public static void MarkDirtyForPartnerDeath(Actor pDyingActor)
        {
            Actor partner = pDyingActor?.lover;
            if (partner?.data == null || !partner.isAlive() ||
                partner.isRekt()) return;
            MarkDirty(partner.kingdom);
        }

        public static void OnKingdomYear(Kingdom pKingdom)
        {
            if (!Ready || pKingdom?.data == null || pKingdom.isRekt())
                return;

            List<NobleRemarriageSubjectCandidate> candidates =
                BuildSubjects(pKingdom);
            IReadOnlyList<long> selected = NobleRemarriageRules
                .SelectSubjects(candidates,
                    NobleRemarriageRules.MaximumSubjectsPerKingdomYear);
            bool unresolved = candidates.Count > selected.Count;
            for (int i = 0; i < selected.Count; i++)
            {
                Actor subject = FindActor(selected[i]);
                if (!TryRemarry(pKingdom, subject)) unresolved = true;
            }
            pKingdom.data.set(LineageKeys.NOBLE_REMARRIAGE_DIRTY,
                unresolved);
        }

        private static List<NobleRemarriageSubjectCandidate> BuildSubjects(
            Kingdom pKingdom)
        {
            var result = new List<NobleRemarriageSubjectCandidate>(12);
            var seen = new HashSet<long>();
            AddSubject(result, seen, pKingdom.king, pKingdom,
                NobleRemarriagePriority.Ruler);
            AddSubject(result, seen, HeirService.GetHeir(pKingdom),
                pKingdom, NobleRemarriagePriority.Heir);
            IReadOnlyList<FeudatorySnapshot> feudatories =
                FeudatoryService.GetByKingdom(pKingdom.id);
            for (int i = 0; i < feudatories.Count; i++)
                AddSubject(result, seen,
                    FindActor(feudatories[i].PrinceActorId), pKingdom,
                    NobleRemarriagePriority.FeudatoryPrince);
            foreach (long actorId in ReadTitledActorIds(pKingdom.id))
                AddSubject(result, seen, FindActor(actorId), pKingdom,
                    NobleRemarriagePriority.TitledNoble);
            return result;
        }

        private static void AddSubject(
            List<NobleRemarriageSubjectCandidate> pResult,
            HashSet<long> pSeen, Actor pActor, Kingdom pKingdom,
            NobleRemarriagePriority pPriority)
        {
            if (pActor?.data == null || !pSeen.Add(pActor.data.id) ||
                pActor.kingdom != pKingdom) return;
            Actor partner = pActor.lover;
            bool partnerReference = partner != null;
            bool partnerAlive = partner?.data != null &&
                                partner.isAlive() && !partner.isRekt();
            bool eligible = NobleRemarriageRules.NeedsRemarriage(
                pActor.isAlive() && !pActor.isRekt(), pActor.isAdult(),
                pActor.isBreedingAge(), partnerReference, partnerAlive);
            if (!eligible) return;
            pResult.Add(new NobleRemarriageSubjectCandidate(
                pActor.data.id, pPriority, true,
                pActor.data.created_time));
        }

        private static IReadOnlyList<long> ReadTitledActorIds(
            long pKingdomId)
        {
            var result = new List<long>(MaximumTitledSubjects);
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT ACTOR_ID FROM " +
                    EnfeoffmentTableItem.GetTableName() +
                    " INDEXED BY idx_Enfeoffment_kingdom_active " +
                    "WHERE KINGDOM_ID=@kingdom AND ACTIVE=1 " +
                    "ORDER BY NOBLE_RANK DESC,ACTOR_ID LIMIT @limit";
                command.Parameters.AddWithValue("@kingdom", pKingdomId);
                command.Parameters.AddWithValue("@limit",
                    MaximumTitledSubjects);
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read()) result.Add(reader.GetInt64(0));
            }
            catch { }
            return result;
        }

        private static bool TryRemarry(Kingdom pKingdom, Actor pSubject)
        {
            if (!IsSubjectStillEligible(pKingdom, pSubject)) return false;
            City targetCity = pSubject.city;
            if (targetCity?.data == null || targetCity.isRekt() ||
                targetCity.kingdom != pKingdom) return false;
            long spouseId = SelectSpouse(pKingdom, pSubject);
            Actor spouse = FindActor(spouseId);
            if (!IsSpouseStillEligible(pKingdom, pSubject, spouse))
                return false;

            City previousCity = spouse.city;
            try
            {
                ClearDeadPartner(pSubject);
                ClearDeadPartner(spouse);
                if (spouse.city != targetCity)
                {
                    using (FormalAffiliationTransferScope.Open(
                               spouse.data.id, pKingdom.id, targetCity.id))
                        spouse.joinCity(targetCity);
                    if (spouse.city != targetCity) return false;
                }
                pSubject.becomeLoversWith(spouse);
                if (pSubject.lover != spouse || spouse.lover != pSubject)
                {
                    RestoreCity(spouse, previousCity, pKingdom);
                    return false;
                }
                DynasticMaleLineContinuityService.RequestContinuation(
                    pSubject.isSexMale() ? pSubject : spouse);
                LineageService.ArchiveActor(pSubject, pAlive: true);
                LineageService.ArchiveActor(spouse, pAlive: true);
                ChronicleEvents.OnNobleRemarried(pKingdom, pSubject,
                    spouse);
                return true;
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Noble remarriage failed: " +
                                    exception.Message);
                RestoreCity(spouse, previousCity, pKingdom);
                return false;
            }
        }

        private static long SelectSpouse(Kingdom pKingdom, Actor pSubject)
        {
            var candidates = new List<NobleRemarriageSpouseCandidate>(
                NobleRemarriageRules.MaximumSpouseCandidates);
            foreach (long actorId in ReadSpouseCandidateIds(pKingdom.id,
                         pSubject.isSexMale() ? 1 : 0))
            {
                Actor actor = FindActor(actorId);
                bool eligible = IsSpouseStillEligible(pKingdom, pSubject,
                    actor);
                int ageDifference = eligible
                    ? Math.Abs(pSubject.getAge() - actor.getAge())
                    : int.MaxValue;
                int merit = eligible
                    ? Math.Max(0, actor.diplomacy) +
                      Math.Max(0, actor.intelligence) +
                      Math.Max(0, actor.stewardship) +
                      Math.Max(0, actor.warfare)
                    : 0;
                candidates.Add(new NobleRemarriageSpouseCandidate(actorId,
                    eligible, eligible && actor.city == pSubject.city,
                    ageDifference, merit));
            }
            return NobleRemarriageRules.SelectSpouse(candidates);
        }

        private static IReadOnlyList<long> ReadSpouseCandidateIds(
            long pKingdomId, int pSex)
        {
            var result = new List<long>(
                NobleRemarriageRules.MaximumSpouseCandidates);
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT ID FROM " +
                    ActorArchiveTableItem.GetTableName() +
                    " INDEXED BY idx_ActorArchive_kingdom_alive_birth " +
                    "WHERE KINGDOM_ID=@kingdom AND IS_ALIVE=1 " +
                    "AND BIRTH_TIME<=@cutoff AND SEX=@sex " +
                    "ORDER BY BIRTH_TIME DESC,ID LIMIT @limit";
                command.Parameters.AddWithValue("@kingdom", pKingdomId);
                command.Parameters.AddWithValue("@cutoff",
                    LineageService.CurTime() -
                    MinimumCandidateAgeYears * WorldTimePerYear);
                command.Parameters.AddWithValue("@sex", pSex);
                command.Parameters.AddWithValue("@limit",
                    NobleRemarriageRules.MaximumSpouseCandidates);
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read()) result.Add(reader.GetInt64(0));
            }
            catch { }
            return result;
        }

        private static bool IsSubjectStillEligible(Kingdom pKingdom,
            Actor pSubject)
        {
            if (pSubject?.data == null || pSubject.kingdom != pKingdom)
                return false;
            Actor partner = pSubject.lover;
            return NobleRemarriageRules.NeedsRemarriage(
                pSubject.isAlive() && !pSubject.isRekt(),
                pSubject.isAdult(), pSubject.isBreedingAge(),
                partner != null, partner?.data != null &&
                                 partner.isAlive() && !partner.isRekt());
        }

        private static bool IsSpouseStillEligible(Kingdom pKingdom,
            Actor pSubject, Actor pCandidate)
        {
            if (pSubject?.data == null || pCandidate?.data == null ||
                pCandidate == pSubject ||
                pCandidate.isSexMale() == pSubject.isSexMale()) return false;
            Actor partner = pCandidate.lover;
            bool hasLivingPartner = partner?.data != null &&
                                    partner.isAlive() && !partner.isRekt();
            bool related = SafeRelated(pSubject, pCandidate);
            long subjectShi = LineageQuery.GetActorShiId(pSubject.data.id);
            long candidateShi = LineageQuery.GetActorShiId(pCandidate.data.id);
            bool sameShi = subjectShi >= 0 && subjectShi == candidateShi;
            return NobleRemarriageRules.CanUseSpouse(
                pCandidate.isAlive() && !pCandidate.isRekt(),
                pCandidate.isAdult(), pCandidate.isBreedingAge(),
                hasLivingPartner, related, sameShi,
                pCandidate.kingdom != pKingdom);
        }

        private static bool SafeRelated(Actor pFirst, Actor pSecond)
        {
            try
            {
                return pFirst.isRelatedTo(pSecond) ||
                       pSecond.isRelatedTo(pFirst);
            }
            catch { return true; }
        }

        private static void ClearDeadPartner(Actor pActor)
        {
            Actor partner = pActor?.lover;
            if (pActor?.data == null || partner == null ||
                partner?.data != null && partner.isAlive() &&
                !partner.isRekt()) return;
            pActor.setLover(null);
            if (partner?.lover == pActor) partner.setLover(null);
        }

        private static void RestoreCity(Actor pActor, City pCity,
            Kingdom pKingdom)
        {
            if (pActor?.data == null || pCity?.data == null ||
                pCity.isRekt() || pCity.kingdom != pKingdom ||
                pActor.city == pCity) return;
            try
            {
                using (FormalAffiliationTransferScope.Open(pActor.data.id,
                           pKingdom.id, pCity.id))
                    pActor.joinCity(pCity);
            }
            catch { }
        }

        private static Actor FindActor(long pActorId)
        {
            if (pActorId < 0) return null;
            try { return World.world?.units?.get(pActorId); }
            catch { return null; }
        }
    }
}
