using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.schools
{
    public sealed class HistoricalSchoolMasterSlot
    {
        internal HistoricalSchoolMasterSlot(string pSchoolId, string pMasterId,
            long pActorId, bool pActive)
        {
            SchoolId = pSchoolId;
            MasterId = pMasterId;
            ActorId = pActorId;
            Active = pActive;
        }

        public string SchoolId { get; }
        public string MasterId { get; }
        public long ActorId { get; internal set; }
        public bool Active { get; internal set; }
    }

    public sealed class HistoricalSchoolActiveMasterSlots
    {
        private readonly Dictionary<string, HistoricalSchoolMasterSlot> _bySchool =
            new Dictionary<string, HistoricalSchoolMasterSlot>(StringComparer.Ordinal);

        public bool IsOccupied(string pSchoolId)
        {
            return !string.IsNullOrWhiteSpace(pSchoolId) &&
                   _bySchool.ContainsKey(pSchoolId);
        }

        public bool TryReserve(string pSchoolId, string pMasterId)
        {
            if (string.IsNullOrWhiteSpace(pSchoolId) ||
                string.IsNullOrWhiteSpace(pMasterId) || _bySchool.ContainsKey(pSchoolId))
                return false;
            _bySchool[pSchoolId] = new HistoricalSchoolMasterSlot(pSchoolId,
                pMasterId, -1L, pActive: false);
            return true;
        }

        public bool TryAttachActor(string pSchoolId, string pMasterId, long pActorId)
        {
            if (pActorId < 0 || !TryGetMatching(pSchoolId, pMasterId,
                    out HistoricalSchoolMasterSlot slot)) return false;
            if (slot.Active) return slot.ActorId == pActorId;
            if (slot.ActorId >= 0 && slot.ActorId != pActorId) return false;
            slot.ActorId = pActorId;
            return true;
        }

        public bool TryActivate(string pSchoolId, string pMasterId, long pActorId)
        {
            if (pActorId < 0 || !TryGetMatching(pSchoolId, pMasterId,
                    out HistoricalSchoolMasterSlot slot)) return false;
            if (slot.ActorId >= 0 && slot.ActorId != pActorId) return false;
            slot.ActorId = pActorId;
            slot.Active = true;
            return true;
        }

        public bool TryRestoreActive(string pSchoolId, string pMasterId, long pActorId)
        {
            if (string.IsNullOrWhiteSpace(pSchoolId) ||
                string.IsNullOrWhiteSpace(pMasterId) || pActorId < 0) return false;
            if (_bySchool.TryGetValue(pSchoolId, out HistoricalSchoolMasterSlot existing))
                return existing.MasterId == pMasterId && existing.ActorId == pActorId &&
                       existing.Active;
            _bySchool[pSchoolId] = new HistoricalSchoolMasterSlot(pSchoolId,
                pMasterId, pActorId, pActive: true);
            return true;
        }

        public bool TryRelease(string pSchoolId, string pMasterId, long pActorId)
        {
            if (!TryGetMatching(pSchoolId, pMasterId,
                    out HistoricalSchoolMasterSlot slot)) return false;
            if (slot.ActorId >= 0 && slot.ActorId != pActorId) return false;
            if (slot.ActorId < 0 && pActorId >= 0) return false;
            return _bySchool.Remove(pSchoolId);
        }

        public bool TryGet(string pSchoolId, out HistoricalSchoolMasterSlot pSlot)
        {
            return _bySchool.TryGetValue(pSchoolId ?? "", out pSlot);
        }

        public void Clear()
        {
            _bySchool.Clear();
        }

        private bool TryGetMatching(string pSchoolId, string pMasterId,
            out HistoricalSchoolMasterSlot pSlot)
        {
            pSlot = null;
            return !string.IsNullOrWhiteSpace(pSchoolId) &&
                   !string.IsNullOrWhiteSpace(pMasterId) &&
                   _bySchool.TryGetValue(pSchoolId, out pSlot) &&
                   pSlot.MasterId == pMasterId;
        }
    }
}
