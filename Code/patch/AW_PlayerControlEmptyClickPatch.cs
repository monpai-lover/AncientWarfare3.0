using AncientWarfare3.core.policy;
using HarmonyLib;
using UnityEngine;

namespace AncientWarfare3.patch
{
    // The vanilla empty-click scan assumes every entry in World.units still
    // has a tile and an asset. RTS/path recovery can briefly leave a stale
    // actor in that list; one null dereference would otherwise reach the
    // MapBox finalizer and pause the entire simulation.
    [HarmonyPatch(typeof(PlayerControl), "checkEmptyClick")]
    internal static class AW_PlayerControlEmptyClickPatch
    {
        [HarmonyPriority(Priority.First)]
        [HarmonyPrefix]
        private static bool Prefix()
        {
            if (!InputHelpers.GetMouseButtonUp(0)) return false;
            try
            {
                MapBox world = World.world;
                if (world == null || world.units == null) return false;
                if (!PixelDetector.GetSpritePixelColorUnderMousePointer(
                        world, out Vector2Int position) || position.x < 0)
                    return false;

                WorldTile tile = world.GetTile(position.x, position.y);
                if (tile == null) return false;
                foreach (Actor actor in world.units)
                {
                    if (!PlayerControlEmptyClickSafetyRules.CanInvokeActor(
                            actor != null, actor?.current_tile != null,
                            actor?.asset != null)) continue;
                    if (Toolbox.Dist(actor.current_tile.posV3.x,
                            actor.current_tile.posV3.y, position.x,
                            position.y) > 10f) continue;
                    actor.asset.action_click?.Invoke(actor,
                        actor.current_tile);
                }
            }
            catch
            {
                // Empty-click input is presentation-only. A stale actor or a
                // transition during save/load must never stop simulation.
            }
            return false;
        }
    }
}
