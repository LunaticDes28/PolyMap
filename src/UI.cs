using System;
using HarmonyLib;
using PolytopiaBackendBase.Game;
using Polytopia.Data;

namespace PolyMap
{
    public static class UI
    {
        public static string? SelectedCustomMapId;

        public static bool IsCustomSelected => SelectedCustomMapId != null;

        /// <summary>Max total players (you + opponents) on Galapagos.</summary>
        public const int GalapagosMaxPlayers = 4;

        /// <summary>Max opponents when Galapagos is selected (total = opponents + 1).</summary>
        public const int GalapagosMaxOpponents = GalapagosMaxPlayers - 1; // 3

        public static bool IsGalapagosSelected
        {
            get
            {
                if (SelectedCustomMapId != null
                    && SelectedCustomMapId.Equals("galapagos", StringComparison.OrdinalIgnoreCase))
                    return true;

                try
                {
                    if (Loader.TryGetCustom("galapagos", out var g)
                        && GameManager.PreliminaryGameSettings != null
                        && (int)GameManager.PreliminaryGameSettings.mapPreset == g.IdValue)
                        return true;
                }
                catch { }

                return false;
            }
        }

        public static void ClearCustomMapSelection()
        {
            SelectedCustomMapId = null;
        }

        static bool _injecting;
        static bool _clampingOpponents;

        // -----------------------------------------------------------------
        // Inject custom maps after "Water World"
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

                        __instance.AddItem(map.DisplayName, map.IdValue);
                        Loader.modLogger?.LogInfo(
                            $"[PolyMap-UI] Added MapType '{map.DisplayName}' id={map.IdValue}");
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

            if (label.Trim().Equals("water world", StringComparison.OrdinalIgnoreCase))
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
        // Map type changed → track custom id + clamp opponents
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

                if (id >= 0)
                {
                    for (int i = 0; i < Loader.CustomMaps.Count; i++)
                    {
                        if (Loader.CustomMaps[i].IdValue != id)
                            continue;

                        SelectedCustomMapId = Loader.CustomMaps[i].Id;
                        if (GameManager.PreliminaryGameSettings != null)
                            GameManager.PreliminaryGameSettings.mapPreset = (MapPreset)id;

                        Loader.modLogger?.LogInfo(
                            $"[PolyMap-UI] Selected {SelectedCustomMapId} id={id} index={index}");
                        break;
                    }
                }

                if (SelectedCustomMapId == null)
                    TrackFromSettings();

                if (IsGalapagosSelected)
                    ClampOpponentsForGalapagos(__instance);
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogWarning($"[PolyMap-UI] OnMapTypeChanged: {ex.Message}");
            }
        }

        // -----------------------------------------------------------------
        // Real opponents callback from decompile
        // -----------------------------------------------------------------
        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameSetupScreen_UI2), nameof(GameSetupScreen_UI2.OnNumberOfOpponentsChanged))]
        public static void OnNumberOfOpponentsChanged_Galapagos(
            GameSetupScreen_UI2 __instance,
            int index)
        {
            if (_clampingOpponents)
                return;

            try
            {
                if (!IsGalapagosSelected)
                    return;

                ClampOpponentsForGalapagos(__instance);
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogWarning($"[PolyMap-UI] OnNumberOfOpponentsChanged: {ex.Message}");
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameSetupScreen_UI2), nameof(GameSetupScreen_UI2.RunLayout))]
        public static void RunLayout_Track(
            GameSetupScreen_UI2 __instance,
            ScreenBase_UI2.ScreenSize screenSize)
        {
            TrackFromSettings();

            try
            {
                if (IsGalapagosSelected)
                    ClampOpponentsForGalapagos(__instance);
            }
            catch { }
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

        /// <summary>
        /// Galapagos: opponents ≤ 3 (total players ≤ 4).
        /// Uses opponentsData.ids; re-invokes OnNumberOfOpponentsChanged when clamping.
        /// </summary>
        static void ClampOpponentsForGalapagos(GameSetupScreen_UI2? screen)
        {
            if (screen == null || _clampingOpponents)
                return;

            _clampingOpponents = true;
            try
            {
                UIHorizontalListData data = screen.opponentsData;
                if (data.ids == null || data.ids.Count == 0)
                    return;

                // Build filtered labels/ids: only 0..3, skip ANY and anything > 3
                var labels = new Il2CppSystem.Collections.Generic.List<string>();
                var allowedIds = new List<int>();
                int selectedIndex = 0;
                int currentId = -1;

                // Current selection from listOpponentCount if possible
                try
                {
                    var list = screen.view?.listOpponentCount;
                    if (list != null)
                    {
                        int idx = list.SelectedIndex;
                        if (idx >= 0 && idx < data.ids.Count)
                            currentId = data.ids[idx];
                    }
                }
                catch { }

                for (int i = 0; i < data.ids.Count; i++)
                {
                    int id = data.ids[i];

                    bool isAny = false;
                    try { isAny = id == GameSetupScreen_UI2.OPPONENTCOUNT_ANY; }
                    catch { }

                    if (isAny) continue;
                    if (id < 0 || id > GalapagosMaxOpponents) continue; // drop 4+

                    string label = "?";
                    try
                    {
                        if (data.labels != null && i < data.labels.Count && data.labels[i] != null)
                            label = data.labels[i];
                        else
                            label = id.ToString();
                    }
                    catch { label = id.ToString(); }

                    if (id == currentId || (currentId > GalapagosMaxOpponents && id == GalapagosMaxOpponents))
                        selectedIndex = labels.Count;

                    labels.Add(label);
                    allowedIds.Add(id);
                }

                if (labels.Count == 0)
                    return;

                // Prefer 3 opponents as default when previous selection was too high / ANY
                if (currentId < 0 || currentId > GalapagosMaxOpponents)
                {
                    for (int i = 0; i < allowedIds.Count; i++)
                    {
                        if (allowedIds[i] == GalapagosMaxOpponents)
                        {
                            selectedIndex = i;
                            break;
                        }
                    }
                }

                // Rewrite opponentsData so ids/labels stay in sync with the UI list
                try
                {
                    // Clear and re-add only allowed entries
                    // UIHorizontalListData API varies — safest is SetShowOpponents on the view
                    string header = "Opponents";
                    try
                    {
                        // keep existing header if you can read it from view; else default
                    }
                    catch { }

                    screen.view?.SetShowOpponents(
                        header,
                        labels,
                        numActive: labels.Count,  // only these are active/shown
                        selected: selectedIndex);
                }
                catch (Exception ex)
                {
                    Loader.modLogger?.LogWarning($"[PolyMap-UI] SetShowOpponents: {ex.Message}");
                }

                // Keep data.ids aligned if the list still drives OnNumberOfOpponentsChanged by index
                try
                {
                    // If ids is a List<int> we can clear+add; if array, rebuild via AddItem if available
                    if (data.ids != null)
                    {
                        // Best-effort: many builds expose ids as List
                        try
                        {
                            data.ids.Clear();
                            for (int i = 0; i < allowedIds.Count; i++)
                                data.ids.Add(allowedIds[i]);
                        }
                        catch
                        {
                            // struct copy — write back if needed
                        }
                    }
                }
                catch { }

                // Drive vanilla so settings match selected index
                screen.OnNumberOfOpponentsChanged(selectedIndex);

                Loader.modLogger?.LogInfo(
                    $"[PolyMap-UI] Galapagos: opponents list limited to {labels.Count} options " +
                    $"(max {GalapagosMaxOpponents}), selected={selectedIndex}");
            }
            finally
            {
                _clampingOpponents = false;
            }
        }
    }
}