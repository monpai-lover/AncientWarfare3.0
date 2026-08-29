using AncientWarfare3.content.figures;
using AncientWarfare3.content.schools;
using AncientWarfare3.core.db;
using AncientWarfare3.core.schools;

namespace AncientWarfare3.core.lineage
{
    /// <summary>
    ///     旧存档修档:把已经存在的历史人物拉到「无引擎双亲 + 史载合成双亲」这个
    ///     不变量上。
    ///
    ///     为什么需要:今天的降临路径不会给历史人物留下引擎双亲
    ///     (`Actor.newCreature` 只在 `createNewUnit` 里被调,那条路造的是无双亲的
    ///     成年单位;`makeBaby` 出口处 `isBaby()` 已挡掉)。但
    ///     `HistoricalFigureService.TrySpawnOn` 的注释还留着一个 `applyParentsMeta`
    ///     钩点 —— 那个钩子现在代码里已经没有了,而在它存在的版本下,历史人物是会
    ///     拿到某对平民父母的 `parent_id_1/2` 与 FamilyEdge 边的。所以老档可能带着
    ///     错误双亲,需要一次性抹掉。
    ///
    ///     整个流程幂等(见 HistoricalAncestorRules.IsAlreadyApplied),已经就位的
    ///     人物一条 SQL 都不会发,所以每次读档无脑跑一遍是安全的。
    /// </summary>
    internal static class HistoricalAncestorRepairService
    {
        internal static void Run()
        {
            int figures = RepairFigures();
            int masters = RepairMasters();
            if (figures == 0 && masters == 0) return;
            ModClass.LogInfo("historical ancestors repaired: figures=" +
                figures + " masters=" + masters);
        }

        /// <summary>按 registry 槽位直查,不扫 actor 列表(最多 91 次 id 查找)。</summary>
        private static int RepairFigures()
        {
            if (!FigureStateStore.IsReady) return 0;
            int repaired = 0;
            for (int index = 0; index < HistoricalFigureDef.Count; index++)
            {
                HistoricalFigureDef definition =
                    HistoricalFigureDef.Get(index);
                if (definition == null) continue;
                long actorId = FigureStateStore.GetActorId(index);
                if (actorId < 0L) continue;
                Actor actor = ResolveActor(actorId);
                if (actor?.data == null) continue;
                if (HistoricalAncestorService.EnsureFigureParentage(actor,
                        definition.RegistryIndex, definition.Id))
                    repaired++;
            }
            return repaired;
        }

        /// <summary>
        ///     宗师没有 id 索引(身份只记在 actor data 的 SCHOOL_MASTER_ID 上),
        ///     所以这里扫一遍 actor 列表 —— 读档一次性开销,与管线里
        ///     RulerAppellationService.RebuildLivingCache 之类同级。
        /// </summary>
        private static int RepairMasters()
        {
            ActorManager manager = World.world?.units;
            if (manager == null) return 0;
            manager.checkContainer();
            manager.prepareArray();
            Actor[] actors = manager.getSimpleArray();
            if (actors == null) return 0;
            int count = manager.Count;
            int repaired = 0;
            for (int index = 0; index < count && index < actors.Length; index++)
            {
                Actor actor = actors[index];
                if (actor?.data == null) continue;
                // 先按 data 键筛,绝大多数 actor 在这里就出局,不查库。
                if (!HistoricalSchoolDescentService.IsCanonicalMaster(actor))
                    continue;
                HistoricalSchoolMasterDefinition definition =
                    HistoricalSchoolDescentService.DefinitionFor(actor);
                if (definition == null) continue;
                if (HistoricalAncestorService.EnsureMasterParentage(actor,
                        definition.RegistryIndex, definition.CanonicalName))
                    repaired++;
            }
            return repaired;
        }

        private static Actor ResolveActor(long pActorId)
        {
            try { return World.world?.units?.get(pActorId); }
            catch { return null; }
        }
    }
}
