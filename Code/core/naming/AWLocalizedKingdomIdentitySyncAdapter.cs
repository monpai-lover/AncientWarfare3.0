using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.naming
{
    internal static class AWLocalizedKingdomIdentitySyncAdapter
    {
        internal static AWLocalizedNameIdentitySnapshot SelectAuthority(
            AWLocalizedNameIdentitySnapshot original,
            AWLocalizedNameIdentitySnapshot rival)
        {
            return original ?? rival;
        }

        internal static int Synchronize(
            AWLocalizedNameIdentitySnapshot pAuthority,
            IReadOnlyList<long> pTargetIds,
            Action<long, AWLocalizedNameIdentitySnapshot> pApplyAndEnqueue)
        {
            if (pAuthority == null || pTargetIds == null ||
                pApplyAndEnqueue == null) return 0;
            var synchronizedIds = new HashSet<long>();
            int writes = 0;
            for (int i = 0; i < pTargetIds.Count; i++)
            {
                long targetId = pTargetIds[i];
                if (targetId < 0L || !synchronizedIds.Add(targetId))
                    continue;
                pApplyAndEnqueue(targetId, Clone(pAuthority));
                writes++;
            }
            return writes;
        }

        private static AWLocalizedNameIdentitySnapshot Clone(
            AWLocalizedNameIdentitySnapshot pIdentity)
        {
            return new AWLocalizedNameIdentitySnapshot(
                pIdentity.NativeName, pIdentity.ChineseName,
                pIdentity.GivenName, pIdentity.FamilyComponent,
                pIdentity.GeneratorId, pIdentity.CultureId,
                pIdentity.SchemaVersion);
        }
    }
}
