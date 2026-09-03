using System;
using AncientWarfare3.content.figures;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.lineage
{
    public sealed class HistoricalFigureCardDeploymentRequest
    {
        public HistoricalFigureCardDeploymentRequest(string pCardId,
            string pDrawId, string pDeploymentId, City pTargetCity)
        {
            CardId = pCardId ?? "";
            DrawId = pDrawId ?? "";
            DeploymentId = pDeploymentId ?? "";
            TargetCity = pTargetCity;
        }

        public string CardId { get; }
        public string DrawId { get; }
        public string DeploymentId { get; }
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

        public static bool IsActive => _scopeDepth > 0;

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
            Kingdom oldKingdom = city?.kingdom;
            Actor oldLeader = city?.leader;
            City oldCapital = oldKingdom?.capital;
            bool kingdomCreated = false;
            try
            {
                HistoricalFigureCardDefinition definition =
                    HistoricalFigureCardCatalog.Get(pRequest.CardId);
                if (definition == null)
                    return HistoricalFigureCardDeploymentResult.Failure("card_missing");
                HistoricalFigureCardCollectionStore collection =
                    HistoricalFigureCardRuntimeService.Collection;
                bool cardOwned = collection.GetOwnedCount(definition.CardId) > 0;

                var facts = new HistoricalFigureCardDeploymentFacts(
                    city?.data != null, city != null && !city.isRekt(), true,
                    oldKingdom?.data != null && !oldKingdom.isRekt(),
                    LineageArchiveManager.Instance?.InitializeSuccessful == true,
                    false, definition.HistoricalKingdomName, cardOwned);
                if (!HistoricalFigureCardDeploymentRules.CanDeploy(facts))
                    return HistoricalFigureCardDeploymentResult.Failure(
                        "deployment_precondition_failed");
                if (World.world?.units == null || city.getTile() == null)
                    return HistoricalFigureCardDeploymentResult.Failure("world_unavailable");

                ActorAsset asset = AssetManager.actor_library.get("Xia") ??
                    city.getActorAsset();
                if (asset == null || string.IsNullOrEmpty(asset.id))
                    return HistoricalFigureCardDeploymentResult.Failure(
                        "actor_asset_missing");

                using (OpenScope())
                {
                    actor = World.world.units.createNewUnit(asset.id,
                        city.getTile(), pMiracleSpawn: false, 0f,
                        FindXiaSubspecies(city), null,
                        pSpawnWithItems: true, pAdultAge: true);
                    if (actor?.data == null || actor.isRekt())
                        throw new InvalidOperationException("actor_creation_failed");
                    HistoricalFigureCardIdentityService.Apply(actor, definition,
                        pRequest.DrawId, pRequest.DeploymentId);
                    actor.joinCity(city);
                    newKingdom = city.makeOwnKingdom(actor,
                        pRebellion: true, pFellApart: false);
                    kingdomCreated = newKingdom?.data != null;
                    if (!kingdomCreated)
                        throw new InvalidOperationException("kingdom_creation_failed");
                    newKingdom.setCapital(city);
                    if (newKingdom.king != actor) newKingdom.setKing(actor);
                    newKingdom.setName(definition.HistoricalKingdomName,
                        pTrack: false);
                }

                if (newKingdom.capital != city || newKingdom.king != actor)
                    throw new InvalidOperationException("kingdom_projection_failed");
                CommitLineage(actor, definition);
                if (!HistoricalAncestorService.EnsureCardParentage(actor,
                        definition, pRequest.DeploymentId))
                    throw new InvalidOperationException("parentage_commit_failed");
                RecordHistory(actor, newKingdom, city, definition,
                    pRequest.DeploymentId);
                return HistoricalFigureCardDeploymentResult.Success(actor,
                    newKingdom, city);
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Historical card deployment failed: " +
                    error.Message);
                Rollback(actor, newKingdom, oldKingdom, oldCapital, oldLeader,
                    city, kingdomCreated);
                return HistoricalFigureCardDeploymentResult.Failure(error.Message);
            }
            finally
            {
                HistoricalFigureCardDeploymentRules.End(pRequest.DeploymentId);
            }
        }

        private static void CommitLineage(Actor pActor,
            HistoricalFigureCardDefinition pDefinition)
        {
            long lineageId = LineageIdAllocator.NextLineageId();
            long shiId = LineageIdAllocator.NextShiId();
            if (lineageId < 0L || shiId < 0L)
                throw new InvalidOperationException("lineage_id_unavailable");
            LineageService.InsertLineageGroup(lineageId,
                pDefinition.FamilyName, pActor);
            LineageService.InsertShiBranch(shiId, lineageId,
                pDefinition.ClanName, pActor, ShiSourceType.SPECIAL_FIGURE);
            pActor.data.set(LineageKeys.LINEAGE_ID, lineageId);
            pActor.data.set(LineageKeys.SHI_ID, shiId);
            pActor.data.set(LineageKeys.NAME_INTEGRATED, true);
            LineageService.OnActorPromoted(pActor, NobleTrigger.Figure);
        }

        private static void RecordHistory(Actor pActor, Kingdom pKingdom,
            City pCity, HistoricalFigureCardDefinition pDefinition,
            string pDeploymentId)
        {
            string name = pActor.getName();
            HistoryText text = HistoryText.PlainText(name + " / " +
                pDefinition.HistoricalKingdomName + " / " + pDeploymentId);
            HistoryWriter.RecordPerson(pActor.data.id, pKingdom, name,
                "card_deployed", text, ChronicleCategory.HONOR,
                HistoryTarget.City(pCity));
            HistoryWriter.RecordPerson(pActor.data.id, pKingdom, name,
                "card_king", text, ChronicleCategory.HONOR,
                HistoryTarget.Kingdom(pKingdom));
            HistoryWriter.RecordKingdom(pKingdom, "card_kingdom_founded",
                text, HistoryTarget.Actor(pActor));
            HistoryWriter.RecordCity(pCity, pKingdom, "card_deployed", text,
                HistoryTarget.Actor(pActor));
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

        private static void Rollback(Actor pActor, Kingdom pNewKingdom,
            Kingdom pOldKingdom, City pOldCapital, Actor pOldLeader,
            City pCity, bool pKingdomCreated)
        {
            try
            {
                if (pCity?.data != null && pOldKingdom?.data != null &&
                    pCity.kingdom != pOldKingdom)
                    pCity.setKingdom(pOldKingdom);
            }
            catch { }
            try
            {
                if (pOldCapital?.data != null && pOldKingdom?.data != null)
                    pOldKingdom.setCapital(pOldCapital);
                if (pOldLeader?.data != null && pCity?.data != null)
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
            }
        }
    }
}
