using System;
using System.Collections.Generic;
using System.Data.SQLite;
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
            return PrepareOffer(pSource, pRecipient,
                FindActor(pCandidateActorId), pKind);
        }

        internal static RulerHouseholdOfferPreview PrepareOffer(
            Kingdom pSource, Kingdom pRecipient, Actor pCandidate,
            RulerHouseholdKind pKind)
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
                pSource == pRecipient)
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
            if (!IsEligibleCandidate(pCandidate, pSource, out string reason))
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
                ResolveRulingLineageId(pSource), pRequestedLimit: 1).Count > 0;
        }

        internal static RulerHouseholdOfferCandidatePool
            BuildOfferCandidatePool(Kingdom pSource, Kingdom pRecipient,
                RulerHouseholdKind pKind)
        {
            var pool = new RulerHouseholdOfferCandidatePool();
            if (!Ready || !IsLiveRealm(pSource) ||
                !IsLiveRealm(pRecipient) || pSource == pRecipient)
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
                pSource.id, rulingLineageId, MaximumPlayerCandidateActors);
            for (int i = 0; i < ids.Count; i++)
            {
                Actor candidate = FindActor(ids[i]);
                RulerHouseholdOfferPreview preview = PrepareOffer(
                    pSource, pRecipient, candidate, pKind);
                if (!preview.Available) continue;
                ActorArchiveTableItem archive =
                    LineageArchiveReader.ReadRow(ids[i]);
                pool.Candidates.Add(new RulerHouseholdOfferCandidate
                {
                    ActorId = ids[i],
                    Actor = candidate,
                    ActorName = candidate?.getName() ??
                                archive?.display_name ?? "",
                    Age = SafeAge(candidate),
                    MemberOfRulingLineage = rulingLineageId >= 0L &&
                        archive?.lineage_id == rulingLineageId,
                    DirectChildOfRuler = IsDirectChildOfRuler(candidate,
                        pSource.king),
                    LineageLabel = AncestryDisplayRules.FormatLineageLabel(
                        archive?.city_name, archive?.clan_name)
                });
            }
            pool.Candidates.Sort((left, right) =>
            {
                int priority = RulerHouseholdRules.HouseholdCandidatePriority(
                        left.MemberOfRulingLineage,
                        left.DirectChildOfRuler).CompareTo(
                    RulerHouseholdRules.HouseholdCandidatePriority(
                            right.MemberOfRulingLineage,
                            right.DirectChildOfRuler));
                if (priority != 0) return priority;
                int age = left.Age.CompareTo(right.Age);
                return age != 0 ? age : left.ActorId.CompareTo(right.ActorId);
            });
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
                pSource.id, rulingLineageId, MaximumAiCandidateActors);
            var candidates = new List<RulerHouseholdOfferCandidate>(
                candidateIds.Count);
            for (int index = 0; index < candidateIds.Count; index++)
            {
                Actor actor = FindActor(candidateIds[index]);
                if (actor?.data == null) continue;
                actor.data.get(LineageKeys.LINEAGE_ID,
                    out long actorLineageId, -1L);
                candidates.Add(new RulerHouseholdOfferCandidate
                {
                    ActorId = actor.data.id,
                    Actor = actor,
                    Age = SafeAge(actor),
                    MemberOfRulingLineage = rulingLineageId >= 0L &&
                                             actorLineageId == rulingLineageId,
                    DirectChildOfRuler = IsDirectChildOfRuler(actor,
                        pSource.king)
                });
            }
            candidates.Sort((left, right) =>
            {
                int priority = RulerHouseholdRules.HouseholdCandidatePriority(
                        left.MemberOfRulingLineage,
                        left.DirectChildOfRuler).CompareTo(
                    RulerHouseholdRules.HouseholdCandidatePriority(
                        right.MemberOfRulingLineage,
                        right.DirectChildOfRuler));
                if (priority != 0) return priority;
                int age = left.Age.CompareTo(right.Age);
                return age != 0 ? age : left.ActorId.CompareTo(right.ActorId);
            });
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
                long modifierId = TableIdAllocator.Next(DB,
                    DiplomaticRelationModifierTableItem.GetTableName(),
                    "MODIFIER_ID");
                int year = SafeYear();
                using SQLiteTransaction transaction = DB.BeginTransaction();
                InsertRelationship(transaction, relationshipId, ruler,
                    partner, pSource, pRecipient, pKind, year,
                    pSourceProposalId);
                if (!DiplomaticRelationModifierService.Upsert(transaction,
                        modifierId, pSource.id, pRecipient.id,
                        "ruler_household", relationshipId,
                        RulerHouseholdRules.RelationshipBonus(pKind), year,
                        int.MaxValue))
                    throw new InvalidOperationException(
                        "household relation modifier write failed");
                transaction.Commit();

                if (!MovePartner(partner, pRecipient, capital))
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
                        sameRealm);
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

                int capacity = RulerHouseholdRules.ConsortCapacity(
                    ResolveRealmTier(pActor.kingdom));
                IReadOnlyList<RulerHouseholdRecord> ruled =
                    query.ReadActiveByRuler(pActor.data.id, capacity);
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
            int pYear, long pProposalId)
        {
            using var command = new SQLiteCommand(DB)
            {
                Transaction = transaction,
                CommandText =
                    "INSERT INTO RulerHousehold " +
                    "(RELATIONSHIP_ID,RULER_ACTOR_ID,PARTNER_ACTOR_ID," +
                    "SOURCE_KINGDOM_ID,RECIPIENT_KINGDOM_ID," +
                    "RELATIONSHIP_KIND,RANK_CODE,START_YEAR,START_TIME," +
                    "END_TIME,STATUS,SOURCE_PROPOSAL_ID) VALUES " +
                    "(@id,@ruler,@partner,@source,@recipient,@kind,@rank," +
                    "@year,@time,-1,0,@proposal)"
            };
            command.Parameters.AddWithValue("@id", pRelationshipId);
            command.Parameters.AddWithValue("@ruler", pRuler.data.id);
            command.Parameters.AddWithValue("@partner", pPartner.data.id);
            command.Parameters.AddWithValue("@source", pSource.id);
            command.Parameters.AddWithValue("@recipient", pRecipient.id);
            command.Parameters.AddWithValue("@kind", KindCode(pKind));
            command.Parameters.AddWithValue("@rank",
                RulerHouseholdRules.TitleKey(ResolveRealmTier(pRecipient),
                    pKind, pRuler.isSexFemale()));
            command.Parameters.AddWithValue("@year", pYear);
            command.Parameters.AddWithValue("@time", LineageService.CurTime());
            command.Parameters.AddWithValue("@proposal", pProposalId);
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException(
                    "household relationship insert failed");
        }

        private static bool MovePartner(Actor partner, Kingdom pRecipient,
            City pCapital)
        {
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
                return partner.kingdom == pRecipient &&
                       partner.city == pCapital;
            }
            catch
            {
                return false;
            }
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
            Kingdom pSource, out string pReason)
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
            if (SlaveService.IsSlave(pCandidate))
            {
                pReason = "candidate_is_slave";
                return false;
            }
            if (!NobleHeirPregnancyService.IsEligibleNoble(pCandidate))
            {
                pReason = "candidate_not_noble";
                return false;
            }
            if (!HasNobleLineage(pCandidate))
            {
                pReason = "candidate_not_noble_lineage";
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
            RulerHouseholdKind pKind)
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
            string text = partnerName + localized + rulerName;
            HistoryWriter.RecordPerson(pPartner.data.id, pRecipient,
                partnerName, PersonEvent.ROYAL_MARRIAGE, text,
                ChronicleCategory.BOND, HistoryTarget.Actor(pRuler));
            HistoryWriter.RecordPerson(pRuler.data.id, pRecipient,
                rulerName, PersonEvent.ROYAL_MARRIAGE, text,
                ChronicleCategory.BOND, HistoryTarget.Actor(pPartner));
            HistoryWriter.RecordKingdom(pSource,
                KingdomEvent.ROYAL_MARRIAGE, text,
                HistoryTarget.Kingdom(pRecipient));
            HistoryWriter.RecordKingdom(pRecipient,
                KingdomEvent.ROYAL_MARRIAGE, text,
                HistoryTarget.Kingdom(pSource));
        }

        private static bool IsEligibleRuler(Actor pRuler, Kingdom pRealm)
        {
            return IsLiveActor(pRuler) && pRealm?.king == pRuler &&
                   pRuler.kingdom == pRealm && pRuler.isSexMale() &&
                   pRuler.isAdult() && pRuler.isBreedingAge() &&
                   !RepublicGovernmentService.IsRepublic(pRealm);
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
    }
}
