using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal sealed class ArmyNativeRouteLock
    {
        private readonly List<int> _tileIds = new List<int>();

        internal bool IsLocked { get; private set; }
        internal bool NeedsNativeResume { get; private set; }
        internal bool Completed { get; private set; }
        internal long ArmyId { get; private set; } = -1L;
        internal long TargetCityId { get; private set; } = -1L;
        internal long CaptainId { get; private set; } = -1L;
        internal int EndpointTileId { get; private set; } = -1;
        internal int Cursor { get; private set; }
        internal int Generation { get; private set; }
        internal IReadOnlyList<int> TileIds => _tileIds;

        internal bool Matches(long pArmyId, long pTargetCityId,
            int pEndpointTileId, long pCaptainId)
        {
            return IsLocked && ArmyId == pArmyId &&
                   TargetCityId == pTargetCityId &&
                   EndpointTileId == pEndpointTileId &&
                   CaptainId == pCaptainId;
        }

        internal void Capture(long pArmyId, long pTargetCityId,
            int pEndpointTileId, long pCaptainId,
            IReadOnlyList<int> pTileIds, int pCursor)
        {
            _tileIds.Clear();
            if (pTileIds != null)
            {
                for (int i = 0; i < pTileIds.Count; i++)
                {
                    int tileId = pTileIds[i];
                    if (tileId >= 0) _tileIds.Add(tileId);
                }
            }
            if (_tileIds.Count == 0)
            {
                Invalidate();
                return;
            }
            ArmyId = pArmyId;
            TargetCityId = pTargetCityId;
            EndpointTileId = pEndpointTileId;
            CaptainId = pCaptainId;
            Cursor = Math.Max(0, Math.Min(_tileIds.Count, pCursor));
            Completed = Cursor >= _tileIds.Count;
            NeedsNativeResume = false;
            IsLocked = true;
            Generation++;
        }

        internal void AdvanceTo(int pCursor)
        {
            if (!IsLocked) return;
            Cursor = Math.Max(Cursor,
                Math.Max(0, Math.Min(_tileIds.Count, pCursor)));
            Completed = Cursor >= _tileIds.Count;
        }

        internal void MarkMovementInterrupted()
        {
            if (IsLocked && !Completed) NeedsNativeResume = true;
        }

        internal void ObserveNativeResume()
        {
            if (IsLocked) NeedsNativeResume = false;
        }

        internal void Invalidate()
        {
            IsLocked = false;
            NeedsNativeResume = false;
            Completed = false;
            ArmyId = -1L;
            TargetCityId = -1L;
            CaptainId = -1L;
            EndpointTileId = -1;
            Cursor = 0;
            _tileIds.Clear();
        }
    }
}
