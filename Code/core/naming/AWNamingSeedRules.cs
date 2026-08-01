namespace AncientWarfare3.core.naming
{
    public static class AWNamingSeedRules
    {
        public static long Combine(long pObjectId, long pCultureId,
            string pGeneratorId, int pSchemaVersion)
        {
            unchecked
            {
                ulong value = 14695981039346656037UL;
                MixByte(ref value, 1);
                MixUInt64(ref value, (ulong)pObjectId);
                MixByte(ref value, 2);
                MixUInt64(ref value, (ulong)pCultureId);
                MixByte(ref value, 3);
                MixString(ref value, pGeneratorId ?? string.Empty);
                MixByte(ref value, 4);
                MixUInt64(ref value, (uint)pSchemaVersion);
                value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
                value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
                return (long)(value ^ (value >> 31));
            }
        }

        private static void MixString(ref ulong pHash, string pValue)
        {
            MixUInt64(ref pHash, (uint)pValue.Length);
            foreach (char ch in pValue)
            {
                MixByte(ref pHash, (byte)ch);
                MixByte(ref pHash, (byte)(ch >> 8));
            }
        }

        private static void MixUInt64(ref ulong pHash, ulong pValue)
        {
            for (int shift = 0; shift < 64; shift += 8)
                MixByte(ref pHash, (byte)(pValue >> shift));
        }

        private static void MixByte(ref ulong pHash, byte pValue)
        {
            pHash = (pHash ^ pValue) * 1099511628211UL;
        }
    }
}
