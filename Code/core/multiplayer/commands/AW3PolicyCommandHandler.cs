using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.multiplayer.commands
{
    internal static class AW3PolicyCommandHandler
    {
        internal static AW3CommandResult Dispatch(AW3CommandRequest request)
        {
            Kingdom kingdom = FindKingdom(request.CountryId);
            if (kingdom == null) return NotFound();
            switch (request.Kind)
            {
                case AW3CommandKind.ConfigurePolicy:
                    return ConfigurePolicy(kingdom, request);
                case AW3CommandKind.SetPolicyClass:
                    return Result(KingdomPolicyService.ForceSetClassState(
                        kingdom, request.Key), kingdom.id);
                case AW3CommandKind.StartPolicyNode:
                    return Result(request.BoolValue
                            ? KingdomPolicyService.ForceStartResearch(
                                kingdom, request.Key)
                            : KingdomPolicyService.StartResearch(
                                kingdom, request.Key),
                        kingdom.id);
                case AW3CommandKind.TogglePolicyNodeLock:
                    return Result(KingdomPolicyService.ToggleNodeLocked(
                        kingdom, request.Key), kingdom.id);
                case AW3CommandKind.StartCoreFabrication:
                    City city = FindCity(request.CityId);
                    return city == null
                        ? NotFound()
                        : Result(KingdomPolicyService.StartFabricationDecision(
                            kingdom, kingdom, city, request.Key), kingdom.id);
                case AW3CommandKind.StartMandateDecision:
                    return Result(MandateDecisionService.ForceStart(
                        kingdom, request.Key), kingdom.id);
                default:
                    return AW3CommandResult.Rejected(
                        AW3CommandError.InvalidRequest,
                        "aw3_command_invalid_request");
            }
        }

        private static AW3CommandResult ConfigurePolicy(Kingdom kingdom,
            AW3CommandRequest request)
        {
            if (!KingdomPolicyService.SetPolicyEnabled(kingdom,
                    request.BoolValue)) return IllegalTarget();
            if (request.BoolValue &&
                !KingdomPolicyService.SetPolicyAIEnabled(kingdom,
                    request.SecondaryBoolValue)) return IllegalTarget();
            return Accepted(kingdom.id);
        }

        private static AW3CommandResult Result(bool accepted,
            long affectedId) => accepted
            ? Accepted(affectedId)
            : IllegalTarget();

        private static AW3CommandResult Accepted(long affectedId) =>
            AW3CommandResult.Success("aw3_command_accepted", affectedId);

        private static AW3CommandResult IllegalTarget() =>
            AW3CommandResult.Rejected(AW3CommandError.IllegalTarget,
                "aw3_command_illegal_target");

        private static AW3CommandResult NotFound() =>
            AW3CommandResult.Rejected(AW3CommandError.NotFound,
                "aw3_command_not_found");

        private static Kingdom FindKingdom(long id)
        {
            if (id <= 0 || World.world?.kingdoms == null) return null;
            try
            {
                Kingdom kingdom = World.world.kingdoms.get(id);
                return kingdom?.data != null && !kingdom.isRekt()
                    ? kingdom
                    : null;
            }
            catch { return null; }
        }

        private static City FindCity(long id)
        {
            if (id <= 0 || World.world?.cities == null) return null;
            try
            {
                City city = World.world.cities.get(id);
                return city?.data != null && !city.isRekt() ? city : null;
            }
            catch { return null; }
        }
    }
}
