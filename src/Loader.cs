using System;
using System.Collections.Generic;
using BepInEx.Logging;
using HarmonyLib;
using PolytopiaBackendBase.Game;
using PolyMod;

namespace PolyMap
{
    public static class Loader
    {
        public static ManualLogSource? modLogger;

        public static int mapPresetsAutoidx = Enum.GetValues(typeof(MapPreset)).Length;

        /// <summary>
        /// Registered custom maps in order (for UI append order).
        /// </summary>
        public static readonly List<CustomMapPreset> CustomMaps = new();

        public struct CustomMapPreset
        {
            public string Id;          // enum / registry key, e.g. "river"
            public string DisplayName; // UI label, e.g. "River"
            public int IdValue;        // MapPreset int value
        }

        public static void Load(ManualLogSource logger)
        {
            modLogger = logger;

            // Register maps before patches that read the list
            RegisterCustomMapPreset("river", "River");
            // RegisterCustomMapPreset("archipelago", "Archipelago");

            Harmony.CreateAndPatchAll(typeof(Loader));
            Harmony.CreateAndPatchAll(typeof(Main));
            Harmony.CreateAndPatchAll(typeof(UI));

            modLogger?.LogInfo("[PolyMap] Mod initialized");
        }

        /// <summary>
        /// Same pattern as RegisterCustomGameMode: double EnumCache map + bump index.
        /// </summary>
        public static void RegisterCustomMapPreset(string id, string? displayName = null)
        {
            try
            {
                if (string.IsNullOrEmpty(id))
                    throw new ArgumentException(nameof(id));

                string label = string.IsNullOrEmpty(displayName)
                    ? char.ToUpperInvariant(id[0]) + id.Substring(1)
                    : displayName;

                int value = mapPresetsAutoidx;

                EnumCache<MapPreset>.AddMapping(id, (MapPreset)value);
                EnumCache<MapPreset>.AddMapping(id, (MapPreset)value);

                CustomMaps.Add(new CustomMapPreset
                {
                    Id = id,
                    DisplayName = label,
                    IdValue = value
                });

                modLogger?.LogInfo(
                    $"[PolyMap] MapPreset '{id}' → {value}, UI=\"{label}\"");

                mapPresetsAutoidx++;
            }
            catch (Exception ex)
            {
                modLogger?.LogError($"[PolyMap] RegisterCustomMapPreset({id}): {ex}");
            }
        }

        public static bool IsCustomMapPreset(MapPreset preset)
        {
            int v = (int)preset;
            for (int i = 0; i < CustomMaps.Count; i++)
                if (CustomMaps[i].IdValue == v)
                    return true;
            return false;
        }

        public static bool IsCustomMapPreset(int presetId)
        {
            for (int i = 0; i < CustomMaps.Count; i++)
                if (CustomMaps[i].IdValue == presetId)
                    return true;
            return false;
        }

        public static bool TryGetCustom(string id, out CustomMapPreset preset)
        {
            for (int i = 0; i < CustomMaps.Count; i++)
            {
                if (CustomMaps[i].Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                {
                    preset = CustomMaps[i];
                    return true;
                }
            }
            preset = default;
            return false;
        }
    }
}