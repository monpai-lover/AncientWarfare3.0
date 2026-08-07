using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.db;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.multiplayer.commands
{
    internal static class AW3RecordsCommandHandler
    {
        internal static AW3CommandResult Dispatch(AW3CommandRequest request)
        {
            Kingdom kingdom = FindKingdom(request.CountryId);
            if (kingdom == null) return NotFound();
            switch (request.Kind)
            {
                case AW3CommandKind.ChangeEra:
                    return ChangeEra(kingdom, request);
                case AW3CommandKind.ConferPosthumousTitle:
                    return ConferPosthumousTitle(kingdom, request);
                case AW3CommandKind.RenameClan:
                    return RenameClan(kingdom, request);
                case AW3CommandKind.RenameSurname:
                    return RenameSurname(kingdom, request);
                case AW3CommandKind.GrantVirtualNobleTitle:
                    return GrantVirtualNobleTitle(kingdom, request);
                case AW3CommandKind.EditVirtualNobleTitle:
                    return EditVirtualNobleTitle(kingdom, request);
                case AW3CommandKind.DeleteVirtualNobleTitle:
                    return DeleteVirtualNobleTitle(kingdom, request);
                default:
                    return AW3CommandResult.Rejected(
                        AW3CommandError.InvalidRequest,
                        "aw3_command_invalid_request");
            }
        }

        private static AW3CommandResult ChangeEra(Kingdom kingdom,
            AW3CommandRequest request)
        {
            Actor ruler = kingdom.king;
            if (ruler?.data == null || ruler.isRekt()) return NotFound();
            EraChangeResult result = YearNameService.TryChangeEra(
                kingdom, ruler, request.Text, EraChangeKind.Voluntary,
                EraChangeReason.PlayerRequested);
            return result.Success
                ? AW3CommandResult.Success("aw3_command_accepted",
                    result.EraId, detailCode: (int)result.BlockReason)
                : AW3CommandResult.Rejected(MapEraError(result.BlockReason),
                    "aw3_era_change_rejected",
                    detailCode: (int)result.BlockReason);
        }

        private static AW3CommandResult ConferPosthumousTitle(
            Kingdom kingdom, AW3CommandRequest request)
        {
            if (FindActor(request.ActorId) == null) return NotFound();
            ConferredPosthumousCommitResult result =
                ConferredPosthumousTitleService.TryCommit(
                    kingdom.id, request.ActorId, request.Key,
                    ConferredPosthumousSource.Player);
            return result.Success
                ? AW3CommandResult.Success("aw3_command_accepted",
                    result.RecordId, detailCode: (int)result.Result)
                : AW3CommandResult.Rejected(
                    MapConferredError(result.Result),
                    "aw3_conferred_commit_rejected",
                    detailCode: (int)result.Result);
        }

        private static AW3CommandResult RenameClan(Kingdom kingdom,
            AW3CommandRequest request)
        {
            if (!KingdomContainsShi(kingdom, request.SecondaryId))
                return AW3CommandResult.Rejected(
                    AW3CommandError.Unauthorized,
                    "aw3_command_unauthorized");
            int changed = VisibleClanRenameService.RenameWholeShiTree(
                request.SecondaryId, request.Text);
            return changed > 0
                ? AW3CommandResult.Success("aw3_command_accepted",
                    request.SecondaryId, detailCode: changed)
                : AW3CommandResult.Rejected(AW3CommandError.IllegalTarget,
                    "aw3_rename_visible_clan_none", detailCode: 0);
        }

        private static bool KingdomContainsShi(Kingdom kingdom, long shiId)
        {
            if (kingdom?.data == null || shiId <= 0) return false;
            try
            {
                foreach (Actor actor in kingdom.units)
                {
                    if (actor?.data == null || actor.isRekt()) continue;
                    actor.data.get(LineageKeys.SHI_ID,
                        out long actorShiId, -1L);
                    if (actorShiId == shiId) return true;
                }
            }
            catch { }
            long founderId = LineageQuery.GetShiBranchFounderId(shiId);
            ActorArchiveTableItem founder =
                LineageArchiveReader.ReadRow(founderId);
            return founder != null && founder.kingdom_id == kingdom.id;
        }

        private static AW3CommandResult RenameSurname(Kingdom kingdom,
            AW3CommandRequest request)
        {
            if (!ActorBelongsToKingdom(kingdom, request.ActorId))
                return AW3CommandResult.Rejected(
                    AW3CommandError.Unauthorized,
                    "aw3_command_unauthorized");
            int changed = VisibleSurnameRenameService.
                RenamePatrilinealBranch(request.ActorId, request.Text);
            return changed > 0
                ? AW3CommandResult.Success("aw3_command_accepted",
                    request.ActorId, detailCode: changed)
                : AW3CommandResult.Rejected(AW3CommandError.IllegalTarget,
                    "aw3_rename_visible_surname_none", detailCode: 0);
        }

        private static AW3CommandResult GrantVirtualNobleTitle(
            Kingdom kingdom, AW3CommandRequest request)
        {
            Actor grantor = kingdom?.king;
            Actor target = FindActor(request.ActorId);
            VirtualNobleTitleGrantResult result =
                VirtualNobleTitleService.TryGrant(kingdom, grantor, target,
                    request.Text, request.BoolValue, out _);
            if (result == VirtualNobleTitleGrantResult.Success)
                return AW3CommandResult.Success("aw3_command_accepted",
                    request.ActorId, detailCode: (int)result);
            return AW3CommandResult.Rejected(MapVirtualTitleError(result),
                MapVirtualTitleMessageKey(result), request.ActorId,
                (int)result);
        }

        private static AW3CommandResult EditVirtualNobleTitle(
            Kingdom kingdom, AW3CommandRequest request)
        {
            VirtualNobleTitleEditResult result = VirtualNobleTitleService.
                TryEdit(request.SecondaryId, kingdom.id, request.Text);
            return MapVirtualTitleEditResult(result, request.SecondaryId);
        }

        private static AW3CommandResult DeleteVirtualNobleTitle(
            Kingdom kingdom, AW3CommandRequest request)
        {
            VirtualNobleTitleEditResult result = VirtualNobleTitleService.
                TryDelete(request.SecondaryId, kingdom.id);
            return MapVirtualTitleEditResult(result, request.SecondaryId);
        }

        private static AW3CommandResult MapVirtualTitleEditResult(
            VirtualNobleTitleEditResult pResult, long pTitleId)
        {
            if (pResult == VirtualNobleTitleEditResult.Success)
                return AW3CommandResult.Success("aw3_command_accepted",
                    pTitleId, detailCode: (int)pResult);
            AW3CommandError error;
            string message;
            switch (pResult)
            {
                case VirtualNobleTitleEditResult.NotReady:
                case VirtualNobleTitleEditResult.PersistenceFailed:
                    error = AW3CommandError.ExecutionFailed;
                    message = pResult == VirtualNobleTitleEditResult.NotReady
                        ? "aw_virtual_title_error_not_ready"
                        : "aw_virtual_title_error_persistence";
                    break;
                case VirtualNobleTitleEditResult.InvalidText:
                    error = AW3CommandError.IllegalTarget;
                    message = "aw_virtual_title_error_invalid_text";
                    break;
                case VirtualNobleTitleEditResult.Duplicate:
                    error = AW3CommandError.Conflict;
                    message = "aw_virtual_title_error_duplicate";
                    break;
                default:
                    error = AW3CommandError.IllegalTarget;
                    message = "aw_virtual_title_error_not_found";
                    break;
            }
            return AW3CommandResult.Rejected(error, message, pTitleId,
                (int)pResult);
        }

        private static AW3CommandError MapVirtualTitleError(
            VirtualNobleTitleGrantResult pResult)
        {
            switch (pResult)
            {
                case VirtualNobleTitleGrantResult.NotReady:
                case VirtualNobleTitleGrantResult.PersistenceFailed:
                    return AW3CommandError.ExecutionFailed;
                case VirtualNobleTitleGrantResult.Duplicate:
                    return AW3CommandError.Conflict;
                case VirtualNobleTitleGrantResult.InvalidText:
                case VirtualNobleTitleGrantResult.InvalidTarget:
                    return AW3CommandError.IllegalTarget;
                default:
                    return AW3CommandError.InvalidRequest;
            }
        }

        private static string MapVirtualTitleMessageKey(
            VirtualNobleTitleGrantResult pResult)
        {
            switch (pResult)
            {
                case VirtualNobleTitleGrantResult.NotReady:
                    return "aw_virtual_title_error_not_ready";
                case VirtualNobleTitleGrantResult.InvalidTarget:
                    return "aw_virtual_title_error_invalid_target";
                case VirtualNobleTitleGrantResult.InvalidText:
                    return "aw_virtual_title_error_invalid_text";
                case VirtualNobleTitleGrantResult.Duplicate:
                    return "aw_virtual_title_error_duplicate";
                case VirtualNobleTitleGrantResult.PersistenceFailed:
                    return "aw_virtual_title_error_persistence";
                default:
                    return "aw_virtual_title_error_generic";
            }
        }

        private static bool ActorBelongsToKingdom(Kingdom kingdom,
            long actorId)
        {
            if (kingdom?.data == null || actorId <= 0) return false;
            Actor actor = FindActor(actorId);
            if (actor?.data != null) return actor.kingdom == kingdom;
            ActorArchiveTableItem row = LineageArchiveReader.ReadRow(actorId);
            return row != null && row.kingdom_id == kingdom.id;
        }

        private static AW3CommandError MapEraError(
            EraChangeBlockReason reason)
        {
            switch (reason)
            {
                case EraChangeBlockReason.Cooldown:
                    return AW3CommandError.Cooldown;
                case EraChangeBlockReason.InsufficientPoliticalPoints:
                    return AW3CommandError.InsufficientResources;
                case EraChangeBlockReason.ArchiveUnavailable:
                case EraChangeBlockReason.PersistenceFailed:
                    return AW3CommandError.ExecutionFailed;
                default:
                    return AW3CommandError.IllegalTarget;
            }
        }

        private static AW3CommandError MapConferredError(
            ConferredPosthumousResult result)
        {
            switch (result)
            {
                case ConferredPosthumousResult.Cooldown:
                    return AW3CommandError.Cooldown;
                case ConferredPosthumousResult.StalePreview:
                    return AW3CommandError.StaleState;
                case ConferredPosthumousResult.MissingArchive:
                case ConferredPosthumousResult.PersistenceFailed:
                    return AW3CommandError.ExecutionFailed;
                default:
                    return AW3CommandError.IllegalTarget;
            }
        }

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

        private static Actor FindActor(long id)
        {
            if (id <= 0 || World.world?.units == null) return null;
            try
            {
                Actor actor = World.world.units.get(id);
                return actor?.data != null ? actor : null;
            }
            catch { return null; }
        }
    }
}
