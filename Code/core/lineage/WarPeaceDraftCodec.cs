using System;
using Newtonsoft.Json;

namespace AncientWarfare3.core.lineage
{
    internal static class WarPeaceDraftCodec
    {
        private const int SchemaVersion = 2;
        private const int MinimumSupportedVersion = 1;
        private const int MaximumPayloadLength = 8192;

        private sealed class Envelope
        {
            public int Version { get; set; }
            public WarPeaceSettlementDraft Draft { get; set; }
        }

        public static string Serialize(WarPeaceSettlementDraft pDraft)
        {
            if (pDraft == null) throw new ArgumentNullException(nameof(pDraft));
            if (pDraft.Terms.Count >
                WarPeaceSettlementValidationRules.MaximumTerms)
                throw new ArgumentException("Too many peace terms.",
                    nameof(pDraft));
            string payload = JsonConvert.SerializeObject(new Envelope
            {
                Version = SchemaVersion,
                Draft = pDraft
            }, Formatting.None);
            if (payload.Length > MaximumPayloadLength)
                throw new ArgumentException("Peace draft is too large.",
                    nameof(pDraft));
            return payload;
        }

        public static bool TryDeserialize(string pPayload,
            out WarPeaceSettlementDraft pDraft, out string pReason)
        {
            pDraft = null;
            pReason = "invalid_peace_draft";
            if (string.IsNullOrWhiteSpace(pPayload) ||
                pPayload.Length > MaximumPayloadLength) return false;
            try
            {
                Envelope envelope = JsonConvert.DeserializeObject<Envelope>(
                    pPayload);
                if (envelope == null || envelope.Draft == null)
                    return false;
                if (envelope.Version < MinimumSupportedVersion ||
                    envelope.Version > SchemaVersion)
                {
                    pReason = "unsupported_peace_payload_version";
                    return false;
                }
                if (envelope.Draft.Terms.Count == 0 ||
                    envelope.Draft.Terms.Count >
                    WarPeaceSettlementValidationRules.MaximumTerms)
                    return false;
                if (envelope.Version == 1)
                {
                    envelope.Draft.Scope =
                        WarPeaceSettlementScopeKind.Coalition;
                    envelope.Draft.ExitRootKingdomId = -1L;
                    envelope.Draft.Participants.Clear();
                }
                pDraft = envelope.Draft;
                pReason = "";
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
