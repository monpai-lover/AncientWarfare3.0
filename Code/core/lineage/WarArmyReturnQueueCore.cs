using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public sealed class WarArmyReturnQueueOrder
    {
        public long ArmyId { get; internal set; }
        public long KingdomId { get; internal set; }
        public long TargetCityId { get; internal set; }
    }

    public sealed class WarArmyReturnQueueCore
    {
        private readonly Dictionary<long, WarArmyReturnQueueOrder> _orders =
            new Dictionary<long, WarArmyReturnQueueOrder>();
        private readonly Queue<long> _work = new Queue<long>();
        private readonly HashSet<long> _queuedIds = new HashSet<long>();

        public int OrderCount => _orders.Count;
        public int WorkCount => _work.Count;

        public bool Begin(long pArmyId, long pKingdomId, long pTargetCityId)
        {
            if (pArmyId < 0L || pKingdomId < 0L || pTargetCityId < 0L)
                return false;
            if (!_orders.TryGetValue(pArmyId,
                    out WarArmyReturnQueueOrder order))
            {
                order = new WarArmyReturnQueueOrder { ArmyId = pArmyId };
                _orders[pArmyId] = order;
            }
            order.KingdomId = pKingdomId;
            order.TargetCityId = pTargetCityId;
            EnqueueOnce(pArmyId);
            return true;
        }

        public bool UpdateTarget(long pArmyId, long pTargetCityId)
        {
            if (pTargetCityId < 0L ||
                !_orders.TryGetValue(pArmyId,
                    out WarArmyReturnQueueOrder order)) return false;
            order.TargetCityId = pTargetCityId;
            return true;
        }

        public bool TryTake(out WarArmyReturnQueueOrder pOrder)
        {
            pOrder = null;
            if (_work.Count == 0) return false;
            long armyId = _work.Dequeue();
            return _queuedIds.Remove(armyId) &&
                   _orders.TryGetValue(armyId, out pOrder);
        }

        public bool Requeue(long pArmyId)
        {
            return _orders.ContainsKey(pArmyId) && EnqueueOnce(pArmyId);
        }

        public bool Cancel(long pArmyId)
        {
            return pArmyId >= 0L && _orders.Remove(pArmyId);
        }

        public bool Complete(long pArmyId)
        {
            return Cancel(pArmyId);
        }

        public void Clear()
        {
            _orders.Clear();
            _work.Clear();
            _queuedIds.Clear();
        }

        private bool EnqueueOnce(long pArmyId)
        {
            if (!_queuedIds.Add(pArmyId)) return false;
            _work.Enqueue(pArmyId);
            return true;
        }
    }

    public sealed class WarArmyReturnStoredIntent
    {
        public bool Active { get; set; }
        public long ArmyId { get; set; } = -1L;
        public long KingdomId { get; set; } = -1L;
        public long TargetCityId { get; set; } = -1L;
    }

    public sealed class WarArmyReturnRestoreFacts
    {
        public bool ArmyAlive { get; set; }
        public bool ArmyKingdomMatches { get; set; }
        public bool InsideFriendlySafeCity { get; set; }
        public bool HasValidMission { get; set; }
        public bool StoredTargetFriendlySafe { get; set; }
        public long ReplacementTargetCityId { get; set; } = -1L;
    }

    public static class WarArmyReturnPersistenceRules
    {
        public static WarArmyReturnStoredIntent Encode(long armyId,
            long kingdomId, long targetCityId)
        {
            return new WarArmyReturnStoredIntent
            {
                Active = armyId >= 0L && kingdomId >= 0L &&
                         targetCityId >= 0L,
                ArmyId = armyId,
                KingdomId = kingdomId,
                TargetCityId = targetCityId
            };
        }

        public static bool TryRestore(WarArmyReturnStoredIntent pStored,
            WarArmyReturnRestoreFacts pFacts,
            out WarArmyReturnStoredIntent pRestored)
        {
            pRestored = null;
            if (pStored == null || !pStored.Active || pFacts == null ||
                pStored.ArmyId < 0L || pStored.KingdomId < 0L ||
                !pFacts.ArmyAlive || !pFacts.ArmyKingdomMatches ||
                pFacts.InsideFriendlySafeCity || pFacts.HasValidMission)
                return false;
            long targetCityId = pFacts.StoredTargetFriendlySafe
                ? pStored.TargetCityId
                : pFacts.ReplacementTargetCityId;
            if (targetCityId < 0L) return false;
            pRestored = Encode(pStored.ArmyId, pStored.KingdomId,
                targetCityId);
            return pRestored.Active;
        }
    }
}
