using AncientWarfare3.api.multiplayer;
using AncientWarfare3.api.commands;

namespace AncientWarfare3.core.multiplayer.commands
{
    internal static class AW3AuthoritativeCommandRouter
    {
        internal static AW3CommandResult Dispatch(AW3CommandRequest request)
        {
            switch (request.Kind)
            {
                case AW3CommandKind.ConfigurePolicy:
                    return AW3PolicyCommandHandler.Dispatch(request);
                case AW3CommandKind.SetPolicyClass:
                    return AW3PolicyCommandHandler.Dispatch(request);
                case AW3CommandKind.StartPolicyNode:
                    return AW3PolicyCommandHandler.Dispatch(request);
                case AW3CommandKind.TogglePolicyNodeLock:
                    return AW3PolicyCommandHandler.Dispatch(request);
                case AW3CommandKind.StartCoreFabrication:
                    return AW3PolicyCommandHandler.Dispatch(request);
                case AW3CommandKind.StartTargetedDecision:
                    return AW3RealmCommandHandler.Dispatch(request);
                case AW3CommandKind.StartMandateDecision:
                    return AW3PolicyCommandHandler.Dispatch(request);
                case AW3CommandKind.AppointCourtOfficer:
                    return AW3CourtCommandHandler.Dispatch(request);
                case AW3CommandKind.SetCourtDisposition:
                    return AW3CourtCommandHandler.Dispatch(request);
                case AW3CommandKind.ChangeCourtAuxiliaryLaw:
                    return AW3CourtCommandHandler.Dispatch(request);
                case AW3CommandKind.ChangeInheritanceLaw:
                    return AW3CourtCommandHandler.Dispatch(request);
                case AW3CommandKind.SubmitCivilServiceRanking:
                    return AW3CourtCommandHandler.Dispatch(request);
                case AW3CommandKind.RelocateFeudatory:
                    return AW3RealmCommandHandler.Dispatch(request);
                case AW3CommandKind.ReclaimFeudatoryCity:
                    return AW3RealmCommandHandler.Dispatch(request);
                case AW3CommandKind.AbolishFeudatory:
                    return AW3RealmCommandHandler.Dispatch(request);
                case AW3CommandKind.CreateDiplomacyProposal:
                    return AW3DiplomacyCommandHandler.Dispatch(request);
                case AW3CommandKind.RespondDiplomacyProposal:
                    return AW3DiplomacyCommandHandler.Dispatch(request);
                case AW3CommandKind.StartSpyNetwork:
                    return AW3DiplomacyCommandHandler.Dispatch(request);
                case AW3CommandKind.StartForgeDocuments:
                    return AW3DiplomacyCommandHandler.Dispatch(request);
                case AW3CommandKind.DeclareWar:
                    return AW3DiplomacyCommandHandler.Dispatch(request);
                case AW3CommandKind.ConferPosthumousTitle:
                    return AW3RecordsCommandHandler.Dispatch(request);
                case AW3CommandKind.RenameClan:
                case AW3CommandKind.RenameSurname:
                    return AW3RecordsCommandHandler.Dispatch(request);
                case AW3CommandKind.ChangeEra:
                    return AW3RecordsCommandHandler.Dispatch(request);
                case AW3CommandKind.SetArmyRallyPoint:
                case AW3CommandKind.SetArmyTargetCity:
                case AW3CommandKind.SetArmyPosture:
                case AW3CommandKind.CancelArmyOrder:
                    return ArmyRtsCommandService.Dispatch(request);
                default:
                    return AW3CommandResult.Rejected(
                        AW3CommandError.InvalidRequest,
                        "aw3_command_invalid_request");
            }
        }
    }
}
