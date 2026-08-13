using System;
using System.Collections.Generic;
using AncientWarfare3.core.grandstrategy;

namespace AncientWarfare3.api.commands
{
    public sealed class GrandStrategyArmyCommandService
    {
        private readonly GrandStrategyPathService _paths;
        private readonly Func<int, int, IReadOnlyList<int>> _routeResolver;
        private readonly HashSet<string> _committed =
            new HashSet<string>(StringComparer.Ordinal);

        public GrandStrategyArmyCommandService(GrandStrategyPathService paths,
            Func<int, int, IReadOnlyList<int>> routeResolver)
        {
            _paths = paths ?? throw new ArgumentNullException(nameof(paths));
            _routeResolver = routeResolver ??
                throw new ArgumentNullException(nameof(routeResolver));
        }

        public GrandStrategyArmyCommandResult Execute(GrandStrategyArmy army,
            GrandStrategyArmyCommand command)
        {
            if (army == null || command == null || army.Id != command.ArmyId)
                return Reject("army_missing");
            if (!GrandStrategyArmyRules.CanCommand(army, command.KingdomId))
                return Reject("not_authorized");
            string key = command.WorldGeneration + ":" + command.KingdomId +
                ":" + command.ClientSequence;
            if (_committed.Contains(key))
                return new GrandStrategyArmyCommandResult
                    { Accepted = true, Duplicate = true };
            if (army.Revision != command.ExpectedRevision)
                return Reject("revision_mismatch");

            bool accepted = command.Kind switch
            {
                GrandStrategyArmyCommandKind.Move => ExecuteMove(army, command),
                GrandStrategyArmyCommandKind.Rally => SetTask(army,
                    GrandStrategyArmyTask.Rally),
                GrandStrategyArmyCommandKind.Pursue => SetTask(army,
                    GrandStrategyArmyTask.Pursue),
                GrandStrategyArmyCommandKind.Siege => SetTask(army,
                    GrandStrategyArmyTask.Siege),
                GrandStrategyArmyCommandKind.Follow => SetTask(army,
                    GrandStrategyArmyTask.Follow),
                GrandStrategyArmyCommandKind.Retreat => SetTask(army,
                    GrandStrategyArmyTask.Retreat),
                GrandStrategyArmyCommandKind.Disband => SetTask(army,
                    GrandStrategyArmyTask.Disband),
                _ => false
            };
            if (!accepted) return Reject("command_rejected");
            _committed.Add(key);
            return new GrandStrategyArmyCommandResult { Accepted = true };
        }

        private bool ExecuteMove(GrandStrategyArmy army,
            GrandStrategyArmyCommand command)
        {
            if (command.TargetTileId < 0) return false;
            IReadOnlyList<int> route = _routeResolver(army.PositionTileId,
                command.TargetTileId);
            return _paths.TrySubmit(army, command.TargetTileId, route,
                estimatedArrival: Math.Max(1, route?.Count ?? 0),
                supplyCost: Math.Max(0, (route?.Count ?? 0) - 1));
        }

        private static bool SetTask(GrandStrategyArmy army,
            GrandStrategyArmyTask task)
        {
            army.Task = task;
            army.Revision++;
            return true;
        }

        private static GrandStrategyArmyCommandResult Reject(string error)
        {
            return new GrandStrategyArmyCommandResult
                { Accepted = false, Error = error };
        }
    }
}
