using System;
using HarmonyLib;
using PolytopiaBackendBase.Game;
using Polytopia.Data;

namespace PolyMap
{
    public static class UI
    {
        public static string? SelectedCustomMapId;

        public static bool IsRiverSelected =>
            SelectedCustomMapId != null
            && SelectedCustomMapId.Equals("river", StringComparison.OrdinalIgnoreCase);

        public static void ClearCustomMapSelection()
        {
            SelectedCustomMapId = null;
        }

        static bool _injecting;

        // -----------------------------------------------------------------
        // After vanilla finishes the map list with "Water World",
        // append every custom map (each after the previous).
        // -----------------------------------------------------------------
        [HarmonyPostfix]
        [HarmonyPatch(typeof(UIHorizontalListData), nameof(UIHorizontalListData.AddItem))]
        public static void AddItem_AfterWaterWorld(
            UIHorizontalListData __instance,
            string label,
            int id)
        {
            if (_injecting || __instance == null)
                return;

            try
            {
                if (!IsWaterWorldLabel(label))
                    return;

                var labels = __instance.labels;
                if (labels == null)
                    return;

                _injecting = true;
                try
                {
                    for (int i = 0; i < Loader.CustomMaps.Count; i++)
                    {
                        var map = Loader.CustomMaps[i];

                        if (ListContainsLabel(labels, map.DisplayName))
                            continue;

                        // Appends after current last (Water World, then River, then next…)
                        __instance.AddItem(map.DisplayName, map.IdValue);

                        Loader.modLogger?.LogInfo(
                            $"[PolyMap-UI] After Water World → '{map.DisplayName}' id={map.IdValue}");
                    }
                }
                finally
                {
                    _injecting = false;
                }
            }
            catch (Exception ex)
            {
                _injecting = false;
                Loader.modLogger?.LogError($"[PolyMap-UI] AddItem: {ex}");
            }
        }

        static bool IsWaterWorldLabel(string label)
        {
            if (string.IsNullOrEmpty(label))
                return false;

            string lower = label.Trim().ToLowerInvariant();
            if (lower == "water world")
                return true;

            try
            {
                string loc = Localization.Get("gamesettings.map.waterworld");
                if (!string.IsNullOrEmpty(loc)
                    && label.Equals(loc, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch { }

            return false;
        }

        static bool ListContainsLabel(
            Il2CppSystem.Collections.Generic.List<string> labels,
            string name)
        {
            if (labels == null)
                return false;

            for (int i = 0; i < labels.Count; i++)
            {
                if (labels[i] != null
                    && labels[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        // -----------------------------------------------------------------
        // Track selection
        // -----------------------------------------------------------------
        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameSetupScreen_UI2), nameof(GameSetupScreen_UI2.OnMapTypeChanged))]
        public static void OnMapTypeChanged_Track(GameSetupScreen_UI2 __instance, int index)
        {
            try
            {
                SelectedCustomMapId = null;

                UIHorizontalListData data = __instance.mapTypeData;
                int id = -1;

                try
                {
                    if (data.ids != null && index >= 0 && index < data.ids.Count)
                        id = data.ids[index];
                }
                catch { }

                if (id < 0)
                {
                    TrackFromSettings();
                    return;
                }

                for (int i = 0; i < Loader.CustomMaps.Count; i++)
                {
                    if (Loader.CustomMaps[i].IdValue != id)
                        continue;

                    SelectedCustomMapId = Loader.CustomMaps[i].Id;

                    if (GameManager.PreliminaryGameSettings != null)
                        GameManager.PreliminaryGameSettings.mapPreset = (MapPreset)id;

                    Loader.modLogger?.LogInfo(
                        $"[PolyMap-UI] Selected {SelectedCustomMapId} id={id} index={index}");
                    return;
                }

                TrackFromSettings();
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogWarning($"[PolyMap-UI] OnMapTypeChanged: {ex.Message}");
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameSetupScreen_UI2), nameof(GameSetupScreen_UI2.RunLayout))]
        public static void RunLayout_Track(
            GameSetupScreen_UI2 __instance,
            ScreenBase_UI2.ScreenSize screenSize)
        {
            TrackFromSettings();
        }

        static void TrackFromSettings()
        {
            SelectedCustomMapId = null;
            try
            {
                if (GameManager.PreliminaryGameSettings == null)
                    return;

                int preset = (int)GameManager.PreliminaryGameSettings.mapPreset;
                for (int i = 0; i < Loader.CustomMaps.Count; i++)
                {
                    if (Loader.CustomMaps[i].IdValue == preset)
                    {
                        SelectedCustomMapId = Loader.CustomMaps[i].Id;
                        break;
                    }
                }
            }
            catch { }
        }
    }
}