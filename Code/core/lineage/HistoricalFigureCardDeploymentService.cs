using System;
using System.Collections.Generic;
using AncientWarfare3.content;
using AncientWarfare3.content.figures;
using AncientWarfare3.core.court;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.lineage
{
    public sealed class HistoricalFigureCardDeploymentRequest
    {
        public HistoricalFigureCardDeploymentRequest(string pCardId,
            string pDrawId, string pDeploymentId, City pTargetCity)
            : this(pCardId, pDrawId, pDeploymentId,
                pTargetCity?.getTile(), pTargetCity)
        {
        }

        public HistoricalFigureCardDeploymentRequest(string pCardId,
            string pDrawId, string pDeploymentId, WorldTile pTargetTile,
            City pTargetCity = null)
        {
            CardId = pCardId ?? "";
            DrawId = pDrawId ?? "";
            DeploymentId = pDeploymentId ?? "";
            TargetTile = pTargetTile;
            TargetCity = pTargetCity;
        }

        public string CardId { get; }
        public string DrawId { get; }
        public string DeploymentId { get; }
        public WorldTile TargetTile { get; }
        public City TargetCity { get; }
    }

    public sealed class HistoricalFigureCardDeploymentResult
    {
        private HistoricalFigureCardDeploymentResult(bool pSucceeded,
            string pError, long pActorId, long pKingdomId, long pCityId,
            string pKingdomName)
        {
            Succeeded = pSucceeded;
            Error = pError ?? "";
            ActorId = pActorId;
            KingdomId = pKingdomId;
            CityId = pCityId;
            KingdomName = pKingdomName ?? "";
        }

        public bool Succeeded { get; }
        public string Error { get; }
        public long ActorId { get; }
        public long KingdomId { get; }
        public long CityId { get; }
        public string KingdomName { get; }

        internal static HistoricalFigureCardDeploymentResult Success(
            Actor pActor, Kingdom pKingdom, City pCity)
        {
            return new HistoricalFigureCardDeploymentResult(true, "",
                pActor?.data?.id ?? -1L, pKingdom?.data?.id ?? -1L,
                pCity?.data?.id ?? -1L, pKingdom?.name ?? "");
        }

        internal static HistoricalFigureCardDeploymentResult Failure(
            string pError)
        {
            return new HistoricalFigureCardDeploymentResult(false, pError,
                -1L, -1L, -1L, "");
        }
    }

    public static class HistoricalFigureCardDeploymentService
    {
        [ThreadStatic] private static int _scopeDepth;
        [ThreadStatic] private static string _pendingKingdomName;

        public static bool IsActive => _scopeDepth > 0;

        /// <summary>
        ///     本次降临要用的历史国号。<c>AW_HistoricalFigureCardPatch</c> 在
        ///     <c>Kingdom.newCivKingdom</c> 的后置里读它,赶在
        ///     <c>WorldLog.logNewKingdom</c> 之前把随机国名替换掉。
        /// </summary>
        internal static string PendingKingdomName => _pendingKingdomName;

        public static HistoricalFigureCardDeploymentResult TryDeploy(
            HistoricalFigureCardDeploymentRequest pRequest)
        {
            if (pRequest == null)
                return HistoricalFigureCardDeploymentResult.Failure("request_missing");
            if (!HistoricalFigureCardDeploymentRules.TryBegin(pRequest.DeploymentId))
                return HistoricalFigureCardDeploymentResult.Failure(
                    "deployment_already_active");

            Actor actor = null;
            Kingdom newKingdom = null;
            City city = pRequest.TargetCity;
            WorldTile targetTile = pRequest.TargetTile ?? city?.getTile();
            if (city == null)
                city = targetTile?.zone_city;
            Kingdom oldKingdom = city?.kingdom;
            Actor oldLeader = city?.leader;
            City oldCapital = oldKingdom?.capital;
            bool kingdomCreated = false;
            bool cityCreated = false;
            Army militaryArmy = null;
            bool militaryArmyCreated = false;
            bool militaryGeneralPromoted = false;
            bool militaryActorWasWarrior = false;
            List<Actor> militaryAddedSoldiers = new List<Actor>();
            List<Actor> militaryConvertedSoldiers = new List<Actor>();
            long lineageId = -1L;
            long shiId = -1L;
            try
            {
                HistoricalFigureCardDefinition definition =
                    HistoricalFigureCardCatalog.Get(pRequest.CardId);
                if (definition == null)
                    return HistoricalFigureCardDeploymentResult.Failure("card_missing");
                HistoricalFigureCardCollectionStore collection =
                    HistoricalFigureCardRuntimeService.Collection;
                bool cardOwned = collection.GetOwnedCount(definition.CardId) > 0;
                bool hasLivingKingdom = oldKingdom?.data != null &&
                    !oldKingdom.isRekt() && oldKingdom.isCiv() &&
                    !oldKingdom.isNeutral();

                var facts = new HistoricalFigureCardDeploymentFacts(
                    city?.data != null, city != null && !city.isRekt() &&
                    city.isAlive(), true,
                    hasLivingKingdom,
                    LineageArchiveManager.Instance?.InitializeSuccessful == true,
                    false, definition.HistoricalKingdomName, cardOwned,
                    targetTile?.data != null,
                    IsBuildableUnownedTile(targetTile));
                if (HistoricalFigureCardRoleRules.IsMinister(definition) &&
                    !HistoricalFigureCardRoleRules.CanDeployMinister(
                        city?.data != null && city.isAlive() && !city.isRekt(),
                        hasLivingKingdom))
                    return HistoricalFigureCardDeploymentResult.Failure(
                        "minister_requires_existing_city");
                if (HistoricalFigureCardRoleRules.IsMilitaryGeneral(
                        definition) &&
                    !HistoricalFigureCardDeploymentRules.
                        CanDeployMilitaryGeneral(
                            city?.data != null && city.isAlive() &&
                            !city.isRekt(), hasLivingKingdom,
                            oldKingdom?.isCiv() == true))
                    return HistoricalFigureCardDeploymentResult.Failure(
                        "military_general_requires_civil_kingdom");
                if (!HistoricalFigureCardDeploymentRules.CanDeploy(facts))
                    return HistoricalFigureCardDeploymentResult.Failure(
                        "deployment_precondition_failed");
                if (World.world?.units == null || World.world.kingdoms == null ||
                    World.world.cities == null || targetTile == null)
                    return HistoricalFigureCardDeploymentResult.Failure("world_unavailable");

                ActorAsset asset = ResolveDeploymentAsset(city, definition);
                if (asset == null || string.IsNullOrEmpty(asset.id))
                    return HistoricalFigureCardDeploymentResult.Failure(
                        "actor_asset_missing");

                using (OpenScope())
                {
                    // 建国前先把历史国号挂上。AW_HistoricalFigureCardPatch 的
                    // Kingdom.newCivKingdom 后置会读它,赶在 logNewKingdom 之前
                    // 覆盖掉原版生成的随机国名 —— 否则随机名会先落进世界日志和
                    // 编年史,再被我们改掉,留下一条污染记录。
                    _pendingKingdomName = definition.HistoricalKingdomName;
                    actor = World.world.units.createNewUnit(asset.id,
                        targetTile, pMiracleSpawn: city == null, 0f,
                        FindXiaSubspecies(city), null,
                        pSpawnWithItems: true, pAdultAge: true);
                    if (actor?.data == null || actor.isRekt())
                        throw new InvalidOperationException("actor_creation_failed");
                    HistoricalFigureCardIdentityService.Apply(actor, definition,
                        pRequest.DrawId, pRequest.DeploymentId);
                    // 谱系 id 必须在任何 setKing 之前落到 actor.data 上。
                    // makeOwnKingdom / makeNewCivKingdom / setKing 都会触发
                    // AW_PromotionPatch.SetKing_Postfix → OnActorPromoted →
                    // EnsureLineageForNoble;后者以 HasCompleteLineageData
                    // (LINEAGE_ID + SHI_ID + CLAN_NAME)判定是否已有谱系,
                    // 见空就 LineageNamePool.RandomSurname() 随机改姓。
                    // 之前 CommitLineage 排在建国之后,卡片人物于是被连改两次姓
                    // (每次称王一次),预设的姓与国号一并被随机氏名冲掉。
                    // 这里只做预留(分配 id + 写 actor.data);氏支入库紧跟其后,
                    // 晋升留到建国之后,好让 ResolveOriginIds 能取到国/城。
                    ReserveLineageIdentity(actor, out lineageId, out shiId);
                    // 氏支必须在 setKing 之前入库:setKing 会触发
                    // OnKingChanged → TryOnKingChanged,后者靠 GetShiBranchInfo
                    // 判断朝代承继,查不到氏支就建不起朝代分段(编年史段头
                    // 因此回落成「早期」)。
                    CommitLineageRecords(actor, definition, lineageId, shiId);
                    if (HistoricalFigureCardRoleRules.IsMinister(definition))
                    {
                        if (city?.data == null || oldKingdom?.data == null)
                            throw new InvalidOperationException(
                                "minister_requires_existing_city");
                        actor.joinCity(city);
                        actor.spawnOn(targetTile);
                        newKingdom = oldKingdom;
                        if (HistoricalFigureCardRoleRules.IsMilitaryGeneral(
                                definition))
                        {
                            militaryActorWasWarrior = actor.isWarrior();
                            EnsureMilitaryGeneralWarrior(actor, city);
                            if (!GeneralService.PromoteToGeneral(actor))
                                throw new InvalidOperationException(
                                    "military_general_promotion_failed");
                            militaryGeneralPromoted = true;
                            militaryArmy = ResolveMilitaryArmy(city,
                                oldKingdom);
                            if (militaryArmy == null)
                            {
                                using (MilitaryRecruitmentScope.Open(
                                           MilitaryRecruitmentKind.StandingArmy))
                                {
                                    militaryArmy = World.world.armies.
                                        newArmy(actor, city);
                                }
                                militaryArmyCreated = true;
                            }
                            if (militaryArmy?.data == null)
                                throw new InvalidOperationException(
                                    "military_general_army_creation_failed");
                            AWArmyService.AddToArmy(actor, militaryArmy);
                            AWArmyService.SetCaptainIfChanged(militaryArmy,
                                actor);
                            if (actor.army != militaryArmy ||
                                militaryArmy.getCaptain() != actor)
                                throw new InvalidOperationException(
                                    "military_general_captain_assignment_failed");
                            AddInitialMilitarySoldiers(city, oldKingdom,
                                militaryArmy, actor, militaryAddedSoldiers,
                                militaryConvertedSoldiers);
                        }
                    }
                    else if (city?.data != null)
                    {
                        actor.joinCity(city);
                        newKingdom = city.makeOwnKingdom(actor,
                            pRebellion: true, pFellApart: false);
                        kingdomCreated = newKingdom?.data != null;
                        if (!kingdomCreated)
                            throw new InvalidOperationException(
                                "kingdom_creation_failed");
                        actor.spawnOn(targetTile);
                    }
                    else
                    {
                        newKingdom = World.world.kingdoms.makeNewCivKingdom(
                            actor, pID: null, pLog: true);
                        kingdomCreated = newKingdom?.data != null;
                        if (!kingdomCreated)
                            throw new InvalidOperationException(
                                "kingdom_creation_failed");
                        city = World.world.cities.newCity(newKingdom,
                            targetTile.zone, actor);
                        cityCreated = city?.data != null;
                        if (!cityCreated)
                            throw new InvalidOperationException(
                                "city_creation_failed");
                        city.setUnitMetas(actor);
                        city.newCityEvent(actor);
                        actor.joinCity(city);
                        actor.spawnOn(targetTile);
                        newKingdom.setCityMetas(city);
                    }
                    if (HistoricalFigureCardRoleRules.IsMonarch(definition))
                    {
                        newKingdom.setCapital(city);
                        if (newKingdom.king != actor) newKingdom.setKing(actor);
                        newKingdom.setName(definition.HistoricalKingdomName,
                            pTrack: false);
                    }
                }

                if (HistoricalFigureCardRoleRules.IsMonarch(definition) &&
                    (newKingdom.capital != city || newKingdom.king != actor))
                    throw new InvalidOperationException("kingdom_projection_failed");
                CommitLineagePromotion(actor);
                if (!HistoricalAncestorService.EnsureCardParentage(actor,
                        definition, pRequest.DeploymentId))
                    throw new InvalidOperationException("parentage_commit_failed");
                RecordHistory(actor, newKingdom, city, definition,
                    pRequest.DeploymentId);
                if (HistoricalFigureCardRoleRules.IsMinister(definition))
                {
                    OfficerCandidateCatalog.GetOrBuild(newKingdom);
                    OfficerCandidateCatalog.EnsurePresent(newKingdom, actor);
                }
                if (!collection.TryConsume(definition.CardId,
                        DateTime.UtcNow.ToString("O")))
                    throw new InvalidOperationException("card_consume_failed");
                return HistoricalFigureCardDeploymentResult.Success(actor,
                    newKingdom, city);
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Historical card deployment failed: " +
                    error.Message);
                Rollback(actor, newKingdom, oldKingdom, oldCapital, oldLeader,
                    city, kingdomCreated, cityCreated, militaryArmy,
                    militaryArmyCreated, militaryGeneralPromoted,
                    militaryActorWasWarrior, militaryAddedSoldiers,
                    militaryConvertedSoldiers);
                return HistoricalFigureCardDeploymentResult.Failure(error.Message);
            }
            finally
            {
                HistoricalFigureCardDeploymentRules.End(pRequest.DeploymentId);
            }
        }

        /// <summary>
        ///     只分配谱系 id 并写回 actor.data,不入库。
        ///
        ///     <para>
        ///     目的是让称王链路上的 <c>EnsureLineageForNoble</c> 立刻看到
        ///     「已有完整谱系」(LINEAGE_ID ≥ 0 且 SHI_ID ≥ 0 且 CLAN_NAME 非空,
        ///     见 <c>LineageService.HasCompleteLineageData</c>),从而跳过
        ///     <c>LineageNamePool.RandomSurname()</c> 那条随机改姓分支。
        ///     姓/氏字段本身已由 <c>HistoricalFigureCardIdentityService.Apply</c>
        ///     按卡面预设写好。
        ///     </para>
        /// </summary>
        private static void ReserveLineageIdentity(Actor pActor,
            out long pLineageId, out long pShiId)
        {
            pLineageId = LineageIdAllocator.NextLineageId();
            pShiId = LineageIdAllocator.NextShiId();
            if (pLineageId < 0L || pShiId < 0L)
                throw new InvalidOperationException("lineage_id_unavailable");
            pActor.data.set(LineageKeys.LINEAGE_ID, pLineageId);
            pActor.data.set(LineageKeys.SHI_ID, pShiId);
            pActor.data.set(LineageKeys.NAME_INTEGRATED, true);
        }

        /// <summary>
        ///     氏支入库。必须排在建国之前 —— <c>setKing</c> 会触发
        ///     <c>ChronicleEvents.OnKingChanged</c> → <c>DynastyRecordWriter.TryOnKingChanged</c>,
        ///     后者用 <c>LineageQuery.GetShiBranchInfo(shiId)</c> 判断朝代承继;
        ///     此刻氏支若还没入库,朝代分段就建立不起来。
        /// </summary>
        private static void CommitLineageRecords(Actor pActor,
            HistoricalFigureCardDefinition pDefinition, long pLineageId,
            long pShiId)
        {
            LineageService.InsertLineageGroup(pLineageId,
                pDefinition.FamilyName, pActor);
            LineageService.InsertShiBranch(pShiId, pLineageId,
                pDefinition.ClanName, pActor, ShiSourceType.SPECIAL_FIGURE);
        }

        /// <summary>
        ///     贵族晋升。留在建国之后 —— <c>OnActorPromoted</c> 里的
        ///     <c>ResolveOriginIds</c> 要取得所属国与城。
        /// </summary>
        private static void CommitLineagePromotion(Actor pActor)
        {
            LineageService.OnActorPromoted(pActor, NobleTrigger.Figure);
        }

        private static void RecordHistory(Actor pActor, Kingdom pKingdom,
            City pCity, HistoricalFigureCardDefinition pDefinition,
            string pDeploymentId)
        {
            string name = pActor.getName();
            if (HistoricalFigureCardRoleRules.IsMinister(pDefinition))
            {
                string historyKey = HistoricalFigureCardRoleRules.
                    IsMilitaryGeneral(pDefinition)
                    ? "aw_hist_card_military_deployed"
                    : "aw_hist_card_minister_deployed";
                HistoryText ministerActorText = HistoryText.Actor(pActor, name);
                HistoryText ministerKingdomText = HistoryText.Kingdom(pKingdom,
                    pKingdom?.name ?? pDefinition.HistoricalKingdomName);
                HistoryText appointed = ministerActorText +
                    HistoryLocalizationRules.H(historyKey) +
                    ministerKingdomText;
                HistoryWriter.RecordPerson(pActor.data.id, pKingdom, name,
                    "card_minister_deployed", appointed, ChronicleCategory.HONOR,
                    HistoryTarget.City(pCity));
                HistoryWriter.RecordKingdom(pKingdom, historyKey,
                    appointed, HistoryTarget.Actor(pActor));
                HistoryWriter.RecordCity(pCity, pKingdom, historyKey,
                    appointed, HistoryTarget.Actor(pActor));
                KingdomArchiveWriter.Upsert(pKingdom);
                return;
            }
            // 正文以前是 "名 / 国号 / 部署GUID",编年史里直接显示成一串
            // 十六进制乱码。DeploymentId 只是幂等键,不该出现在文本里。
            HistoryText actorText = HistoryText.Actor(pActor, name);
            HistoryText kingdomText = HistoryText.Kingdom(pKingdom,
                pDefinition.HistoricalKingdomName);
            HistoryText deployed = actorText +
                HistoryLocalizationRules.H("aw_hist_card_deployed") +
                kingdomText;
            HistoryText founded = actorText +
                HistoryLocalizationRules.H("aw_hist_card_kingdom_founded") +
                kingdomText;
            HistoryWriter.RecordPerson(pActor.data.id, pKingdom, name,
                "card_deployed", deployed, ChronicleCategory.HONOR,
                HistoryTarget.City(pCity));
            HistoryWriter.RecordPerson(pActor.data.id, pKingdom, name,
                "card_king", founded, ChronicleCategory.HONOR,
                HistoryTarget.Kingdom(pKingdom));
            // 开国那条 RULE_CHANGE 与 KingdomReign 行都由 setKing 触发的
            // ChronicleEvents.OnKingChanged 负责(它同时写 DynastyPeriod 和
            // 国号绑定,是完整的即位链路)。这里只补卡片专属的降世记述,
            // 不再自己写 RULE_CHANGE / EnsureOpenReign,否则会重复开段。
            HistoryWriter.RecordCity(pCity, pKingdom, "card_deployed",
                deployed, HistoryTarget.Actor(pActor));
            KingdomArchiveWriter.Upsert(pKingdom);
        }

        private static Subspecies FindXiaSubspecies(City pCity)
        {
            try
            {
                foreach (Actor resident in pCity.units)
                    if (LineageService.IsXia(resident) && resident.subspecies != null &&
                        !resident.subspecies.isRekt()) return resident.subspecies;
            }
            catch { }
            return null;
        }

        private static bool IsBuildableUnownedTile(WorldTile pTile)
        {
            return pTile?.data != null && pTile.zone != null &&
                pTile.zone_city == null &&
                pTile.Type?.ground == true && !pTile.Type.liquid &&
                !pTile.Type.lava && !pTile.Type.block && !pTile.hasBuilding();
        }

        /// <summary>
        ///     君主卡一律用夏人 asset,大臣卡沿用所在城市的种族。
        ///
        ///     <para>
        ///     君主卡建国走 <c>makeOwnKingdom</c>/<c>makeNewCivKingdom</c>,原版
        ///     <c>Kingdom.newCivKingdom</c> 会写
        ///     <c>data.original_actor_asset = pActor.asset.id</c>。之前接管已有城市时
        ///     取的是该城原住民的 asset(常是 human),于是新王国的
        ///     <c>original_actor_asset</c> 不是夏人 —— <c>LineageService.IsXiaKingdom</c>
        ///     因此为假,而 <c>DynastyRecordWriter.TryOnKingChanged</c> 与
        ///     <c>ReignRecordWriter.OpenReign</c> 开头都以它做守卫,双双短路返回。
        ///     结果是 DynastyPeriod 与 KingdomReign 都没有行:编年史段头拿不到氏名
        ///     回落成「早期」,年号行拿不到君主名只剩一个干巴巴的干支,
        ///     国号也停留在原版生成的随机名(截图里的「房」)。
        ///     </para>
        ///
        ///     <para>
        ///     大臣卡不建国、只是入朝或从军,保持融入当地种族更合理,不动。
        ///     </para>
        /// </summary>
        private static ActorAsset ResolveDeploymentAsset(City pCity,
            HistoricalFigureCardDefinition pDefinition)
        {
            if (HistoricalFigureCardRoleRules.IsMonarch(pDefinition))
                return ResolveSpawnXiaAsset() ?? pCity?.getActorAsset();
            return pCity?.getActorAsset() ?? ResolveSpawnXiaAsset();
        }

        private static ActorAsset ResolveSpawnXiaAsset()
        {
            GodPower spawnPower = AssetManager.powers?.get(
                GodPowerLibrary.SPAWN_XIA);
            string assetId = spawnPower?.actor_asset_id ?? XiaRace.ID;
            return AssetManager.actor_library.get(assetId) ??
                AssetManager.actor_library.get(XiaRace.ID) ?? XiaRace.asset;
        }

        private static void Rollback(Actor pActor, Kingdom pNewKingdom,
            Kingdom pOldKingdom, City pOldCapital, Actor pOldLeader,
            City pCity, bool pKingdomCreated, bool pCityCreated,
            Army pMilitaryArmy, bool pMilitaryArmyCreated,
            bool pMilitaryGeneralPromoted, bool pMilitaryActorWasWarrior,
            List<Actor> pMilitaryAddedSoldiers,
            List<Actor> pMilitaryConvertedSoldiers)
        {
            RollbackMilitaryState(pActor, pMilitaryArmy,
                pMilitaryArmyCreated, pMilitaryGeneralPromoted,
                pMilitaryActorWasWarrior, pMilitaryAddedSoldiers,
                pMilitaryConvertedSoldiers, pCity, pOldKingdom);
            try
            {
                if (pCityCreated && pCity?.data != null &&
                    World.world?.cities?.get(pCity.id) != null)
                    World.world.cities.removeObject(pCity);
                else if (pCity?.data != null && pOldKingdom?.data != null &&
                    pCity.kingdom != pOldKingdom)
                    pCity.setKingdom(pOldKingdom);
            }
            catch { }
            try
            {
                if (!pCityCreated && pOldCapital?.data != null &&
                    pOldKingdom?.data != null)
                    pOldKingdom.setCapital(pOldCapital);
                if (!pCityCreated && pOldLeader?.data != null &&
                    pCity?.data != null)
                    pCity.setLeader(pOldLeader, pNew: true);
            }
            catch { }
            try
            {
                if (pNewKingdom?.data != null && pKingdomCreated &&
                    World.world?.kingdoms?.get(pNewKingdom.id) != null)
                    World.world.kingdoms.removeObject(pNewKingdom);
            }
            catch { }
            try
            {
                if (pActor?.data != null && !pActor.isRekt())
                    ActionLibrary.removeUnit(pActor);
            }
            catch { }
        }

        private static IDisposable OpenScope()
        {
            _scopeDepth++;
            return new ScopeToken();
        }

        private sealed class ScopeToken : IDisposable
        {
            private bool _disposed;
            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                if (_scopeDepth > 0) _scopeDepth--;
                // 国号覆盖只在建国这一段有效,出了作用域必须清掉,
                // 免得影响后续任何一次普通建国。
                if (_scopeDepth == 0) _pendingKingdomName = null;
            }
        }

        private static void EnsureMilitaryGeneralWarrior(Actor pActor,
            City pCity)
        {
            if (pActor?.data == null || pCity?.data == null)
                throw new InvalidOperationException(
                    "military_general_city_missing");
            if (pActor.isWarrior()) return;
            using (MilitaryRecruitmentScope.Open(
                       MilitaryRecruitmentKind.StandingArmy))
            {
                if (!pCity.checkCanMakeWarrior(pActor))
                    throw new InvalidOperationException(
                        "military_general_warrior_conversion_failed");
                pCity.makeWarrior(pActor);
            }
            if (!pActor.isWarrior())
                throw new InvalidOperationException(
                    "military_general_warrior_conversion_failed");
        }

        private static Army ResolveMilitaryArmy(City pCity,
            Kingdom pKingdom)
        {
            Army army = null;
            try
            {
                if (pCity?.hasArmy() == true) army = pCity.getArmy();
            }
            catch { army = null; }
            if (army?.data == null || !army.isAlive() ||
                AWArmyService.IsSpecialArmy(army) ||
                AWArmyService.GetIntendedKingdom(army) != pKingdom)
                return null;
            Actor captain = null;
            try { captain = army.getCaptain(); }
            catch { }
            if (captain?.data != null && captain.isAlive() &&
                !captain.isRekt()) return null;
            return army;
        }

        private static int AddInitialMilitarySoldiers(City pCity,
            Kingdom pKingdom, Army pArmy, Actor pCaptain,
            List<Actor> pAddedSoldiers, List<Actor> pConvertedSoldiers)
        {
            if (pCity?.data == null || pKingdom?.data == null ||
                pArmy?.data == null || pCaptain?.data == null) return 0;
            int added = 0;
            const int targetAdditionalSoldiers = 5;
            List<Actor> residents;
            try { residents = new List<Actor>(pCity.units); }
            catch { return 0; }
            for (int i = 0; i < residents.Count &&
                 added < targetAdditionalSoldiers; i++)
            {
                Actor candidate = residents[i];
                if (candidate?.data == null || candidate == pCaptain ||
                    candidate.kingdom != pKingdom || candidate.isRekt() ||
                    !candidate.isAlive() || candidate.isKing() ||
                    candidate.isCityLeader() || GeneralService.IsGeneral(candidate) ||
                    candidate.army?.data != null) continue;
                try
                {
                    bool wasWarrior = candidate.isWarrior();
                    if (!wasWarrior)
                    {
                        using (MilitaryRecruitmentScope.Open(
                                   MilitaryRecruitmentKind.StandingArmy))
                        {
                            if (!pCity.checkCanMakeWarrior(candidate)) continue;
                            pCity.makeWarrior(candidate);
                        }
                        if (candidate.isWarrior() && !wasWarrior)
                            pConvertedSoldiers?.Add(candidate);
                    }
                    if (!candidate.isWarrior()) continue;
                    AWArmyService.AddToArmy(candidate, pArmy);
                    if (candidate.army == pArmy)
                    {
                        pAddedSoldiers?.Add(candidate);
                        added++;
                    }
                }
                catch { }
            }
            return added;
        }

        private static void RollbackMilitaryState(Actor pActor,
            Army pArmy, bool pArmyCreated, bool pGeneralPromoted,
            bool pActorWasWarrior, List<Actor> pAddedSoldiers,
            List<Actor> pConvertedSoldiers, City pCity, Kingdom pKingdom)
        {
            if (pAddedSoldiers != null)
            {
                for (int i = 0; i < pAddedSoldiers.Count; i++)
                {
                    Actor soldier = pAddedSoldiers[i];
                    try
                    {
                        if (soldier?.army == pArmy) soldier.removeFromArmy();
                    }
                    catch { }
                }
            }
            if (pArmy?.data != null && pActor?.data != null)
            {
                try
                {
                    using (ArmyCaptainDisposalScope.Open(pArmy))
                    {
                        if (pArmy.getCaptain() == pActor)
                            pArmy.setCaptain(null);
                        if (pActor.army == pArmy) pActor.removeFromArmy();
                    }
                }
                catch
                {
                    try { pActor.setArmy(null); } catch { }
                }
                if (pArmyCreated)
                    ArmyInvalidCleanupQueue.ScheduleShell(pArmy, pCity,
                        pKingdom);
            }
            if (pGeneralPromoted)
                GeneralService.RetireForCardDeployment(pActor);
            if (pConvertedSoldiers != null)
            {
                for (int i = 0; i < pConvertedSoldiers.Count; i++)
                {
                    Actor soldier = pConvertedSoldiers[i];
                    try
                    {
                        if (soldier?.data != null && soldier.isWarrior())
                            soldier.stopBeingWarrior();
                    }
                    catch { }
                }
            }
            if (pActor?.data != null && !pActorWasWarrior &&
                pActor.isWarrior())
            {
                try { pActor.stopBeingWarrior(); } catch { }
            }
        }
    }
}
