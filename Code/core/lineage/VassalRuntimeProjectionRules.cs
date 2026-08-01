namespace AncientWarfare3.core.lineage
{
    public readonly struct VassalRuntimeProjection
    {
        public VassalRuntimeProjection(int pContractTier,
            long pVassalSuzerainId, long pVassalRelationId,
            long pTributarySuzerainId, long pTributaryRelationId)
        {
            ContractTier = pContractTier;
            VassalSuzerainId = pVassalSuzerainId;
            VassalRelationId = pVassalRelationId;
            TributarySuzerainId = pTributarySuzerainId;
            TributaryRelationId = pTributaryRelationId;
        }

        public int ContractTier { get; }
        public long VassalSuzerainId { get; }
        public long VassalRelationId { get; }
        public long TributarySuzerainId { get; }
        public long TributaryRelationId { get; }
    }

    public static class VassalRuntimeProjectionRules
    {
        public static VassalRuntimeProjection Resolve(long suzerainId,
            long relationId, int contractTier)
        {
            int tier = VassalContractTierRules.NormalizeTier(contractTier);
            if (VassalContractTierRules.CountsAsVassal(tier))
                return new VassalRuntimeProjection(tier, suzerainId,
                    relationId, -1L, -1L);
            return new VassalRuntimeProjection(tier, -1L, -1L,
                suzerainId, relationId);
        }
    }
}
