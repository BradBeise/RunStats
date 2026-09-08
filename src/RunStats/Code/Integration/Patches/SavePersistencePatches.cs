using HarmonyLib;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using RunStats.Models;

namespace RunStats.Integration.Patches;

[HarmonyPatch(typeof(RunSaveManager), nameof(RunSaveManager.SaveRun), typeof(SerializableRun), typeof(bool))]
internal static class PrepareSidecarSavePatch
{
    [HarmonyPrefix]
    private static void Prefix(SerializableRun save, bool isMultiplayer)
    {
        RunStatsRuntime.OnVanillaSavePreparing(
            save.SaveTime,
            isMultiplayer ? RunMode.Multiplayer : RunMode.Singleplayer);
    }
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.SetUpSavedSingleplayer))]
internal static class ObserveSingleplayerLoadPatch
{
    [HarmonyPrefix]
    private static void Prefix(SerializableRun save)
    {
        RunStatsRuntime.OnVanillaRunLoaded(save, RunMode.Singleplayer);
    }
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.SetUpSavedMultiplayer))]
internal static class ObserveMultiplayerLoadPatch
{
    [HarmonyPrefix]
    private static void Prefix(LoadRunLobby lobby)
    {
        RunStatsRuntime.OnVanillaRunLoaded(lobby.Run, RunMode.Multiplayer);
    }
}

[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.DeleteCurrentRun))]
internal static class ArchiveDeletedSingleplayerSidecarPatch
{
    [HarmonyPrefix]
    private static void Prefix()
    {
        RunStatsRuntime.OnVanillaRunDeleted(RunMode.Singleplayer);
    }
}

[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.DeleteCurrentMultiplayerRun))]
internal static class ArchiveDeletedMultiplayerSidecarPatch
{
    [HarmonyPrefix]
    private static void Prefix()
    {
        RunStatsRuntime.OnVanillaRunDeleted(RunMode.Multiplayer);
    }
}
