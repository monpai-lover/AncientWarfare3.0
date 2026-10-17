namespace UnityEngine
{
    // Standalone persistence tests supply their own temporary collection path.
    // Never resolve the player's real Unity storage from a test process.
    internal static class Application
    {
        public static string persistentDataPath =>
            throw new System.NotSupportedException(
                "Standalone card tests must supply a temporary storage path.");
    }
}
