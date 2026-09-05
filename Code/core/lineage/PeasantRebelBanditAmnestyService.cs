using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.content.policies;
using AncientWarfare3.core.court;
using AncientWarfare3.core.db;
using AncientWarfare3.core.policy;
using AncientWarfare3.core.presentation;
using AncientWarfare3.core.schools;
using AncientWarfare3.utils;

namespace AncientWarfare3.core.lineage
{
    internal static class PeasantRebelBanditAmnestyService
    {
        private const string Table = "BanditAmnestySettlement";

        private sealed class Settlement
        {
            internal long Id;
            internal long BanditId;
            internal long OriginId;
            internal long LeaderId;
            internal long StrongholdId;
            internal long MotherId;
            internal PeasantRebelBanditAmnestyOffer Offer;
            internal BanditAmnestySettlementPhase Phase;
        }

        private static SQLiteConnection DB =>
            LineageArchiveManager.Instance?.OperatingDB;

        private static bool Ready => DB != null &&
            LineageArchiveManager.Instance.InitializeSuccessful;

        internal static bool TryAmnesty(Kingdom pBandit,
            Kingdom pOfferingKingdom, out string pFailureKey)
        {
            return TryAmnesty(pBandit, pOfferingKingdom,
                new PeasantRebelBanditAmnestyOffer(), out pFailureKey);
        }

        internal static bool TryAmnesty(Kingdom pBandit,
            Kingdom pOfferingKingdom,
            PeasantRebelBanditAmnestyOffer pOffer,
            out string pFailureKey)
        {
            pFailureKey = "aw_bandit_amnesty_unavailable";
            bool bandit = PeasantRebelRouteService.IsBandit(pBandit) ||
                          PeasantRebelGuiyiService.IsGuiyi(pBandit);
            bool stronghold = PeasantRebelBanditStrongholdService.
                HasActiveStronghold(pBandit);
            Kingdom origin = ResolveAmnestyOfferingKingdom(pBandit);
            bool originValid = IsLiveOrigin(origin);
            bool offeringIsOrigin = originValid && origin == pOfferingKingdom;
            bool authoritative = PeasantRebelRouteRules.CanMutateAuthority(
                AW3MultiplayerReplicaScope.IsReplicaSession);
            bool applying = AW3MultiplayerReplicaScope.IsApplying;
            if (!PeasantRebelBanditAmnestyRules.CanAccept(bandit,
                    stronghold, originValid, offeringIsOrigin, authoritative,
                    applying))
            {
                pFailureKey = "aw_bandit_amnesty_" +
                    PeasantRebelBanditAmnestyRules.ResolveFailureKey(
                        bandit, stronghold, originValid, offeringIsOrigin);
                return false;
            }

            Actor leader = pBandit.king;
            City strongholdCity = PeasantRebelBanditStrongholdService.
                ResolveStronghold(pBandit);
            if (leader?.data == null || strongholdCity?.data == null ||
                !PeasantRebelBanditStateStore.TryRead(pBandit,
                    out PeasantRebelBanditStrongholdState strongholdState) ||
                !ValidateOffer(pOfferingKingdom, leader, pOffer,
                    out pFailureKey)) return false;
            Settlement settlement = CreateSettlement(pBandit,
                pOfferingKingdom, leader, strongholdCity, strongholdState,
                pOffer);
            if (settlement == null)
            {
                pFailureKey = "aw_bandit_amnesty_persistence_failed";
                return false;
            }

            if (!EndBanditWars(pBandit))
            {
                MarkFailed(settlement.Id,
                    "aw_bandit_amnesty_war_failed");
                pFailureKey = "aw_bandit_amnesty_war_failed";
                return false;
            }

            UpdatePhase(settlement.Id,
                BanditAmnestySettlementPhase.TerritorialSettlement, "");
            if (!RestoreOrdinaryGovernment(pBandit))
            {
                IncrementRetry(settlement.Id,
                    "aw_bandit_amnesty_settlement_failed");
                pFailureKey = "aw_bandit_amnesty_settlement_failed";
                return false;
            }

            UpdatePhase(settlement.Id,
                BanditAmnestySettlementPhase.RewardPending, "");
            if (!TryApplyReward(settlement, out string rewardFailure))
            {
                IncrementRetry(settlement.Id, rewardFailure);
                ModClass.LogWarning("Bandit amnesty reward deferred: " +
                                    rewardFailure);
                pFailureKey = string.Empty;
                return true;
            }

            RecordSettlementHistory(pBandit, origin, leader,
                strongholdCity, pOffer);
            UpdatePhase(settlement.Id,
                BanditAmnestySettlementPhase.Completed, "");
            pFailureKey = string.Empty;
            return true;
        }

        internal static void ProcessAuthorityCycle()
        {
            if (!Ready || !PeasantRebelRouteRules.CanMutateAuthority(
                    AW3MultiplayerReplicaScope.IsReplicaSession) ||
                AW3MultiplayerReplicaScope.IsApplying) return;
            foreach (Settlement settlement in ReadPending(4))
            {
                if (settlement.Phase == BanditAmnestySettlementPhase.
                        TerritorialSettlement &&
                    !TryResumeTerritorialSettlement(settlement))
                    continue;
                if (!TryApplyReward(settlement, out string failure))
                {
                    IncrementRetry(settlement.Id, failure);
                    continue;
                }
                Kingdom origin = ResolveKingdom(settlement.OriginId);
                Actor leader = ResolveActor(settlement.LeaderId);
                RecordSettlementHistory(null, origin, leader, null,
                    settlement.Offer);
                UpdatePhase(settlement.Id,
                    BanditAmnestySettlementPhase.Completed, "");
            }
        }

        internal static void ProcessAiDecision(Kingdom pBandit)
        {
            if (!Ready || !PeasantRebelRouteRules.CanMutateAuthority(
                    AW3MultiplayerReplicaScope.IsReplicaSession) ||
                AW3MultiplayerReplicaScope.IsApplying ||
                !PeasantRebelRouteService.IsBandit(pBandit) ||
                !PeasantRebelBanditStrongholdService.HasActiveStronghold(
                    pBandit)) return;

            Kingdom origin = PeasantRebelRouteService.ResolveOrigin(pBandit);
            if (!IsLiveOrigin(origin)) return;

            BanditAmnestyAiDecision decision =
                PeasantRebelBanditAmnestyRules.ResolveAiDecision(
                    PeasantRebelRouteService.RealmStrength(pBandit),
                    PeasantRebelRouteService.RealmStrength(origin));
            if (decision == BanditAmnestyAiDecision.Amnesty)
            {
                TryAmnesty(pBandit, origin,
                    new PeasantRebelBanditAmnestyOffer(), out _);
                return;
            }

            if (HasActiveWarPair(pBandit, origin)) return;
            DiplomaticWarDeclarationService.Issue(origin, pBandit,
                WarTerritoryService.GOAL_BANDIT_SUPPRESSION, null,
                WarDecisionService.WAR_BANDIT_SUPPRESSION,
                "bandit_suppression",
                HistoryLocalizationRules.Text("aw_war_goal_bandit_suppression"));
        }

        private static bool TryResumeTerritorialSettlement(
            Settlement pSettlement)
        {
            Kingdom bandit = ResolveKingdom(pSettlement.BanditId);
            if (bandit?.data == null || !EndBanditWars(bandit) ||
                !RestoreOrdinaryGovernment(bandit))
            {
                IncrementRetry(pSettlement.Id,
                    "aw_bandit_amnesty_settlement_failed");
                return false;
            }
            pSettlement.Phase = BanditAmnestySettlementPhase.RewardPending;
            return UpdatePhase(pSettlement.Id,
                BanditAmnestySettlementPhase.RewardPending, "");
        }

        private static bool ValidateOffer(Kingdom pOrigin, Actor pLeader,
            PeasantRebelBanditAmnestyOffer pOffer, out string pFailureKey)
        {
            pFailureKey = "aw_bandit_amnesty_reward_invalid";
            if (pOffer == null) return false;
            switch (pOffer.RewardKind)
            {
                case BanditAmnestyRewardKind.None:
                    return true;
                case BanditAmnestyRewardKind.Office:
                    if (!CourtService.CanPromiseAmnestyOffice(pOrigin,
                            pLeader, pOffer.OfficeId)) return false;
                    return true;
                case BanditAmnestyRewardKind.VirtualTitle:
                    VirtualNobleTitleGrantResult result =
                        VirtualNobleTitleService.ValidateGrant(pOrigin,
                            pOrigin?.king, pLeader, pOffer.TitleText,
                            pAllowForeignTarget: true);
                    if (result != VirtualNobleTitleGrantResult.Success)
                    {
                        pFailureKey = "aw_virtual_title_error_" +
                                      result.ToString().ToLowerInvariant();
                        return false;
                    }
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryApplyReward(Settlement pSettlement,
            out string pFailureKey)
        {
            pFailureKey = "aw_bandit_amnesty_reward_failed";
            Kingdom origin = ResolveKingdom(pSettlement?.OriginId ?? -1L);
            Actor leader = ResolveActor(pSettlement?.LeaderId ?? -1L);
            PeasantRebelBanditAmnestyOffer offer = pSettlement?.Offer;
            if (origin?.data == null || leader?.data == null || offer == null ||
                !EnsureRewardRecipientNaturalized(pSettlement, origin,
                    leader)) return false;
            switch (offer.RewardKind)
            {
                case BanditAmnestyRewardKind.None:
                    return true;
                case BanditAmnestyRewardKind.Office:
                    CourtManualAppointmentResult appointment =
                        CourtService.TryManualAppointment(origin.id,
                            offer.OfficeId, leader.data.id);
                    if (appointment == CourtManualAppointmentResult.Success)
                        return true;
                    pFailureKey = "aw_court_appointment_" +
                                  appointment.ToString().ToLowerInvariant();
                    return false;
                case BanditAmnestyRewardKind.VirtualTitle:
                    VirtualNobleTitleGrantResult title =
                        VirtualNobleTitleService.TryGrant(origin,
                            origin.king, leader, offer.TitleText,
                            offer.Hereditary, out _);
                    if (title == VirtualNobleTitleGrantResult.Success)
                        return true;
                    pFailureKey = "aw_virtual_title_error_" +
                                  title.ToString().ToLowerInvariant();
                    return false;
                default:
                    return false;
            }
        }

        private static bool EnsureRewardRecipientNaturalized(
            Settlement pSettlement, Kingdom origin, Actor leader)
        {
            if (pSettlement == null || origin?.data == null ||
                leader?.data == null || leader.isRekt() ||
                !leader.isAlive()) return false;

            Kingdom bandit = ResolveKingdom(pSettlement.BanditId);
            try
            {
                if (bandit?.data != null && bandit.king == leader &&
                    leader.kingdom == bandit)
                    bandit.removeKing();
            }
            catch { return false; }

            City destination = ResolveNaturalizationCity(
                pSettlement.MotherId, origin);
            bool hasCity = leader.city?.data != null &&
                           !leader.city.isRekt();
            bool alreadyNaturalized =
                PeasantRebelBanditAmnestyRules.
                    IsRewardRecipientNaturalized(
                        leader.kingdom == origin, hasCity,
                        hasCity && leader.city.kingdom == origin);
            if (alreadyNaturalized) return true;

            try
            {
                if (destination == null && leader.city?.kingdom != origin)
                    leader.setCity(null);
                using (FormalAffiliationTransferScope.Open(leader.data.id,
                           origin.id, destination?.data?.id ?? -1L))
                {
                    if (leader.kingdom != origin)
                        leader.joinKingdom(origin);
                    if (destination?.data != null &&
                        leader.city != destination)
                        leader.joinCity(destination);
                }
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Bandit amnesty naturalization failed: " +
                                    error.Message);
                return false;
            }

            // Join operations update the engine pointers first. Persist the
            // historical home only after both pointers have been accepted, so
            // a failed transfer cannot leave a false domestic affiliation.
            if (destination?.data != null &&
                !HistoricalAffiliationService.SynchronizeHomeForNaturalization(
                    leader, origin, destination)) return false;
            hasCity = leader.city?.data != null && !leader.city.isRekt();
            return PeasantRebelBanditAmnestyRules.
                IsRewardRecipientNaturalized(
                    leader.kingdom == origin, hasCity,
                    hasCity && leader.city.kingdom == origin);
        }

        private static City ResolveNaturalizationCity(long pMotherCityId,
            Kingdom pOrigin)
        {
            City mother = ResolveCity(pMotherCityId);
            if (mother?.data != null && !mother.isRekt() &&
                mother.kingdom == pOrigin) return mother;
            City capital = pOrigin?.capital;
            if (capital?.data != null && !capital.isRekt() &&
                capital.kingdom == pOrigin) return capital;
            try
            {
                foreach (City city in pOrigin?.getCities() ??
                         Array.Empty<City>())
                    if (city?.data != null && !city.isRekt() &&
                        city.kingdom == pOrigin)
                        return city;
            }
            catch { }
            return null;
        }

        private static Settlement CreateSettlement(Kingdom pBandit,
            Kingdom pOrigin, Actor pLeader, City pStronghold,
            PeasantRebelBanditStrongholdState pState,
            PeasantRebelBanditAmnestyOffer pOffer)
        {
            if (!Ready) return null;
            try
            {
                long id = TableIdAllocator.Next(DB, Table,
                    "SETTLEMENT_ID");
                double now = LineageService.CurTime();
                DB.Insert(Table,
                    ColumnVal.Create("SETTLEMENT_ID", id),
                    ColumnVal.Create("BANDIT_KINGDOM_ID", pBandit.id),
                    ColumnVal.Create("ORIGIN_KINGDOM_ID", pOrigin.id),
                    ColumnVal.Create("LEADER_ACTOR_ID", pLeader.data.id),
                    ColumnVal.Create("STRONGHOLD_CITY_ID",
                        pStronghold.id),
                    ColumnVal.Create("MOTHER_CITY_ID", pState.MotherCityId),
                    ColumnVal.Create("REWARD_KIND",
                        pOffer.RewardKind.ToString()),
                    ColumnVal.Create("OFFICE_ID", pOffer.OfficeId ?? ""),
                    ColumnVal.Create("TITLE_TEXT", pOffer.TitleText ?? ""),
                    ColumnVal.Create("HEREDITARY",
                        pOffer.Hereditary ? 1 : 0),
                    ColumnVal.Create("PHASE",
                        BanditAmnestySettlementPhase.Prepared.ToString()),
                    ColumnVal.Create("RETRY_COUNT", 0),
                    ColumnVal.Create("FAILURE_KEY", ""),
                    ColumnVal.Create("CREATED_YEAR", Date.getCurrentYear()),
                    ColumnVal.Create("CREATED_TIME", now),
                    ColumnVal.Create("UPDATED_TIME", now));
                return new Settlement
                {
                    Id = id,
                    BanditId = pBandit.id,
                    OriginId = pOrigin.id,
                    LeaderId = pLeader.data.id,
                    StrongholdId = pStronghold.id,
                    MotherId = pState.MotherCityId,
                    Offer = CloneOffer(pOffer),
                    Phase = BanditAmnestySettlementPhase.Prepared
                };
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Bandit amnesty prepare failed: " +
                                    error.Message);
                return null;
            }
        }

        private static bool UpdatePhase(long pSettlementId,
            BanditAmnestySettlementPhase pPhase, string pFailureKey)
        {
            if (!Ready || pSettlementId < 0L) return false;
            try
            {
                DB.UpdateValue(Table,
                    new List<SimpleColumnConstraint>
                    {
                        SimpleColumnConstraint.CreateEq("SETTLEMENT_ID",
                            pSettlementId)
                    },
                    ColumnVal.Create("PHASE", pPhase.ToString()),
                    ColumnVal.Create("FAILURE_KEY", pFailureKey ?? ""),
                    ColumnVal.Create("UPDATED_TIME",
                        LineageService.CurTime()));
                return true;
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Bandit amnesty phase update failed: " +
                                    error.Message);
                return false;
            }
        }

        private static void MarkFailed(long pSettlementId,
            string pFailureKey)
        {
            UpdatePhase(pSettlementId,
                BanditAmnestySettlementPhase.Failed, pFailureKey);
        }

        private static void IncrementRetry(long pSettlementId,
            string pFailureKey)
        {
            if (!Ready || pSettlementId < 0L) return;
            try
            {
                using SQLiteCommand command = new SQLiteCommand(DB);
                command.CommandText = "UPDATE " + Table +
                    " SET RETRY_COUNT=RETRY_COUNT+1,FAILURE_KEY=@failure," +
                    "UPDATED_TIME=@time WHERE SETTLEMENT_ID=@id";
                command.Parameters.AddWithValue("@failure",
                    pFailureKey ?? "");
                command.Parameters.AddWithValue("@time",
                    LineageService.CurTime());
                command.Parameters.AddWithValue("@id", pSettlementId);
                command.ExecuteNonQuery();
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Bandit amnesty retry update failed: " +
                                    error.Message);
            }
        }

        private static List<Settlement> ReadPending(int pLimit)
        {
            var result = new List<Settlement>();
            if (!Ready || pLimit <= 0) return result;
            try
            {
                using SQLiteCommand command = new SQLiteCommand(DB);
                command.CommandText = "SELECT SETTLEMENT_ID," +
                    "BANDIT_KINGDOM_ID,ORIGIN_KINGDOM_ID,LEADER_ACTOR_ID," +
                    "STRONGHOLD_CITY_ID,MOTHER_CITY_ID,REWARD_KIND," +
                    "OFFICE_ID,TITLE_TEXT,HEREDITARY,PHASE FROM " + Table +
                    " WHERE PHASE=@territory OR PHASE=@reward" +
                    " ORDER BY UPDATED_TIME,SETTLEMENT_ID LIMIT @limit";
                command.Parameters.AddWithValue("@territory",
                    BanditAmnestySettlementPhase.TerritorialSettlement.
                        ToString());
                command.Parameters.AddWithValue("@reward",
                    BanditAmnestySettlementPhase.RewardPending.ToString());
                command.Parameters.AddWithValue("@limit", pLimit);
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    Enum.TryParse(reader.GetString(6), true,
                        out BanditAmnestyRewardKind rewardKind);
                    Enum.TryParse(reader.GetString(10), true,
                        out BanditAmnestySettlementPhase phase);
                    result.Add(new Settlement
                    {
                        Id = reader.GetInt64(0),
                        BanditId = reader.GetInt64(1),
                        OriginId = reader.GetInt64(2),
                        LeaderId = reader.GetInt64(3),
                        StrongholdId = reader.GetInt64(4),
                        MotherId = reader.GetInt64(5),
                        Offer = new PeasantRebelBanditAmnestyOffer
                        {
                            RewardKind = rewardKind,
                            OfficeId = reader.IsDBNull(7) ? "" :
                                reader.GetString(7),
                            TitleText = reader.IsDBNull(8) ? "" :
                                reader.GetString(8),
                            Hereditary = !reader.IsDBNull(9) &&
                                         reader.GetInt32(9) != 0
                        },
                        Phase = phase
                    });
                }
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Bandit amnesty recovery read failed: " +
                                    error.Message);
            }
            return result;
        }

        private static PeasantRebelBanditAmnestyOffer CloneOffer(
            PeasantRebelBanditAmnestyOffer pOffer)
        {
            return new PeasantRebelBanditAmnestyOffer
            {
                RewardKind = pOffer?.RewardKind ??
                    BanditAmnestyRewardKind.None,
                OfficeId = pOffer?.OfficeId ?? "",
                TitleText = pOffer?.TitleText ?? "",
                Hereditary = pOffer?.Hereditary ?? true
            };
        }

        private static void RecordSettlementHistory(Kingdom pBandit,
            Kingdom pOrigin, Actor pLeader, City pStronghold,
            PeasantRebelBanditAmnestyOffer pOffer)
        {
            if (pOrigin?.data == null) return;
            if (pBandit?.data != null)
                HistoryWriter.RecordKingdom(pBandit,
                    KingdomEvent.MANDATE_REBELLION,
                    HistoryText.Kingdom(pBandit) +
                    HistoryLocalizationRules.H("aw_hist_bandit_amnestied"),
                    HistoryTarget.Kingdom(pOrigin));
            HistoryWriter.RecordKingdom(pOrigin,
                KingdomEvent.MANDATE_REBELLION,
                HistoryText.Kingdom(pOrigin) +
                HistoryLocalizationRules.H(
                    "aw_hist_bandit_amnesty_granted") +
                (pBandit?.data != null
                    ? HistoryText.Kingdom(pBandit)
                    : HistoryText.Actor(pLeader)) +
                HistoryLocalizationRules.H(RewardHistoryKey(pOffer)),
                pLeader?.data != null
                    ? HistoryTarget.Actor(pLeader)
                    : HistoryTarget.Kingdom(pOrigin));
        }

        private static string RewardHistoryKey(
            PeasantRebelBanditAmnestyOffer pOffer)
        {
            switch (pOffer?.RewardKind ?? BanditAmnestyRewardKind.None)
            {
                case BanditAmnestyRewardKind.Office:
                    return "aw_hist_bandit_amnesty_reward_office";
                case BanditAmnestyRewardKind.VirtualTitle:
                    return "aw_hist_bandit_amnesty_reward_title";
                default:
                    return "aw_hist_bandit_amnesty_reward_none";
            }
        }

        private static Kingdom ResolveKingdom(long pKingdomId)
        {
            if (pKingdomId <= 0L) return null;
            try { return World.world?.kingdoms?.get(pKingdomId); }
            catch { return null; }
        }

        private static Actor ResolveActor(long pActorId)
        {
            if (pActorId < 0L) return null;
            try { return World.world?.units?.get(pActorId); }
            catch { return null; }
        }

        private static City ResolveCity(long pCityId)
        {
            if (pCityId <= 0L) return null;
            try { return World.world?.cities?.get(pCityId); }
            catch { return null; }
        }

        private static bool RestoreOrdinaryGovernment(Kingdom pBandit)
        {
            if (pBandit?.data == null || pBandit.isRekt()) return false;
            if (!PeasantRebelBanditStrongholdService.
                    DestroyForOrdinaryGovernment(pBandit)) return false;

            pBandit.data.set(LineageKeys.MANDATE_REBEL, false);
            pBandit.data.set(LineageKeys.MANDATE_REBEL_ORIGIN_KINGDOM_ID,
                -1L);
            pBandit.data.set(LineageKeys.MANDATE_REBEL_BUFF_UNTIL, 0);
            pBandit.data.get(LineageKeys.MANDATE_MAP_MARKER_KIND,
                out string marker, "");
            if (marker == "rebel_claimant")
                pBandit.data.set(LineageKeys.MANDATE_MAP_MARKER_KIND, "");
            MandateService.NormalizeMapMarkerAfterRebelSettlement(pBandit);

            foreach (Actor unit in pBandit.getUnits())
            {
                if (unit?.data == null) continue;
                unit.data.set(LineageKeys.MANDATE_REBEL, false);
                unit.data.set(LineageKeys.MANDATE_REBEL_LEADER, false);
                if (unit.hasTrait("rebel")) unit.removeTrait("rebel");
            }

            pBandit.data.set(LineageKeys.MANDATE_REBEL_ROUTE, "");
            // 招安后必须换成正式国号,不能继续顶着土匪名。
            //
            // 原来这里直接把匪号词根当国名用:
            //     data.get(MANDATE_REBEL_NAME_ROOT, out root);
            //     TryApplyRouteName(pBandit, root.Trim());
            // 而 word_libraries/default/土匪名根.txt 里全是**双字**词根
            // (赤眉、黄巾、绿林、梁山…),NormalizeRoot 也只剥「义军」「贼」两个
            // 后缀、不动词根本身。于是土匪→义军→正规国家走完整条路之后,国名
            // 仍然是「赤眉」这种双字匪号,而不是汉式的单字国号。
            //
            // 正式国号取自 XiaPreQinKingdomNameRules —— 和其他所有国家同一个
            // 池子(StateNameService 走的也是它),先秦国名,绝大多数是单字。
            // 选取用 StateNameRules.SelectFirstAvailable:它按稳定种子起步并
            // 跳过已被占用的名字,所以同一个王国每次算出来一样,也不会和现存
            // 国家撞名。
            if (!PeasantRebelStateNameService.ApplyCanonical(pBandit))
                return false;
            string targetClass = PeasantRebelBanditAmnestyRules.
                ResolveSettlementClass(true);
            if (!KingdomPolicyService.ApplyClassStateDirect(
                    pBandit, targetClass)) return false;

            PeasantRebelRouteService.RemoveRuntime(pBandit);
            RulerAppellationService.RefreshLivingProjection(pBandit);
            KingdomRenameProjectionService.Refresh(pBandit);
            PeasantRebelAppearanceService.OnProjectionChanged(pBandit);
            return true;
        }

        private static bool EndBanditWars(Kingdom pBandit)
        {
            if (pBandit?.data == null || World.world?.wars == null)
                return false;
            var wars = new List<War>();
            try
            {
                foreach (War war in pBandit.getWars())
                    if (war?.data != null && !war.hasEnded()) wars.Add(war);
            }
            catch { return false; }
            try
            {
                for (int i = 0; i < wars.Count; i++)
                    World.world.wars.endWar(wars[i], WarWinner.Peace);
                return true;
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Bandit amnesty could not end wars: " +
                                    error.Message);
                return false;
            }
        }

        private static bool IsLiveOrigin(Kingdom pOrigin)
        {
            try
            {
                return pOrigin?.data != null && !pOrigin.isRekt() &&
                       pOrigin.isAlive() && !pOrigin.isNeutral() &&
                       pOrigin.isCiv();
            }
            catch { return false; }
        }

        private static Kingdom ResolveAmnestyOfferingKingdom(Kingdom pBandit)
        {
            Kingdom origin = PeasantRebelRouteService.ResolveOrigin(pBandit);
            if (origin?.data != null) return origin;
            if (!PeasantRebelBanditStateStore.TryRead(pBandit,
                    out PeasantRebelBanditStrongholdState state) ||
                !string.Equals(state.RouteSubtype,
                    PeasantRebelGuiyiRules.RouteSubtype,
                    StringComparison.Ordinal) ||
                state.GuiyiOccupierKingdomId <= 0L) return null;
            return ResolveKingdom(state.GuiyiOccupierKingdomId);
        }

        private static bool HasActiveWarPair(Kingdom pLeft,
            Kingdom pRight)
        {
            if (pLeft?.data == null || pRight?.data == null) return false;
            try
            {
                foreach (War war in pLeft.getWars())
                {
                    if (war?.data == null || war.hasEnded()) continue;
                    Kingdom attacker = war.getMainAttacker();
                    Kingdom defender = war.getMainDefender();
                    if (attacker == pLeft && defender == pRight ||
                        attacker == pRight && defender == pLeft)
                        return true;
                }
            }
            catch { return true; }
            return false;
        }
    }
}
