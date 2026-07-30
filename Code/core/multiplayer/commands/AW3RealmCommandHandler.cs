using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.court;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.multiplayer.commands
{
    internal static class AW3RealmCommandHandler
    {
        internal static AW3CommandResult Dispatch(AW3CommandRequest request)
        {
            switch (request.Kind)
            {
                case AW3CommandKind.StartTargetedDecision:
                    return StartTargetedDecision(request);
                case AW3CommandKind.RelocateFeudatory:
                    return Relocate(request);
                case AW3CommandKind.ReclaimFeudatoryCity:
                    return Reclaim(request);
                case AW3CommandKind.AbolishFeudatory:
                    return Abolish(request);
                default:
                    return Invalid();
            }
        }

        private static AW3CommandResult StartTargetedDecision(
            AW3CommandRequest request)
        {
            Kingdom country = FindKingdom(request.CountryId);
            Kingdom target = FindKingdom(request.TargetCountryId);
            if (country?.data == null || country.isRekt() ||
                target?.data == null || target.isRekt())
                return NotFound();
            bool started = KingdomPolicyService.StartDecisionWithTarget(
                country, request.Key, target);
            return started
                ? AW3CommandResult.Success("aw3_targeted_decision_started",
                    target.id)
                : Rejected("aw3_targeted_decision_rejected");
        }

        private static AW3CommandResult Relocate(AW3CommandRequest request)
        {
            Kingdom country = FindKingdom(request.CountryId);
            bool changed = FeudatoryService.TryRelocateFeudatory(country,
                request.SecondaryId, out int intensity);
            return changed
                ? AW3CommandResult.Success("aw3_feudatory_relocated",
                    request.SecondaryId, intensity)
                : Rejected("aw3_feudatory_relocate_rejected");
        }

        private static AW3CommandResult Reclaim(AW3CommandRequest request)
        {
            Kingdom country = FindKingdom(request.CountryId);
            bool changed = FeudatoryService.TryReclaimFeudatoryCity(country,
                request.SecondaryId, request.CityId, out int intensity);
            return changed
                ? AW3CommandResult.Success("aw3_feudatory_city_reclaimed",
                    request.CityId, intensity)
                : Rejected("aw3_feudatory_reclaim_rejected");
        }

        private static AW3CommandResult Abolish(AW3CommandRequest request)
        {
            Kingdom country = FindKingdom(request.CountryId);
            bool changed = FeudatoryService.TryAbolishFeudatory(country,
                request.SecondaryId, out int intensity);
            return changed
                ? AW3CommandResult.Success("aw3_feudatory_abolished",
                    request.SecondaryId, intensity)
                : Rejected("aw3_feudatory_abolish_rejected");
        }

        private static Kingdom FindKingdom(long id)
        {
            try { return World.world?.kingdoms?.get(id); }
            catch { return null; }
        }

        private static AW3CommandResult Invalid() =>
            AW3CommandResult.Rejected(AW3CommandError.InvalidRequest,
                "aw3_command_invalid_request");

        private static AW3CommandResult NotFound() =>
            AW3CommandResult.Rejected(AW3CommandError.NotFound,
                "aw3_command_target_not_found");

        private static AW3CommandResult Rejected(string message) =>
            AW3CommandResult.Rejected(AW3CommandError.IllegalTarget,
                message);
    }
}
