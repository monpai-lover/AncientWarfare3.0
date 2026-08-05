namespace AncientWarfare3.core.performance
{
    /// <summary>
    /// Prevents the large scheduler from admitting a simulation cycle while
    /// the vanilla world containers are still being constructed or restored.
    /// </summary>
    internal static class AWWorldInitializationGate
    {
        internal static bool IsPending(MapBox pWorld = null)
        {
            MapBox world = pWorld ?? World.world;
            if (world == null || world.map_stats == null) return true;
            if (world.tiles_list == null || world.tiles_list.Length == 0)
                return true;
            if (world.map_chunk_manager == null ||
                world.map_chunk_manager.chunks == null ||
                world.map_chunk_manager.chunks.Length == 0)
                return true;
            return false;
        }
    }
}
