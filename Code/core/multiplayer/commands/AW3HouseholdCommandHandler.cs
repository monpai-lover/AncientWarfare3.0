using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.multiplayer.commands
{
    internal static class AW3HouseholdCommandHandler
    {
        internal static AW3CommandResult Dispatch(AW3CommandRequest request)
        {
            if (request.Kind != AW3CommandKind.CommitDomesticHousehold)
                return Invalid();

            Kingdom kingdom = FindKingdom(request.CountryId);
            Actor candidate = FindActor(request.ActorId);
            if (kingdom == null || candidate == null)
                return AW3CommandResult.Rejected(AW3CommandError.NotFound,
                    "not_found");
            if (kingdom.king?.data == null ||
                kingdom.king.data.id != request.TargetActorId)
                return AW3CommandResult.Rejected(AW3CommandError.StaleState,
                    "invalid_household_ruler");
            if (!RulerHouseholdRules.TryParseKind(request.Key,
                    out RulerHouseholdKind kind))
                return Invalid();

            bool committed = RulerHouseholdService.TryCommitDomestic(
                kingdom, candidate.data.id, kind, out string reason);
            return committed
                ? AW3CommandResult.Success("aw3_command_accepted",
                    candidate.data.id)
                : AW3CommandResult.Rejected(
                    AW3DiplomacyCommandErrorRules.Map(reason), reason,
                    candidate.data.id);
        }

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

        private static Actor FindActor(long id)
        {
            if (id <= 0 || World.world?.units == null) return null;
            try
            {
                Actor actor = World.world.units.get(id);
                return actor?.data != null && !actor.isRekt()
                    ? actor
                    : null;
            }
            catch { return null; }
        }

        private static AW3CommandResult Invalid() =>
            AW3CommandResult.Rejected(AW3CommandError.InvalidRequest,
                "aw3_command_invalid_request");
    }
}
