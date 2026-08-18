using System;
using System.Collections.Generic;
using System.Linq;
#if !AW3_RULES_TESTS
using AncientWarfare3.core.lineage;
#endif

namespace AncientWarfare3.api.multiplayer
{
    public static class AW3MultiplayerSuccessionFacade
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<long, SuccessionHold> Holds =
            new Dictionary<long, SuccessionHold>();
        private static IAW3MultiplayerSuccessionProvider _current;
        private static long _ownershipRevision;

        public static IAW3MultiplayerSuccessionProvider Current
        {
            get
            {
                lock (Gate) return _current;
            }
        }

        public static bool Register(
            IAW3MultiplayerSuccessionProvider provider)
        {
            if (provider == null) return false;
            lock (Gate)
            {
                if (ReferenceEquals(_current, provider)) return true;
                if (_current != null) return false;
                _current = provider;
                _ownershipRevision++;
                return true;
            }
        }

        public static bool Unregister(
            IAW3MultiplayerSuccessionProvider provider)
        {
            if (provider == null) return false;
            SuccessionHold[] released;
            lock (Gate)
            {
                if (!ReferenceEquals(_current, provider)) return false;
                _current = null;
                _ownershipRevision++;
                released = Holds.Values
                    .OrderBy(hold => hold.Offer.CountryId)
                    .ToArray();
                Holds.Clear();
            }
            for (var index = 0; index < released.Length; index++)
                NotifyReleased(provider, released[index].Offer);
            return true;
        }

        public static bool TryBegin(AW3SuccessionOffer offer)
        {
            if (offer == null) return false;
            IAW3MultiplayerSuccessionProvider provider;
            long revision;
            lock (Gate)
            {
                if (Holds.TryGetValue(offer.CountryId,
                        out SuccessionHold existing))
                    return existing.Matches(offer);
                provider = _current;
                revision = _ownershipRevision;
            }
            if (provider == null) return false;

            bool accepted;
            try { accepted = provider.TryBegin(offer); }
            catch { accepted = false; }
            if (!accepted) return false;

            bool retained = false;
            lock (Gate)
            {
                if (ReferenceEquals(_current, provider) &&
                    _ownershipRevision == revision &&
                    !Holds.ContainsKey(offer.CountryId))
                {
                    Holds.Add(offer.CountryId,
                        new SuccessionHold(provider, offer));
                    retained = true;
                }
            }
            if (!retained) NotifyReleased(provider, offer);
            return retained;
        }

        public static bool NotifyInstalled(long countryId,
            long formerRulerActorId, long installedRulerActorId)
        {
            if (countryId <= 0 || formerRulerActorId <= 0 ||
                installedRulerActorId <= 0) return false;
            IAW3MultiplayerSuccessionProvider provider;
            SuccessionHold hold;
            lock (Gate)
            {
                if (!Holds.TryGetValue(countryId, out hold) ||
                    hold.Offer.FormerRulerActorId != formerRulerActorId)
                    return false;
                if (hold.Installing)
                {
                    hold.InstalledActorId = installedRulerActorId;
                    return true;
                }
                Holds.Remove(countryId);
                provider = hold.Provider;
            }
            try
            {
                provider.OnInstalled(countryId, formerRulerActorId,
                    installedRulerActorId);
            }
            catch { }
            return true;
        }

        public static bool Release(long countryId, long formerRulerActorId)
        {
            if (countryId <= 0 || formerRulerActorId <= 0) return false;
            SuccessionHold hold;
            lock (Gate)
            {
                if (!Holds.TryGetValue(countryId, out hold) ||
                    hold.Offer.FormerRulerActorId != formerRulerActorId)
                    return false;
                Holds.Remove(countryId);
            }
            NotifyReleased(hold.Provider, hold.Offer);
            return true;
        }

#if !AW3_RULES_TESTS
        public static bool TryDefer(Kingdom pKingdom, Actor pFormerRuler)
        {
            if (!ThreadHelper.isMainThread() || pKingdom?.data == null ||
                pFormerRuler?.data == null || pKingdom.isRekt() ||
                pKingdom.king != pFormerRuler) return false;
            try
            {
                if (pFormerRuler.isAlive()) return false;
            }
            catch { return false; }
            AW3SuccessionOffer offer = BuildOffer(pKingdom, pFormerRuler);
            return offer != null && TryBegin(offer);
        }

        public static AW3SuccessionInstallResult Install(long countryId,
            long formerRulerActorId, long successorActorId, bool useDefault)
        {
            if (!ThreadHelper.isMainThread() || countryId <= 0 ||
                formerRulerActorId <= 0 || successorActorId <= 0)
                return AW3SuccessionInstallResult.Failed();

            SuccessionHold hold;
            lock (Gate)
            {
                if (!Holds.TryGetValue(countryId, out hold) ||
                    hold.Offer.FormerRulerActorId != formerRulerActorId ||
                    hold.Installing)
                    return AW3SuccessionInstallResult.Failed();
                hold.Installing = true;
                hold.InstalledActorId = -1L;
            }

            AW3SuccessionInstallResult result =
                AW3SuccessionInstallResult.Failed();
            try
            {
                Kingdom pKingdom = World.world?.kingdoms?.get(countryId);
                Actor former = pKingdom?.king;
                if (pKingdom?.data == null || pKingdom.isRekt() ||
                    former?.data == null ||
                    former.data.id != formerRulerActorId)
                    return AW3SuccessionInstallResult.Failed();
                try
                {
                    if (former.isAlive())
                        return AW3SuccessionInstallResult.Failed();
                }
                catch { return AW3SuccessionInstallResult.Failed(); }

                AW3SuccessionOffer current = BuildOffer(pKingdom, former);
                if (current == null)
                    return AW3SuccessionInstallResult.NoLegalSuccessor();

                long selectedActorId = useDefault
                    ? current.DefaultActorId
                    : successorActorId;
                AW3SuccessionCandidate selected = current.Candidates
                    .FirstOrDefault(candidate =>
                        candidate.ActorId == selectedActorId);
                if (selected == null)
                    return useDefault
                        ? AW3SuccessionInstallResult.NoLegalSuccessor()
                        : AW3SuccessionInstallResult.SuccessorUnavailable();

                Actor successor = World.world?.units?.get(selectedActorId);
                if (!IsLiveCandidate(successor, pKingdom, former))
                    return useDefault
                        ? AW3SuccessionInstallResult.NoLegalSuccessor()
                        : AW3SuccessionInstallResult.SuccessorUnavailable();

                if (!HeirService.StoreSelectedHeir(pKingdom, successor,
                        selected.SuccessionMode))
                    return AW3SuccessionInstallResult.Failed();
                if (!SuccessionPreparationService.
                        TryOverridePublishedCandidate(pKingdom, former,
                            successor, selected.SuccessionMode))
                    return AW3SuccessionInstallResult.Failed();
                pKingdom.setKing(successor);

                bool committed;
                lock (Gate)
                    committed = ReferenceEquals(
                                    Holds.TryGetValue(countryId,
                                        out SuccessionHold currentHold)
                                        ? currentHold
                                        : null, hold) &&
                                hold.InstalledActorId == selectedActorId;
                if (pKingdom.king != successor || !committed)
                    return AW3SuccessionInstallResult.Failed();
                result = AW3SuccessionInstallResult.Installed(
                    selectedActorId);
                return result;
            }
            catch
            {
                return AW3SuccessionInstallResult.Failed();
            }
            finally
            {
                lock (Gate)
                {
                    hold.Installing = false;
                    if (result.Status ==
                            AW3SuccessionInstallStatus.Installed &&
                        Holds.TryGetValue(countryId,
                            out SuccessionHold currentHold) &&
                        ReferenceEquals(currentHold, hold))
                        Holds.Remove(countryId);
                }
            }
        }

        public static bool NotifyKingInstalled(Kingdom pKingdom,
            Actor pInstalledKing)
        {
            if (!ThreadHelper.isMainThread() || pKingdom?.data == null ||
                pInstalledKing?.data == null ||
                pKingdom.king != pInstalledKing) return false;
            long formerRulerActorId;
            lock (Gate)
            {
                if (!Holds.TryGetValue(pKingdom.id,
                        out SuccessionHold hold)) return false;
                formerRulerActorId = hold.Offer.FormerRulerActorId;
            }
            return NotifyInstalled(pKingdom.id, formerRulerActorId,
                pInstalledKing.data.id);
        }

        private static AW3SuccessionOffer BuildOffer(Kingdom pKingdom,
            Actor pFormerRuler)
        {
            if (pKingdom?.data == null || pFormerRuler?.data == null)
                return null;
            if (!SuccessionPreparationService.TryGetPublishedCandidate(
                    pKingdom, out Actor defaultActor)) return null;
            if (!IsLiveCandidate(defaultActor, pKingdom, pFormerRuler))
                return null;

            pKingdom.data.get(LineageKeys.KINGDOM_SUCCESSION_MODE,
                out string defaultMode, SuccessionMode.NONE);
            if (string.IsNullOrEmpty(defaultMode) ||
                defaultMode == SuccessionMode.NONE)
                defaultMode = SuccessionMode.REGISTERED;
            InheritanceLaw law = InheritanceLawService.GetEffectiveLaw(
                pKingdom);
            List<InheritanceCandidateSelection> finalists =
                InheritanceCandidateService.CollectFinalists(
                    pKingdom, law, pFormerRuler);

            var candidates = new List<AW3SuccessionCandidate>();
            AddCandidate(candidates, pKingdom, pFormerRuler, defaultActor,
                law, defaultActor, defaultMode, isDefault: true);
            for (var index = 0; index < finalists.Count &&
                 candidates.Count < AW3SuccessionOffer.MaximumCandidates;
                 index++)
            {
                Actor actor = finalists[index]?.Actor;
                if (!IsLiveCandidate(actor, pKingdom, pFormerRuler) ||
                    actor.data.id == defaultActor.data.id) continue;
                AddCandidate(candidates, pKingdom, pFormerRuler, actor,
                    law, defaultActor, defaultMode, isDefault: false);
            }
            if (candidates.Count == 0) return null;
            try
            {
                return new AW3SuccessionOffer(pKingdom.id,
                    pFormerRuler.data.id, defaultActor.data.id, candidates);
            }
            catch { return null; }
        }

        private static void AddCandidate(
            ICollection<AW3SuccessionCandidate> pCandidates,
            Kingdom pKingdom, Actor pFormerRuler, Actor pCandidate,
            InheritanceLaw pLaw, Actor pDefaultActor, string pDefaultMode,
            bool isDefault)
        {
            string mode = HeirService.ResolveSuccessionModeForCandidate(
                pKingdom, pFormerRuler, pCandidate, pLaw, pDefaultActor,
                pDefaultMode);
            string displayName = string.IsNullOrWhiteSpace(pCandidate.name)
                ? "Actor " + pCandidate.data.id
                : pCandidate.name;
            pCandidates.Add(new AW3SuccessionCandidate(pCandidate.data.id,
                displayName, RelationKey(mode), mode, isDefault));
        }

        private static string RelationKey(string pMode)
        {
            if (pMode == SuccessionMode.DIRECT) return "direct_son";
            if (pMode == SuccessionMode.UNDERAGE_DIRECT)
                return "underage_direct_son";
            if (pMode == SuccessionMode.REGISTERED)
                return "registered_heir";
            if (pMode == SuccessionMode.MILITARY_ACCLAIM)
                return "military_acclaim";
            if (pMode == SuccessionMode.CIVIL_ACCLAIM)
                return "civil_acclaim";
            return "collateral";
        }

        private static bool IsLiveCandidate(Actor pActor,
            Kingdom pKingdom, Actor pFormerRuler)
        {
            if (pActor?.data == null || pActor == pFormerRuler ||
                pActor.kingdom != pKingdom) return false;
            try
            {
                return pActor.isAlive() && !pActor.isRekt() &&
                       !pActor.isKing();
            }
            catch { return false; }
        }
#endif

        private static void NotifyReleased(
            IAW3MultiplayerSuccessionProvider provider,
            AW3SuccessionOffer offer)
        {
            try
            {
                provider.OnReleased(offer.CountryId,
                    offer.FormerRulerActorId);
            }
            catch { }
        }

        private sealed class SuccessionHold
        {
            internal SuccessionHold(
                IAW3MultiplayerSuccessionProvider provider,
                AW3SuccessionOffer offer)
            {
                Provider = provider;
                Offer = offer;
            }

            internal IAW3MultiplayerSuccessionProvider Provider { get; }
            internal AW3SuccessionOffer Offer { get; }
            internal bool Installing { get; set; }
            internal long InstalledActorId { get; set; } = -1L;

            internal bool Matches(AW3SuccessionOffer offer)
            {
                if (offer == null || Offer.CountryId != offer.CountryId ||
                    Offer.FormerRulerActorId != offer.FormerRulerActorId ||
                    Offer.DefaultActorId != offer.DefaultActorId ||
                    Offer.Candidates.Count != offer.Candidates.Count)
                    return false;
                for (var index = 0; index < Offer.Candidates.Count; index++)
                {
                    AW3SuccessionCandidate left = Offer.Candidates[index];
                    AW3SuccessionCandidate right = offer.Candidates[index];
                    if (left.ActorId != right.ActorId ||
                        left.IsDefault != right.IsDefault ||
                        !string.Equals(left.DisplayName, right.DisplayName,
                            StringComparison.Ordinal) ||
                        !string.Equals(left.RelationKey, right.RelationKey,
                            StringComparison.Ordinal) ||
                        !string.Equals(left.SuccessionMode,
                            right.SuccessionMode, StringComparison.Ordinal))
                        return false;
                }
                return true;
            }
        }
    }
}
