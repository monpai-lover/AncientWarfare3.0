using System;

namespace AncientWarfare3.core.lineage
{
    internal static class DynasticTitleService
    {
        public static string ResolveLivingTitle(long pActorId)
        {
            if (pActorId < 0) return "";
            Actor actor;
            try { actor = World.world?.units?.get(pActorId); }
            catch { actor = null; }
            return ResolveLivingTitle(actor);
        }

        public static string ResolveLivingTitle(Actor pActor)
        {
            if (pActor?.data == null) return "";
            string formalTitle = NobleRankService.GetDisplayTitle(pActor);
            pActor.data.get(LineageKeys.HISTORICAL_DYNASTIC_TITLE,
                out string historicalTitle, "");

            bool activePrince = FeudatoryService.TryGetByPrince(
                pActor.data.id, out FeudatorySnapshot princeSnapshot) &&
                princeSnapshot.PrinceActorId == pActor.data.id;
            bool successor = FeudatoryService.TryGetBySuccessor(
                pActor.data.id, out FeudatorySnapshot successorSnapshot) &&
                successorSnapshot.SuccessorActorId == pActor.data.id;
            FeudatorySnapshot context = activePrince
                ? princeSnapshot
                : successor
                    ? successorSnapshot
                    : ResolvePrinceChildContext(pActor);
            bool princeChild = !activePrince && context != null &&
                               IsRecordedPrinceChild(pActor, context);

            pActor.data.get(LineageKeys.ROYAL_CHILD,
                out bool royalChild, false);
            if (!royalChild)
                royalChild = HasCurrentEmperorParent(pActor);
            return DynasticTitleRules.Resolve(formalTitle,
                context?.FeudatoryName ?? "", activePrince, successor,
                princeChild, royalChild, pActor.isSexMale(),
                pActor.isAdult(), historicalTitle);
        }

        public static void OnChildBorn(Actor pChild, Actor pParent1,
            Actor pParent2)
        {
            if (pChild?.data == null) return;
            ActorAgeWorkService.MarkDirty(pChild);
            ActorAgeWorkService.MarkDirty(pParent1);
            ActorAgeWorkService.MarkDirty(pParent2);
            DynasticMaleLineContinuityService.OnChildBorn(pChild,
                pParent1, pParent2);
            FeudatoryService.OnChildBorn(pChild, pParent1, pParent2);
            Actor emperor = IsCurrentMandateEmperor(pParent1)
                ? pParent1
                : IsCurrentMandateEmperor(pParent2)
                    ? pParent2
                    : null;
            if (emperor?.data != null)
            {
                pChild.data.set(LineageKeys.ROYAL_CHILD, true);
                pChild.data.set(LineageKeys.ROYAL_PARENT_ACTOR_ID,
                    emperor.data.id);
                pChild.data.set(LineageKeys.ROYAL_PARENT_KINGDOM_ID,
                    emperor.kingdom?.id ?? -1L);
                pChild.data.set(LineageKeys.ROYAL_ADULT_TITLE_PROCESSED,
                    false);
            }
            pChild.data.set(LineageKeys.FEUDATORY_ADULT_REFRESHED, false);
            if (pChild.isAdult()) OnAgeUpdated(pChild);
            try { LineageService.ArchiveActor(pChild, pAlive: true); }
            catch { }
        }

        public static void OnAgeUpdated(Actor pActor)
        {
            if (pActor?.data == null || pActor.isRekt() ||
                !pActor.isAlive())
                return;
            FeudatoryService.OnActorAdult(pActor);
            if (!pActor.isAdult()) return;
            pActor.data.get(LineageKeys.ROYAL_CHILD,
                out bool royalChild, false);
            pActor.data.get(LineageKeys.ROYAL_ADULT_TITLE_PROCESSED,
                out bool processed, false);
            if (!royalChild)
            {
                Actor emperor = FindCurrentEmperorParent(pActor);
                if (emperor?.data != null)
                {
                    royalChild = true;
                    pActor.data.set(LineageKeys.ROYAL_CHILD, true);
                    pActor.data.set(LineageKeys.ROYAL_PARENT_ACTOR_ID,
                        emperor.data.id);
                    pActor.data.set(LineageKeys.ROYAL_PARENT_KINGDOM_ID,
                        emperor.kingdom?.id ?? -1L);
                }
                else if (DynasticTitleRules
                    .ShouldMarkUnresolvedAdultRoyalProbeAsProcessed(
                        adult: true, royalChild: false, processed: processed,
                        foundCurrentEmperorParent: false))
                {
                    pActor.data.set(
                        LineageKeys.ROYAL_ADULT_TITLE_PROCESSED, true);
                    return;
                }
            }
            if (!royalChild || processed) return;

            NobleTitleSnapshot current = NobleRankService.ReadHot(pActor);
            if (current.IsActive)
            {
                pActor.data.set(LineageKeys.ROYAL_ADULT_TITLE_PROCESSED,
                    true);
                return;
            }
            pActor.data.get(LineageKeys.ROYAL_PARENT_KINGDOM_ID,
                out long kingdomId, -1L);
            pActor.data.get(LineageKeys.ROYAL_PARENT_ACTOR_ID,
                out long parentId, -1L);
            Kingdom kingdom = FindKingdom(kingdomId);
            Actor grantor = FindActor(parentId) ?? kingdom?.king;
            if (kingdom?.data == null ||
                !NobleRankService.TryGrantAdultRoyalChildTitle(kingdom,
                    grantor, pActor))
                return;
            pActor.data.set(LineageKeys.ROYAL_ADULT_TITLE_PROCESSED, true);
            try { LineageService.ArchiveActor(pActor, pAlive: true); }
            catch { }
        }

        public static void OnActorDying(Actor pActor)
        {
            if (pActor?.data == null) return;
            string title = ResolveLivingTitle(pActor);
            if (!string.IsNullOrWhiteSpace(title))
                pActor.data.set(LineageKeys.HISTORICAL_DYNASTIC_TITLE,
                    title);
            FeudatoryService.OnActorDying(pActor);
        }

        private static FeudatorySnapshot ResolvePrinceChildContext(
            Actor pActor)
        {
            pActor.data.get(LineageKeys.FEUDATORY_LINE_ID,
                out long feudatoryId, -1L);
            if (feudatoryId >= 0 &&
                FeudatoryService.TryGet(feudatoryId,
                    out FeudatorySnapshot recorded))
                return recorded;
            Actor parent1 = FindActor(pActor.data.parent_id_1);
            if (FeudatoryService.TryGetByPrince(parent1?.data?.id ?? -1L,
                    out FeudatorySnapshot first))
                return first;
            Actor parent2 = FindActor(pActor.data.parent_id_2);
            return FeudatoryService.TryGetByPrince(
                parent2?.data?.id ?? -1L, out FeudatorySnapshot second)
                ? second
                : null;
        }

        private static bool IsRecordedPrinceChild(Actor pActor,
            FeudatorySnapshot pSnapshot)
        {
            if (pActor?.data == null || pSnapshot == null) return false;
            pActor.data.get(LineageKeys.FEUDATORY_PARENT_ACTOR_ID,
                out long recordedParentId, -1L);
            if (recordedParentId >= 0)
                return pActor.data.parent_id_1 == recordedParentId ||
                       pActor.data.parent_id_2 == recordedParentId;
            long princeId = pSnapshot.PrinceActorId;
            return pActor.data.parent_id_1 == princeId ||
                   pActor.data.parent_id_2 == princeId;
        }

        private static bool HasCurrentEmperorParent(Actor pActor)
        {
            return FindCurrentEmperorParent(pActor) != null;
        }

        private static Actor FindCurrentEmperorParent(Actor pActor)
        {
            Actor parent = FindActor(pActor?.data?.parent_id_1 ?? -1L);
            if (IsCurrentMandateEmperor(parent)) return parent;
            parent = FindActor(pActor?.data?.parent_id_2 ?? -1L);
            return IsCurrentMandateEmperor(parent) ? parent : null;
        }

        private static bool IsCurrentMandateEmperor(Actor pActor)
        {
            return pActor?.data != null && pActor.isKing() &&
                   pActor.kingdom?.king == pActor &&
                   MandateService.GetCurrentMandateKingdom() ==
                   pActor.kingdom;
        }

        private static Actor FindActor(long pActorId)
        {
            if (pActorId < 0) return null;
            try { return World.world?.units?.get(pActorId); }
            catch { return null; }
        }

        private static Kingdom FindKingdom(long pKingdomId)
        {
            if (pKingdomId < 0) return null;
            try { return World.world?.kingdoms?.get(pKingdomId); }
            catch { return null; }
        }
    }
}
