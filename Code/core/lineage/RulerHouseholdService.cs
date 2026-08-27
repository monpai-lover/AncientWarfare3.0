using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.db;
using AncientWarfare3.core.schools;

namespace AncientWarfare3.core.lineage
{
    internal static class RulerHouseholdService
    {
        private const int MaximumMaintenanceRows = 16;
        internal const int MaximumAiCandidateActors = 24;
        internal const int MaximumPlayerCandidateActors = 32;

        private static SQLiteConnection DB =>
            LineageArchiveManager.Instance?.OperatingDB;
        private static bool Ready => DB != null &&
                                     LineageArchiveManager.Instance
                                         .InitializeSuccessful;

        public static RulerHouseholdOfferPreview PrepareOffer(
            Kingdom pSource, Kingdom pRecipient, long pCandidateActorId,
            RulerHouseholdKind pKind)
        {
            return PrepareCandidate(pSource, pRecipient,
                FindActor(pCandidateActorId), pKind, pDomestic: false);
        }

        internal static RulerHouseholdOfferPreview PrepareOffer(
            Kingdom pSource, Kingdom pRecipient, Actor pCandidate,
            RulerHouseholdKind pKind)
        {
            return PrepareCandidate(pSource, pRecipient, pCandidate, pKind,
                pDomestic: false);
        }

        private static RulerHouseholdOfferPreview PrepareCandidate(
            Kingdom pSource, Kingdom pRecipient, Actor pCandidate,
            RulerHouseholdKind pKind, bool pDomestic)
        {
            var preview = new RulerHouseholdOfferPreview
            {
                Kind = pKind,
                CandidateActorId = pCandidate?.data?.id ?? -1L,
                RulerActorId = pRecipient?.king?.data?.id ?? -1L
            };
            if (!Ready)
            {
                preview.Reason = "household_not_ready";
                return preview;
            }
            if (!IsLiveRealm(pSource) || !IsLiveRealm(pRecipient) ||
                pDomestic != (pSource == pRecipient))
            {
                preview.Reason = "invalid_household_realms";
                return preview;
            }

            Actor ruler = pRecipient.king;
            if (!IsEligibleRuler(ruler, pRecipient))
            {
                preview.Reason = "invalid_household_ruler";
                return preview;
            }
            if (!IsEligibleCandidate(pCandidate, pSource, pKind, pDomestic,
                    out string reason))
            {
                preview.Reason = reason;
                return preview;
            }

            var query = new RulerHouseholdQuery(DB);
            if (query.TryReadActiveByPartner(pCandidate.data.id, out _))
            {
                preview.Reason = "candidate_in_household";
                return preview;
            }
            if (SafeRelated(pCandidate, ruler))
            {
                preview.Reason = "household_close_relative";
                return preview;
            }

            preview.ActiveConsorts = query.CountActiveConsorts(ruler.data.id);
            preview.ConsortCapacity = RulerHouseholdRules.ConsortCapacity(
                ResolveRealmTier(pRecipient));
            preview.HasPrincipalWife = query.HasActivePrincipal(
                                           ruler.data.id) ||
                                       HasLivingMutualSpouse(ruler);
            var facts = new RulerHouseholdOfferFacts(
                candidateEligible: true, rulerEligible: true,
                related: false,
                hasPrincipalWife: preview.HasPrincipalWife,
                activeConsorts: preview.ActiveConsorts,
                consortCapacity: preview.ConsortCapacity);
            if (!RulerHouseholdRules.CanOffer(facts, pKind))
            {
                preview.Reason = pKind == RulerHouseholdKind.PrincipalWife
                    ? "principal_wife_exists"
                    : "consort_capacity_full";
                return preview;
            }

            preview.Available = true;
            preview.Reason = "";
            return preview;
        }

        internal static bool TryPrepareAiOffer(Kingdom pSource,
            Kingdom pRecipient, out RulerHouseholdOfferPreview pPreview)
        {
            pPreview = new RulerHouseholdOfferPreview
            {
                Reason = "no_household_candidate"
            };
            if (!Ready || !IsLiveRealm(pSource) ||
                !IsLiveRealm(pRecipient) || pSource == pRecipient)
                return false;
            Actor ruler = pRecipient.king;
            if (!IsEligibleRuler(ruler, pRecipient)) return false;

            var query = new RulerHouseholdQuery(DB);
            int activeConsorts = query.CountActiveConsorts(ruler.data.id);
            int capacity = RulerHouseholdRules.ConsortCapacity(
                ResolveRealmTier(pRecipient));
            bool hasPrincipal = query.HasActivePrincipal(ruler.data.id) ||
                                HasLivingMutualSpouse(ruler);
            if (!RulerHouseholdRules.TrySelectAiOfferKind(hasPrincipal,
                    activeConsorts, capacity,
                    out RulerHouseholdKind kind)) return false;

            return TryPrepareAiOfferKind(pSource, pRecipient, kind,
                out pPreview);
        }

        internal static bool TryPrepareAiConsortOffer(Kingdom pSource,
            Kingdom pRecipient, out RulerHouseholdOfferPreview pPreview)
        {
            return TryPrepareAiOfferKind(pSource, pRecipient,
                RulerHouseholdKind.Consort, out pPreview);
        }

        internal static RulerHouseholdConsortRequestPreview
            PrepareConsortRequest(Kingdom pVacancyRealm,
                Kingdom pSupplierRealm, int pOpinion,
                bool pEquivalentPending, bool pRejectionCooldown)
        {
            var result = new RulerHouseholdConsortRequestPreview
            {
                RulerActorId = pVacancyRealm?.king?.data?.id ?? -1L
            };
            if (!Ready || !IsLiveRealm(pVacancyRealm) ||
                !IsLiveRealm(pSupplierRealm) ||
                pVacancyRealm == pSupplierRealm)
            {
                result.Reason = "invalid_household_realms";
                return result;
            }

            Actor ruler = pVacancyRealm.king;
            bool rulerEligible = IsEligibleRuler(ruler, pVacancyRealm);
            var query = new RulerHouseholdQuery(DB);
            result.ActiveConsorts = rulerEligible
                ? query.CountActiveConsorts(ruler.data.id)
                : 0;
            result.ConsortCapacity = RulerHouseholdRules.ConsortCapacity(
                ResolveRealmTier(pVacancyRealm));
            bool supplierHasCandidate = TryPrepareAiConsortOffer(
                pSupplierRealm, pVacancyRealm,
                out RulerHouseholdOfferPreview offer);
            result.SuggestedCandidateActorId = offer?.CandidateActorId ?? -1L;
            var facts = new RulerHouseholdConsortRequestFacts(
                rulerEligible, result.ActiveConsorts,
                result.ConsortCapacity,
                requesterIndependent: IsIndependentRealm(pVacancyRealm),
                supplierIndependent: IsIndependentRealm(pSupplierRealm),
                pOpinion, supplierHasCandidate, pEquivalentPending,
                pRejectionCooldown);
            if (!RulerHouseholdRules.CanRequestConsort(facts))
            {
                if (!rulerEligible)
                    result.Reason = "invalid_household_ruler";
                else if (!IsIndependentRealm(pVacancyRealm) ||
                         !IsIndependentRealm(pSupplierRealm))
                    result.Reason = "consort_request_requires_independence";
                else if (result.ActiveConsorts >= result.ConsortCapacity)
                    result.Reason = "consort_capacity_full";
                else if (pOpinion <
                         RulerHouseholdRules.MinimumConsortRequestOpinion)
                    result.Reason = "consort_request_relation_low";
                else if (pEquivalentPending)
                    result.Reason = "pending_exists";
                else if (pRejectionCooldown)
                    result.Reason = "ai_rejection_cooldown";
                else
                    result.Reason = "no_household_candidate";
                return result;
            }

            result.Available = true;
            result.Reason = "";
            return result;
        }

        internal static bool HasPlausibleConsortSupplier(Kingdom pSource,
            Kingdom pRecipient)
        {
            if (!Ready || !IsLiveRealm(pSource) ||
                !IsLiveRealm(pRecipient) || pSource == pRecipient ||
                !IsIndependentRealm(pSource) ||
                !IsIndependentRealm(pRecipient) ||
                !IsEligibleRuler(pRecipient.king, pRecipient)) return false;
            var query = new RulerHouseholdQuery(DB);
            int active = query.CountActiveConsorts(
                pRecipient.king.data.id);
            int capacity = RulerHouseholdRules.ConsortCapacity(
                ResolveRealmTier(pRecipient));
            if (active >= capacity) return false;
            return query.ReadOfferCandidateIds(pSource.id,
                ResolveRulingLineageId(pSource),
                pRecipient.king.data.id,
                RulerHouseholdKind.Consort, pIncludeSlaves: false,
                pRequestedLimit: 1).Count > 0;
        }

        internal static RulerHouseholdOfferCandidatePool
            BuildOfferCandidatePool(Kingdom pSource, Kingdom pRecipient,
                RulerHouseholdKind pKind)
        {
            return BuildCandidatePool(pSource, pRecipient, pKind,
                pDomestic: false);
        }

        internal static RulerHouseholdOfferCandidatePool
            BuildDomesticCandidatePool(Kingdom pKingdom,
                RulerHouseholdKind pKind)
        {
            return BuildCandidatePool(pKingdom, pKingdom, pKind,
                pDomestic: true);
        }

        private static RulerHouseholdOfferCandidatePool BuildCandidatePool(
            Kingdom pSource, Kingdom pRecipient,
            RulerHouseholdKind pKind, bool pDomestic)
        {
            var pool = new RulerHouseholdOfferCandidatePool();
            if (!Ready || !IsLiveRealm(pSource) ||
                !IsLiveRealm(pRecipient) ||
                pDomestic != (pSource == pRecipient))
            {
                pool.Reason = "invalid_household_realms";
                return pool;
            }

            Actor ruler = pRecipient.king;
            pool.RulerActorId = ruler.data.id;
            pool.RulerName = ruler.getName() ?? "";
            pool.RulerTitle =
                RulerAppellationService.GetFullLivingAppellation(pRecipient);
            var query = new RulerHouseholdQuery(DB);
            pool.ActiveConsorts = query.CountActiveConsorts(ruler.data.id);
            pool.ConsortCapacity = RulerHouseholdRules.ConsortCapacity(
                ResolveRealmTier(pRecipient));
            bool hasPrincipal = query.HasActivePrincipal(ruler.data.id) ||
                                HasLivingMutualSpouse(ruler);
            if (pKind == RulerHouseholdKind.PrincipalWife && hasPrincipal)
            {
                pool.Reason = "principal_wife_exists";
                return pool;
            }
            if (pKind == RulerHouseholdKind.Consort &&
                pool.ActiveConsorts >= pool.ConsortCapacity)
            {
                pool.Reason = "consort_capacity_full";
                return pool;
            }

            long rulingLineageId = ResolveRulingLineageId(pSource);
            IReadOnlyList<long> ids = query.ReadOfferCandidateIds(
                pSource.id, rulingLineageId, pRecipient.king.data.id, pKind,
                pIncludeSlaves: pDomestic,
                MaximumPlayerCandidateActors);
            for (int i = 0; i < ids.Count; i++)
            {
                Actor candidate = FindActor(ids[i]);
                RulerHouseholdOfferPreview preview = PrepareCandidate(
                    pSource, pRecipient, candidate, pKind, pDomestic);
                if (!preview.Available) continue;
                ActorArchiveTableItem archive =
                    LineageArchiveReader.ReadRow(ids[i]);
                RulerHouseholdCandidateClass candidateClass =
                    ResolveCandidateClass(candidate);
                bool memberOfRulingLineage = rulingLineageId >= 0L &&
                    archive?.lineage_id == rulingLineageId;
                bool directChild = IsDirectChildOfRuler(candidate,
                    pSource.king);
                pool.Candidates.Add(new RulerHouseholdOfferCandidate
                {
                    ActorId = ids[i],
                    Actor = candidate,
                    ActorName = candidate?.getName() ??
                                archive?.display_name ?? "",
                    Age = SafeAge(candidate),
                    MemberOfRulingLineage = memberOfRulingLineage,
                    DirectChildOfRuler = directChild,
                    CandidateClass = candidateClass,
                    AttributeScore = HouseholdAttributeScore(candidate),
                    LineagePriority =
                        RulerHouseholdRules.HouseholdCandidatePriority(
                            memberOfRulingLineage, directChild,
                            candidateClass),
                    LineageLabel = AncestryDisplayRules.FormatLineageLabel(
                        archive?.city_name, archive?.clan_name)
                });
            }
            pool.Candidates.Sort((left, right) =>
                CompareHouseholdCandidates(left, right, pKind));
            pool.Reason = pool.Candidates.Count > 0
                ? ""
                : "no_household_candidate";
            return pool;
        }

        private static bool TryPrepareAiOfferKind(Kingdom pSource,
            Kingdom pRecipient, RulerHouseholdKind pKind,
            out RulerHouseholdOfferPreview pPreview)
        {
            pPreview = new RulerHouseholdOfferPreview
            {
                Kind = pKind,
                Reason = "no_household_candidate"
            };
            if (!Ready || !IsLiveRealm(pSource) ||
                !IsLiveRealm(pRecipient) || pSource == pRecipient)
                return false;
            long rulingLineageId = ResolveRulingLineageId(pSource);
            var query = new RulerHouseholdQuery(DB);
            IReadOnlyList<long> candidateIds = query.ReadOfferCandidateIds(
                pSource.id, rulingLineageId, pRecipient.king.data.id, pKind,
                pIncludeSlaves: false,
                MaximumAiCandidateActors);
            var candidates = new List<RulerHouseholdOfferCandidate>(
                candidateIds.Count);
            for (int index = 0; index < candidateIds.Count; index++)
            {
                Actor actor = FindActor(candidateIds[index]);
                if (actor?.data == null) continue;
                actor.data.get(LineageKeys.LINEAGE_ID,
                    out long actorLineageId, -1L);
                RulerHouseholdCandidateClass candidateClass =
                    ResolveCandidateClass(actor);
                bool memberOfRulingLineage = rulingLineageId >= 0L &&
                                             actorLineageId == rulingLineageId;
                bool directChild = IsDirectChildOfRuler(actor,
                    pSource.king);
                candidates.Add(new RulerHouseholdOfferCandidate
                {
                    ActorId = actor.data.id,
                    Actor = actor,
                    Age = SafeAge(actor),
                    MemberOfRulingLineage = memberOfRulingLineage,
                    DirectChildOfRuler = directChild,
                    CandidateClass = candidateClass,
                    AttributeScore = HouseholdAttributeScore(actor),
                    LineagePriority =
                        RulerHouseholdRules.HouseholdCandidatePriority(
                            memberOfRulingLineage, directChild,
                            candidateClass)
                });
            }
            candidates.Sort((left, right) =>
                CompareHouseholdCandidates(left, right, pKind));
            for (int index = 0; index < candidates.Count; index++)
            {
                RulerHouseholdOfferPreview preview = PrepareOffer(pSource,
                    pRecipient, candidates[index].Actor, pKind);
                if (!preview.Available) continue;
                pPreview = preview;
                return true;
            }
            return false;
        }

        public static bool TryCommit(Kingdom pSource, Kingdom pRecipient,
            long pCandidateActorId, RulerHouseholdKind pKind,
            long pSourceProposalId, out string pReason)
        {
            pReason = "household_commit_failed";
            if (!IsAuthority() || !Ready || pSourceProposalId < 0L)
                return false;

            var query = new RulerHouseholdQuery(DB);
            if (query.TryReadByProposal(pSourceProposalId,
                    out RulerHouseholdRecord existing))
            {
                pReason = existing.Active ? "" : "household_relation_closed";
                return existing.Active;
            }

            RulerHouseholdOfferPreview preview = PrepareOffer(pSource,
                pRecipient, pCandidateActorId, pKind);
            if (!preview.Available)
            {
                pReason = preview.Reason;
                return false;
            }

            return TryCommitCore(pSource, pRecipient, pCandidateActorId,
                pKind, pSourceProposalId, pApplyDiplomacyEffects: true,
                out pReason);
        }

        public static bool TryCommitDomestic(Kingdom pKingdom,
            long pCandidateActorId, RulerHouseholdKind pKind,
            out string pReason)
        {
            pReason = "household_commit_failed";
            if (!IsAuthority() || !Ready) return false;
            RulerHouseholdOfferPreview preview = PrepareCandidate(pKingdom,
                pKingdom, FindActor(pCandidateActorId), pKind,
                pDomestic: true);
            if (!preview.Available)
            {
                pReason = preview.Reason;
                return false;
            }
            return TryCommitCore(pKingdom, pKingdom, pCandidateActorId,
                pKind, pSourceProposalId: -1L,
                pApplyDiplomacyEffects: false, out pReason);
        }

        internal static bool TryFillOneDomesticVacancy(Kingdom pKingdom)
        {
            if (!IsAuthority() || !Ready || !IsLiveRealm(pKingdom) ||
                !IsEligibleRuler(pKingdom.king, pKingdom)) return false;

            Actor ruler = pKingdom.king;
            var query = new RulerHouseholdQuery(DB);
            bool hasPrincipal = query.HasActivePrincipal(ruler.data.id) ||
                                HasLivingMutualSpouse(ruler);
            int activeConsorts = query.CountActiveConsorts(ruler.data.id);
            int capacity = RulerHouseholdRules.ConsortCapacity(
                ResolveRealmTier(pKingdom));

            RulerHouseholdOfferCandidatePool principalPool = null;
            bool principalCandidateAvailable = false;
            if (!hasPrincipal)
            {
                principalPool = BuildDomesticCandidatePool(pKingdom,
                    RulerHouseholdKind.PrincipalWife);
                principalCandidateAvailable =
                    principalPool.Candidates.Count > 0;
            }

            RulerHouseholdOfferCandidatePool consortPool = null;
            bool consortCandidateAvailable = false;
            if (!principalCandidateAvailable && activeConsorts < capacity)
            {
                consortPool = BuildDomesticCandidatePool(pKingdom,
                    RulerHouseholdKind.Consort);
                consortCandidateAvailable = consortPool.Candidates.Count > 0;
            }

            if (!RulerHouseholdRules.TrySelectDomesticFillKind(hasPrincipal,
                    activeConsorts, capacity, principalCandidateAvailable,
                    consortCandidateAvailable,
                    out RulerHouseholdKind kind)) return false;
            RulerHouseholdOfferCandidatePool selectedPool = kind ==
                RulerHouseholdKind.PrincipalWife ? principalPool : consortPool;
            if (selectedPool == null || selectedPool.Candidates.Count == 0)
                return false;
            return TryCommitDomestic(pKingdom,
                selectedPool.Candidates[0].ActorId, kind, out _);
        }

        private static bool TryCommitCore(Kingdom pSource,
            Kingdom pRecipient, long pCandidateActorId,
            RulerHouseholdKind pKind, long pSourceProposalId,
            bool pApplyDiplomacyEffects, out string pReason)
        {
            pReason = "household_commit_failed";
            Actor partner = FindActor(pCandidateActorId);
            Actor ruler = pRecipient.king;
            City capital = pRecipient.capital;
            if (partner?.data == null || ruler?.data == null ||
                capital?.data == null || capital.isRekt() ||
                capital.kingdom != pRecipient)
            {
                pReason = "invalid_recipient_capital";
                return false;
            }

            try
            {
                LineageService.ArchiveActor(partner, pAlive: true);
                LineageService.ArchiveActor(ruler, pAlive: true);
                long relationshipId = TableIdAllocator.Next(DB,
                    RulerHouseholdTableItem.GetTableName(),
                    "RELATIONSHIP_ID");
                int year = SafeYear();
                using SQLiteTransaction transaction = DB.BeginTransaction();
                InsertRelationship(transaction, relationshipId, ruler,
                    partner, pSource, pRecipient, pKind, year,
                    pSourceProposalId);
                if (pApplyDiplomacyEffects)
                {
                    long modifierId = TableIdAllocator.Next(DB,
                        DiplomaticRelationModifierTableItem.GetTableName(),
                        "MODIFIER_ID");
                    if (!DiplomaticRelationModifierService.Upsert(transaction,
                            modifierId, pSource.id, pRecipient.id,
                            "ruler_household", relationshipId,
                            RulerHouseholdRules.RelationshipBonus(pKind), year,
                            int.MaxValue))
                        throw new InvalidOperationException(
                            "household relation modifier write failed");
                }
                transaction.Commit();

                if ((partner.kingdom != pRecipient || partner.city != capital) &&
                    !MovePartner(partner, pRecipient, capital))
                {
                    CloseRelationship(relationshipId, partner);
                    pReason = "household_migration_failed";
                    return false;
                }
                if (pKind == RulerHouseholdKind.PrincipalWife)
                    ruler.becomeLoversWith(partner);
                else
                    partner.data.set(LineageKeys.RULER_HOUSEHOLD_RULER_ID,
                        ruler.data.id);
                LineageService.ArchiveActor(partner, pAlive: true);
                LineageService.ArchiveActor(ruler, pAlive: true);
                RecordHouseholdHistory(pSource, pRecipient, partner, ruler,
                    pKind);
                pReason = "";
                return true;
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Ruler household commit failed: " +
                                    error.Message);
                return false;
            }
        }

        public static void OnKingdomYear(Kingdom pKingdom)
        {
            if (!IsAuthority() || !Ready || !IsLiveRealm(pKingdom)) return;
            try
            {
                NormalizeImperialRanks(pKingdom);
                pKingdom.data.get(LineageKeys.RULER_HOUSEHOLD_CURSOR,
                    out long cursor, -1L);
                var query = new RulerHouseholdQuery(DB);
                IReadOnlyList<RulerHouseholdRecord> rows =
                    query.ReadActiveByRecipient(pKingdom.id, cursor,
                        MaximumMaintenanceRows);
                long nextCursor = -1L;
                for (int i = 0; i < rows.Count; i++)
                {
                    RulerHouseholdRecord row = rows[i];
                    nextCursor = row.RelationshipId;
                    Actor ruler = FindActor(row.RulerActorId);
                    Actor partner = FindActor(row.PartnerActorId);
                    bool rulerAlive = IsLiveActor(ruler);
                    bool partnerAlive = IsLiveActor(partner);
                    bool stillReigning = rulerAlive &&
                                          pKingdom.king == ruler;
                    bool sameRealm = partnerAlive &&
                                     partner.kingdom == pKingdom;
                    bool close = RulerHouseholdRules.ShouldCloseRelationship(
                        row.Active, rulerAlive, partnerAlive, stillReigning,
                        sameRealm, row.IsTributaryOffering);
                    if (!close &&
                        row.Kind == RulerHouseholdKind.PrincipalWife)
                        close = ruler.lover != partner ||
                                partner.lover != ruler;
                    if (close)
                    {
                        CloseRelationship(row.RelationshipId, partner);
                        continue;
                    }
                    RepairPartnerCache(partner, row);
                }
                pKingdom.data.set(LineageKeys.RULER_HOUSEHOLD_CURSOR,
                    rows.Count < MaximumMaintenanceRows ? -1L : nextCursor);
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Ruler household maintenance failed: " +
                                    error.Message);
            }
        }

        public static void OnActorDying(Actor pActor)
        {
            if (!IsAuthority() || !Ready || pActor?.data == null) return;
            try
            {
                var query = new RulerHouseholdQuery(DB);
                if (query.TryReadActiveByPartner(pActor.data.id,
                        out RulerHouseholdRecord partnerRow))
                    CloseRelationship(partnerRow.RelationshipId, pActor);

                IReadOnlyList<RulerHouseholdRecord> ruled =
                    query.ReadActiveByOwner(pActor.data.id,
                        MaximumMaintenanceRows);
                for (int i = 0; i < ruled.Count; i++)
                    CloseRelationship(ruled[i].RelationshipId,
                        FindActor(ruled[i].PartnerActorId));
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Ruler household death closure failed: " +
                                    error.Message);
            }
        }

        public static void OnActorLoaded(Actor pActor)
        {
            if (!Ready || pActor?.data == null) return;
            try
            {
                var query = new RulerHouseholdQuery(DB);
                if (query.TryReadActiveByPartner(pActor.data.id,
                        out RulerHouseholdRecord row))
                {
                    RepairPartnerCache(pActor, row);
                    return;
                }
                pActor.data.removeLong(LineageKeys.RULER_HOUSEHOLD_RULER_ID);
            }
            catch
            {
                pActor.data.removeLong(LineageKeys.RULER_HOUSEHOLD_RULER_ID);
            }
        }

        internal static RulerHouseholdRealmTier ResolveRealmTier(
            Kingdom pKingdom)
        {
            if (HeirTitleRules.IsImperialOrMandate(pKingdom))
                return RulerHouseholdRealmTier.Empire;
            return KingdomTitleService.GetTitle(pKingdom) >= KingdomTitle.King
                ? RulerHouseholdRealmTier.Kingdom
                : RulerHouseholdRealmTier.Lower;
        }

        private static void InsertRelationship(SQLiteTransaction transaction,
            long pRelationshipId, Actor pRuler, Actor pPartner,
            Kingdom pSource, Kingdom pRecipient, RulerHouseholdKind pKind,
            int pYear, long pProposalId, string pOwnerRole = "king",
            string pSourceKind = "diplomatic_offer",
            long pSourceRelationId = -1L, int pSourceTributeYear = -1)
        {
            string rankCode = ResolveStoredRankCode(transaction, pRuler,
                pRecipient, pKind);
            if (string.IsNullOrEmpty(rankCode))
                throw new InvalidOperationException(
                    "household rank capacity exhausted");
            using var command = new SQLiteCommand(DB)
            {
                Transaction = transaction,
                CommandText =
                    "INSERT INTO RulerHousehold " +
                    "(RELATIONSHIP_ID,RULER_ACTOR_ID,PARTNER_ACTOR_ID," +
                    "SOURCE_KINGDOM_ID,RECIPIENT_KINGDOM_ID," +
                    "RELATIONSHIP_KIND,RANK_CODE,START_YEAR,START_TIME," +
                    "END_TIME,STATUS,SOURCE_PROPOSAL_ID,OWNER_ROLE_AT_ENTRY," +
                    "SOURCE_KIND,SOURCE_RELATION_ID,SOURCE_TRIBUTE_YEAR) VALUES " +
                    "(@id,@ruler,@partner,@source,@recipient,@kind,@rank," +
                    "@year,@time,-1,0,@proposal,@ownerRole,@sourceKind," +
                    "@sourceRelation,@sourceTributeYear)"
            };
            command.Parameters.AddWithValue("@id", pRelationshipId);
            command.Parameters.AddWithValue("@ruler", pRuler.data.id);
            command.Parameters.AddWithValue("@partner", pPartner.data.id);
            command.Parameters.AddWithValue("@source", pSource.id);
            command.Parameters.AddWithValue("@recipient", pRecipient.id);
            command.Parameters.AddWithValue("@kind", KindCode(pKind));
            command.Parameters.AddWithValue("@rank",
                rankCode);
            command.Parameters.AddWithValue("@year", pYear);
            command.Parameters.AddWithValue("@time", LineageService.CurTime());
            command.Parameters.AddWithValue("@proposal", pProposalId);
            command.Parameters.AddWithValue("@ownerRole", pOwnerRole ?? "");
            command.Parameters.AddWithValue("@sourceKind", pSourceKind ?? "");
            command.Parameters.AddWithValue("@sourceRelation", pSourceRelationId);
            command.Parameters.AddWithValue("@sourceTributeYear", pSourceTributeYear);
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException(
                    "household relationship insert failed");
        }

        private static bool MovePartner(Actor partner, Kingdom pRecipient,
            City pCapital)
        {
            Kingdom sourceKingdom = partner?.kingdom;
            City sourceCity = partner?.city;
            try
            {
                partner.cancelAllBeh();
                using (FormalAffiliationTransferScope.Open(
                           partner.data.id, pRecipient.id, pCapital.data.id))
                {
                    if (partner.kingdom != pRecipient)
                        partner.joinKingdom(pRecipient);
                    if (partner.city != pCapital)
                        partner.joinCity(pCapital);
                }
                if (partner.kingdom == pRecipient && partner.city == pCapital)
                    return true;
                RestorePartnerAffiliation(partner, sourceKingdom, sourceCity);
                return false;
            }
            catch
            {
                RestorePartnerAffiliation(partner, sourceKingdom, sourceCity);
                return false;
            }
        }

        private static void RestorePartnerAffiliation(Actor partner,
            Kingdom sourceKingdom, City sourceCity)
        {
            if (partner?.data == null || sourceKingdom?.data == null ||
                sourceKingdom.isRekt()) return;
            try
            {
                using (FormalAffiliationTransferScope.Open(partner.data.id,
                           sourceKingdom.id, sourceCity?.data?.id ?? -1L))
                {
                    if (partner.kingdom != sourceKingdom)
                        partner.joinKingdom(sourceKingdom);
                    if (sourceCity?.data != null && !sourceCity.isRekt() &&
                        sourceCity.kingdom == sourceKingdom &&
                        partner.city != sourceCity)
                        partner.joinCity(sourceCity);
                }
            }
            catch { }
        }

        private static void CloseRelationship(long pRelationshipId,
            Actor pPartner)
        {
            if (pRelationshipId < 0L) return;
            using var command = new SQLiteCommand(
                "UPDATE RulerHousehold SET STATUS=1,END_TIME=@time WHERE " +
                "RELATIONSHIP_ID=@id AND STATUS=0 AND END_TIME<0", DB);
            command.Parameters.AddWithValue("@time", LineageService.CurTime());
            command.Parameters.AddWithValue("@id", pRelationshipId);
            command.ExecuteNonQuery();
            DiplomaticRelationModifierService.DeactivateSource(
                "ruler_household", pRelationshipId);
            pPartner?.data?.removeLong(
                LineageKeys.RULER_HOUSEHOLD_RULER_ID);
        }

        private static void RepairPartnerCache(Actor pPartner,
            RulerHouseholdRecord pRecord)
        {
            if (pPartner?.data == null) return;
            if (pRecord != null && pRecord.Active &&
                pRecord.Kind == RulerHouseholdKind.Consort)
                pPartner.data.set(LineageKeys.RULER_HOUSEHOLD_RULER_ID,
                    pRecord.RulerActorId);
            else
                pPartner.data.removeLong(
                    LineageKeys.RULER_HOUSEHOLD_RULER_ID);
        }

        private static bool IsEligibleCandidate(Actor pCandidate,
            Kingdom pSource, RulerHouseholdKind pKind, bool pDomestic,
            out string pReason)
        {
            pReason = "invalid_household_candidate";
            if (!IsLiveActor(pCandidate)) return false;
            if (pCandidate.kingdom != pSource)
            {
                pReason = "candidate_not_domestic";
                return false;
            }
            if (pCandidate.isKing())
            {
                pReason = "candidate_is_ruler";
                return false;
            }
            if (!pCandidate.isSexFemale())
            {
                pReason = "candidate_not_female";
                return false;
            }
            if (!pCandidate.isAdult())
            {
                pReason = "candidate_not_adult";
                return false;
            }
            if (!RulerHouseholdRules.IsHouseholdCandidateAge(
                    SafeAge(pCandidate)))
            {
                pReason = "candidate_not_household_age";
                return false;
            }
            RulerHouseholdCandidateClass candidateClass =
                ResolveCandidateClass(pCandidate);
            if (!RulerHouseholdRules.IsCandidateClassEligible(
                    candidateClass, pKind,
                    allowSlaveConsort: pDomestic))
            {
                pReason = candidateClass == RulerHouseholdCandidateClass.Slave
                    ? "candidate_is_slave"
                    : "candidate_not_noble";
                return false;
            }
            if (candidateClass == RulerHouseholdCandidateClass.Noble &&
                !NobleHeirPregnancyService.IsEligibleNoble(pCandidate))
            {
                pReason = "candidate_not_noble";
                return false;
            }
            if (pCandidate.hasLover())
            {
                pReason = "candidate_married";
                return false;
            }
            pReason = "";
            return true;
        }

        private static RulerHouseholdCandidateClass ResolveCandidateClass(
            Actor pCandidate)
        {
            if (SlaveService.IsSlave(pCandidate))
                return RulerHouseholdCandidateClass.Slave;
            return HasNobleLineage(pCandidate)
                ? RulerHouseholdCandidateClass.Noble
                : RulerHouseholdCandidateClass.Commoner;
        }

        private static bool HasNobleLineage(Actor pCandidate)
        {
            if (pCandidate?.data == null) return false;
            pCandidate.data.get(LineageKeys.LINEAGE_STATUS,
                out string status, LineageStatus.NONE);
            pCandidate.data.get(LineageKeys.LINEAGE_ID,
                out long lineageId, -1L);
            pCandidate.data.get(LineageKeys.SHI_ID, out long shiId, -1L);
            return status == LineageStatus.NOBLE && lineageId >= 0L &&
                   shiId >= 0L;
        }

        private static long ResolveRulingLineageId(Kingdom pKingdom)
        {
            long lineageId = -1L;
            pKingdom?.king?.data?.get(LineageKeys.LINEAGE_ID,
                out lineageId, -1L);
            return lineageId;
        }

        private static bool IsDirectChildOfRuler(Actor pActor,
            Actor pRuler)
        {
            if (pActor?.data == null || pRuler?.data == null) return false;
            long rulerId = pRuler.data.id;
            return pActor.data.parent_id_1 == rulerId ||
                   pActor.data.parent_id_2 == rulerId;
        }

        private static void RecordHouseholdHistory(Kingdom pSource,
            Kingdom pRecipient, Actor pPartner, Actor pRuler,
            RulerHouseholdKind pKind, string pEventType = null,
            string pHistoryKey = null)
        {
            string partnerName = pPartner.getName() ?? "";
            string rulerName = pRuler.getName() ?? "";
            string key = pKind == RulerHouseholdKind.PrincipalWife
                ? "aw_hist_household_principal_wife_accepted"
                : "aw_hist_household_consort_accepted";
            string fallback = pKind == RulerHouseholdKind.PrincipalWife
                ? " entered the recipient court as principal wife of "
                : " entered the recipient court as consort of ";
            string localized = HistoryLocalizationRules.Text(key);
            if (string.IsNullOrWhiteSpace(localized) || localized == key)
                localized = fallback;
            if (!string.IsNullOrEmpty(pHistoryKey))
            {
                string offeringText = HistoryLocalizationRules.Text(
                    pHistoryKey);
                if (!string.IsNullOrWhiteSpace(offeringText) &&
                    offeringText != pHistoryKey)
                    localized = offeringText;
            }
            string text = partnerName + localized + rulerName;
            string eventType = string.IsNullOrEmpty(pEventType)
                ? PersonEvent.ROYAL_MARRIAGE
                : pEventType;
            string kingdomEventType = string.IsNullOrEmpty(pEventType)
                ? KingdomEvent.ROYAL_MARRIAGE
                : pEventType;
            HistoryWriter.RecordPerson(pPartner.data.id, pRecipient,
                partnerName, eventType, text,
                ChronicleCategory.BOND, HistoryTarget.Actor(pRuler));
            HistoryWriter.RecordPerson(pRuler.data.id, pRecipient,
                rulerName, eventType, text,
                ChronicleCategory.BOND, HistoryTarget.Actor(pPartner));
            HistoryWriter.RecordKingdom(pSource,
                kingdomEventType, text,
                HistoryTarget.Kingdom(pRecipient));
            if (pSource != pRecipient)
                HistoryWriter.RecordKingdom(pRecipient,
                    kingdomEventType, text,
                    HistoryTarget.Kingdom(pSource));
        }

        private static bool IsEligibleRuler(Actor pRuler, Kingdom pRealm)
        {
            return IsLiveActor(pRuler) && pRealm?.king == pRuler &&
                   pRuler.kingdom == pRealm && pRuler.isSexMale() &&
                   pRuler.isAdult() && pRuler.isBreedingAge() &&
                   !RepublicGovernmentService.IsRepublic(pRealm);
        }

        internal static bool TryCommitTributaryConsort(Kingdom pSource,
            Kingdom pRecipient, Actor pOwner, Actor pCandidate,
            string pOwnerRoleAtEntry, long pRelationId, int pTributeYear,
            int pCapacity, out string pReason,
            string pSourceKind = "tributary_offering")
        {
            pReason = "household_commit_failed";
            if (!IsAuthority() || !Ready || !IsLiveRealm(pSource) ||
                !IsLiveRealm(pRecipient) || pSource == pRecipient ||
                !IsEligibleTributaryOwner(pOwner, pRecipient) ||
                pCandidate?.data == null || pRelationId < 0L)
                return false;
            var query = new RulerHouseholdQuery(DB);
            if (query.HasTributaryOffering(pRelationId, pTributeYear))
            {
                pReason = "duplicate";
                return false;
            }
            if (!IsEligibleCandidate(pCandidate, pSource,
                    RulerHouseholdKind.Consort, pDomestic: false,
                    out pReason) ||
                query.TryReadActiveByPartner(pCandidate.data.id, out _) ||
                SafeRelated(pOwner, pCandidate))
            {
                if (string.IsNullOrEmpty(pReason)) pReason = "no_candidate";
                return false;
            }
            if (query.CountActiveConsorts(pOwner.data.id) >=
                Math.Max(0, pCapacity))
            {
                pReason = "consort_capacity_full";
                return false;
            }
            City capital = pRecipient.capital;
            if (capital?.data == null || capital.isRekt() ||
                capital.kingdom != pRecipient)
            {
                pReason = "invalid_recipient_capital";
                return false;
            }
            try
            {
                LineageService.ArchiveActor(pCandidate, pAlive: true);
                LineageService.ArchiveActor(pOwner, pAlive: true);
                long relationshipId = TableIdAllocator.Next(DB,
                    RulerHouseholdTableItem.GetTableName(),
                    "RELATIONSHIP_ID");
                using SQLiteTransaction transaction = DB.BeginTransaction();
                InsertRelationship(transaction, relationshipId, pOwner,
                    pCandidate, pSource, pRecipient,
                    RulerHouseholdKind.Consort, SafeYear(), -1L,
                    pOwnerRoleAtEntry, pSourceKind ?? "tributary_offering", pRelationId,
                    pTributeYear);
                transaction.Commit();
                if (!MovePartner(pCandidate, pRecipient, capital))
                {
                    CloseRelationship(relationshipId, pCandidate);
                    pReason = "migration_failed";
                    return false;
                }
                pCandidate.data.set(LineageKeys.RULER_HOUSEHOLD_RULER_ID,
                    pOwner.data.id);
                LineageService.ArchiveActor(pCandidate, pAlive: true);
                LineageService.ArchiveActor(pOwner, pAlive: true);
                RecordHouseholdHistory(pSource, pRecipient, pCandidate,
                    pOwner, RulerHouseholdKind.Consort,
                    PersonEvent.SUBJECT_CONSORT_OFFERED,
                    string.Equals(pSourceKind, "vassal_offering",
                        StringComparison.Ordinal)
                        ? "aw_hist_subject_consort_vassal_mid"
                        : "aw_hist_subject_consort_tributary_mid");
                pReason = "";
                return true;
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Tributary household commit failed: " +
                                    error.Message);
                return false;
            }
        }

        internal static bool IsEligibleTributaryOwner(Actor pOwner,
            Kingdom pRecipient)
        {
            return IsLiveActor(pOwner) && pOwner.kingdom == pRecipient &&
                   pOwner.isSexMale() && pOwner.isAdult() &&
                   pOwner.isBreedingAge() && pOwner.canBreed() &&
                   pOwner.canProduceBabies() &&
                   !RepublicGovernmentService.IsRepublic(pRecipient);
        }

        internal static IReadOnlyList<Actor>
            BuildEligibleConsortCandidates(Kingdom pSource)
        {
            var result = new List<Actor>();
            if (!Ready || !IsLiveRealm(pSource)) return result;
            var query = new RulerHouseholdQuery(DB);
            long lineageId = ResolveRulingLineageId(pSource);
            IReadOnlyList<long> ids = query.ReadOfferCandidateIds(
                pKingdomId: pSource.id,
                pRulingLineageId: lineageId,
                pExcludedParentId: -1L,
                pKind: RulerHouseholdKind.Consort,
                pIncludeSlaves: false,
                pRequestedLimit: MaximumPlayerCandidateActors);
            for (int index = 0; index < ids.Count; index++)
            {
                Actor candidate = FindActor(ids[index]);
                if (!IsEligibleCandidate(candidate, pSource,
                        RulerHouseholdKind.Consort, pDomestic: false,
                        out _) ||
                    query.TryReadActiveByPartner(ids[index], out _))
                    continue;
                result.Add(candidate);
            }
            result.Sort((left, right) =>
            {
                left.data.get(LineageKeys.LINEAGE_ID, out long leftLineage,
                    -1L);
                right.data.get(LineageKeys.LINEAGE_ID, out long rightLineage,
                    -1L);
                RulerHouseholdCandidateClass leftClass =
                    ResolveCandidateClass(left);
                RulerHouseholdCandidateClass rightClass =
                    ResolveCandidateClass(right);
                int leftPriority =
                    RulerHouseholdRules.HouseholdCandidatePriority(
                        leftLineage == lineageId,
                        IsDirectChildOfRuler(left, pSource.king), leftClass);
                int rightPriority =
                    RulerHouseholdRules.HouseholdCandidatePriority(
                        rightLineage == lineageId,
                        IsDirectChildOfRuler(right, pSource.king), rightClass);
                int score = RulerHouseholdRankRules.ConsortScore(
                        HouseholdAttributeScore(right), rightPriority,
                        rightClass == RulerHouseholdCandidateClass.Noble)
                    .CompareTo(RulerHouseholdRankRules.ConsortScore(
                        HouseholdAttributeScore(left), leftPriority,
                        leftClass == RulerHouseholdCandidateClass.Noble));
                if (score != 0) return score;
                int age = SafeAge(left).CompareTo(SafeAge(right));
                return age != 0 ? age : left.data.id.CompareTo(right.data.id);
            });
            return result;
        }

        internal static bool AreRelated(Actor pFirst, Actor pSecond)
        {
            return SafeRelated(pFirst, pSecond);
        }

        private static bool HasLivingMutualSpouse(Actor pRuler)
        {
            Actor spouse = pRuler?.lover;
            return IsLiveActor(spouse) && spouse.lover == pRuler;
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

        private static bool IsLiveRealm(Kingdom pKingdom)
        {
            return pKingdom?.data != null && !pKingdom.isRekt() &&
                   !pKingdom.isNeutral() && pKingdom.isCiv() &&
                    pKingdom.hasKing() && pKingdom.king?.data != null;
        }

        private static bool IsIndependentRealm(Kingdom pKingdom)
        {
            return IsLiveRealm(pKingdom) &&
                   VassalService.GetSuzerain(pKingdom)?.data == null;
        }

        private static bool IsLiveActor(Actor pActor)
        {
            return pActor?.data != null && pActor.isAlive() &&
                   !pActor.isRekt();
        }

        private static bool IsAuthority()
        {
            return !AW3MultiplayerReplicaScope.IsApplying &&
                   !AW3MultiplayerReplicaScope.IsReplicaSession;
        }

        private static Actor FindActor(long pActorId)
        {
            try { return World.world?.units?.get(pActorId); }
            catch { return null; }
        }

        private static string KindCode(RulerHouseholdKind pKind)
        {
            return pKind == RulerHouseholdKind.PrincipalWife
                ? "principal_wife"
                : "consort";
        }

        private static int SafeYear()
        {
            try { return Date.getCurrentYear(); }
            catch { return 0; }
        }

        private static int SafeAge(Actor pActor)
        {
            try { return pActor?.data == null ? -1 : pActor.getAge(); }
            catch { return -1; }
        }

        internal static bool NormalizeImperialRanks(Kingdom pKingdom)
        {
            if (!Ready || pKingdom?.king?.data == null ||
                ResolveRealmTier(pKingdom) !=
                RulerHouseholdRealmTier.Empire ||
                pKingdom.king.isSexFemale()) return false;

            long rulerId = pKingdom.king.data.id;
            var query = new RulerHouseholdQuery(DB);
            IReadOnlyList<RulerHouseholdRecord> records =
                query.ReadActiveForRankNormalization(rulerId);
            IReadOnlyList<RulerHouseholdRankMigrationEntry> normalized =
                RulerHouseholdRankMigrationService.AssignLegacy(
                    records.Select(pRow =>
                        new RulerHouseholdRankMigrationEntry(
                            pRow.RelationshipId, pRow.Kind, pRow.RankCode,
                            pRow.StartYear, pRow.StartTime, pRow.Active)));
            if (!normalized.Any(pRow => pRow.NeedsWrite)) return false;

            using SQLiteTransaction transaction = DB.BeginTransaction();
            double now = LineageService.CurTime();
            for (int i = 0; i < normalized.Count; i++)
            {
                RulerHouseholdRankMigrationEntry row = normalized[i];
                if (!row.NeedsWrite || !row.Closed) continue;
                using var close = new SQLiteCommand(
                    "UPDATE RulerHousehold SET STATUS=1,END_TIME=@time " +
                    "WHERE RELATIONSHIP_ID=@id AND STATUS=0 AND END_TIME<0",
                    DB, transaction);
                close.Parameters.AddWithValue("@time", now);
                close.Parameters.AddWithValue("@id", row.RelationshipId);
                close.ExecuteNonQuery();
            }
            for (int i = 0; i < normalized.Count; i++)
            {
                RulerHouseholdRankMigrationEntry row = normalized[i];
                if (!row.NeedsWrite || row.Closed) continue;
                using var clear = new SQLiteCommand(
                    "UPDATE RulerHousehold SET RANK_CODE='' " +
                    "WHERE RELATIONSHIP_ID=@id AND STATUS=0 AND END_TIME<0",
                    DB, transaction);
                clear.Parameters.AddWithValue("@id", row.RelationshipId);
                clear.ExecuteNonQuery();
            }
            for (int i = 0; i < normalized.Count; i++)
            {
                RulerHouseholdRankMigrationEntry row = normalized[i];
                if (!row.NeedsWrite || row.Closed) continue;
                using var assign = new SQLiteCommand(
                    "UPDATE RulerHousehold SET RANK_CODE=@rank " +
                    "WHERE RELATIONSHIP_ID=@id AND STATUS=0 AND END_TIME<0",
                    DB, transaction);
                assign.Parameters.AddWithValue("@rank", row.RankCode);
                assign.Parameters.AddWithValue("@id", row.RelationshipId);
                if (assign.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException(
                        "household rank normalization lost its active row");
            }
            transaction.Commit();
            return true;
        }

        private static string ResolveStoredRankCode(
            SQLiteTransaction pTransaction, Actor pRuler,
            Kingdom pRecipient, RulerHouseholdKind pKind)
        {
            RulerHouseholdRealmTier tier = ResolveRealmTier(pRecipient);
            if (tier != RulerHouseholdRealmTier.Empire ||
                pRuler.isSexFemale())
                return RulerHouseholdRules.TitleKey(tier, pKind,
                    pRuler.isSexFemale());

            var used = new HashSet<string>(StringComparer.Ordinal);
            using var command = new SQLiteCommand(
                "SELECT RANK_CODE FROM RulerHousehold WHERE " +
                "RULER_ACTOR_ID=@ruler AND STATUS=0 AND END_TIME<0 " +
                "ORDER BY RELATIONSHIP_ID LIMIT 10", DB, pTransaction);
            command.Parameters.AddWithValue("@ruler", pRuler.data.id);
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                string code = Convert.ToString(reader.GetValue(0)) ?? "";
                if (RulerHouseholdRankRules.IsFixedImperialRank(code))
                    used.Add(code);
            }
            return RulerHouseholdRankRules.NextEmptySeat(used,
                pKind == RulerHouseholdKind.PrincipalWife);
        }

        private static int CompareHouseholdCandidates(
            RulerHouseholdOfferCandidate pLeft,
            RulerHouseholdOfferCandidate pRight,
            RulerHouseholdKind pKind)
        {
            int priority;
            if (pKind == RulerHouseholdKind.Consort)
            {
                priority = RulerHouseholdRankRules.ConsortScore(
                        pRight.AttributeScore, pRight.LineagePriority,
                        pRight.CandidateClass ==
                        RulerHouseholdCandidateClass.Noble)
                    .CompareTo(RulerHouseholdRankRules.ConsortScore(
                        pLeft.AttributeScore, pLeft.LineagePriority,
                        pLeft.CandidateClass ==
                        RulerHouseholdCandidateClass.Noble));
            }
            else
            {
                priority = pLeft.LineagePriority.CompareTo(
                    pRight.LineagePriority);
            }
            if (priority != 0) return priority;
            int age = pLeft.Age.CompareTo(pRight.Age);
            return age != 0
                ? age
                : pLeft.ActorId.CompareTo(pRight.ActorId);
        }

        private static int HouseholdAttributeScore(Actor pActor)
        {
            return (int)Math.Round(
                SafeStat(pActor, "intelligence") +
                SafeStat(pActor, "diplomacy") +
                SafeStat(pActor, "stewardship") +
                SafeStat(pActor, "warfare"));
        }

        private static float SafeStat(Actor pActor, string pStat)
        {
            try { return pActor?.stats?[pStat] ?? 0f; }
            catch { return 0f; }
        }
    }
}
