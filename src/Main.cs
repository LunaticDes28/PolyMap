using System;
using System.Collections.Generic;
using HarmonyLib;
using PolytopiaBackendBase.Game;
using Polytopia.Data;
using UnityEngine;

namespace PolyMap
{
    public static class Main
    {
        // -----------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------
        private static bool IsRiverMap(GameState? state = null)
        {
            if (!Loader.TryGetCustom("river", out var river))
                return false;
            if (UI.IsRiverSelected)
                return true;
            try
            {
                if (GameManager.PreliminaryGameSettings != null
                    && (int)GameManager.PreliminaryGameSettings.mapPreset == river.IdValue)
                    return true;
            }
            catch { }
            try
            {
                if (state?.Settings != null
                    && (int)state.Settings.mapPreset == river.IdValue)
                    return true;
            }
            catch { }
            return false;
        }

        // -----------------------------------------------------------------
        // Preset → dryland-like settings
        // -----------------------------------------------------------------
        [HarmonyPostfix]
        [HarmonyPatch(typeof(MapGeneratorSettings), nameof(MapGeneratorSettings.CreateFromPreset))]
        private static void CreateFromPreset_River(MapPreset mapPreset, ref MapGeneratorSettings __result)
        {
            try
            {
                if (__result == null)
                    return;
                if (!Loader.TryGetCustom("river", out var river))
                    return;
                if ((int)mapPreset != river.IdValue)
                    return;

                __result.wetness = 0f;
                __result.smoothIterations = 0;
                __result.preTerrainCityDensity = 0f;
                __result.postTerrainCityDensity = 0.8f;
                __result.maxSuburbCount = 0;
                __result.minSuburbCount = 0;
                __result.shallowPercentOfWater = 1f;
                Loader.modLogger?.LogInfo("[PolyMap] CreateFromPreset → river (dryland base)");
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[PolyMap] CreateFromPreset: {ex}");
            }
        }

        // -----------------------------------------------------------------
        // Before generate
        // -----------------------------------------------------------------
        [HarmonyPrefix]
        [HarmonyPatch(typeof(MapGenerator), nameof(MapGenerator.Generate))]
        private static void Generate_StampRiver_Prefix(GameState state, MapGeneratorSettings settings)
        {
            try
            {
                if (settings == null || !IsRiverMap(state))
                    return;
                if (!Loader.TryGetCustom("river", out var river))
                    return;

                settings.wetness = 0f;
                settings.smoothIterations = 0;
                settings.preTerrainCityDensity = 0f;
                settings.postTerrainCityDensity = 0.8f;
                settings.maxSuburbCount = 0;
                settings.minSuburbCount = 0;
                settings.shallowPercentOfWater = 1f;
                try
                {
                    if (state?.Settings != null)
                        state.Settings.mapPreset = (MapPreset)river.IdValue;
                }
                catch { }
                Loader.modLogger?.LogInfo(
                    $"[PolyMap] River prefix id={river.IdValue} wetness=0");
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[PolyMap] Generate prefix: {ex}");
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MapGenerator), nameof(MapGenerator.Generate))]
        private static void Generate_StampRiver_Postfix(GameState state, MapGeneratorSettings settings)
        {
            if (IsRiverMap(state))
                UI.SelectedCustomMapId = null;
        }

        // -----------------------------------------------------------------
        // After vanilla generate
        // -----------------------------------------------------------------
        [HarmonyPostfix]
        [HarmonyPatch(typeof(MapGenerator), nameof(MapGenerator.GenerateInternal))]
        private static void GenerateInternal_River(
            MapGenerator __instance,
            int seed,
            GameState gameState,
            MapGeneratorSettings settings)
        {
            try
            {
                if (gameState?.Map == null || settings == null)
                    return;
                if (!IsRiverMap(gameState))
                    return;
                CarveRiverAndBridgeVillages(__instance, gameState, settings);
            }
            catch (Exception ex)
            {
                Loader.modLogger?.LogError($"[PolyMap] River gen: {ex}");
            }
        }

        // -----------------------------------------------------------------
        // Core river logic
        // -----------------------------------------------------------------
        private static void CarveRiverAndBridgeVillages(
            MapGenerator gen,
            GameState gameState,
            MapGeneratorSettings settings)
        {
            MapData map = gameState.Map;
            int w = map.Width;
            int h = map.Height;
            var rng = gen.random;

            bool horizontal = true;

            List<WorldCoordinates> centerline = BuildMeanderPath(map, rng, horizontal);
            if (centerline.Count == 0)
            {
                Loader.modLogger?.LogError("[PolyMap] Empty river path");
                return;
            }

            int[] widths = BuildWidths(centerline.Count, rng, minNecks: 2);

            var riverTiles = new HashSet<int>();
            var neckLandCandidates = new Il2CppSystem.Collections.Generic.List<TileData>();

            for (int i = 0; i < centerline.Count; i++)
            {
                WorldCoordinates c = centerline[i];
                int width = widths[i];

                for (int d = -2; d <= 2; d++)
                {
                    if (width == 1 && d != 0) continue;
                    if (width == 2 && d != 0 && d != 1) continue;
                    if (width == 3 && (d < -1 || d > 1)) continue;

                    int x = horizontal ? c.X : c.X + d;
                    int y = horizontal ? c.Y + d : c.Y;

                    if (x < 0 || y < 0 || x >= w || y >= h)
                        continue;

                    TileData tile = map.GetTile(new WorldCoordinates(x, y));
                    if (tile == null)
                        continue;

                    if (tile.capitalOf != 0)
                        continue;
                    if (tile.HasImprovement(ImprovementData.Type.City) && tile.owner != 0)
                        continue;

                    tile.terrain = Polytopia.Data.TerrainData.Type.Water;
                    tile.altitude = -1;
                    tile.resource = null;

                    if (tile.improvement != null
                        && tile.improvement.type != ImprovementData.Type.City)
                    {
                        tile.improvement = null;
                    }

                    if (tile.HasImprovement(ImprovementData.Type.City) && tile.owner == 0)
                        tile.improvement = null;

                    riverTiles.Add(map.GetIndexUnsafe(x, y));
                }

                if (width == 1)
                    CollectNeckBanks(map, c, horizontal, neckLandCandidates);
            }

            // Break land to land diagonal connections on meander stairs
            SealMeanderCorners(map, centerline, riverTiles);

            int fishPlaced = PlaceFishOnRiver(map, riverTiles, rng);

            try
            {
                gen.MakeOcean(map, gameState, shouldConvertShallows: false);
            }
            catch { }

            int placed = PlaceBridgeVillages(gameState, gen, map, neckLandCandidates, riverTiles);

            Loader.modLogger?.LogInfo(
                $"[PolyMap] River path={centerline.Count} necks={CountNecks(widths)} villages={placed}");
        }

        /// <summary>
        /// On a stair step, if both orthogonal corners are land they diagonal-
        /// connect across the river — convert only those corners to water.
        /// </summary>
        private static void SealMeanderCorners(
            MapData map,
            List<WorldCoordinates> centerline,
            HashSet<int> riverTiles)
        {
            int w = map.Width;
            int h = map.Height;

            for (int i = 1; i < centerline.Count; i++)
            {
                var a = centerline[i - 1];
                var b = centerline[i];

                // Straight segment — no diagonal gap
                if (a.X == b.X || a.Y == b.Y)
                    continue;

                int c1x = a.X, c1y = b.Y;
                int c2x = b.X, c2y = a.Y;

                // Only seal when both corners are land (they touch diagonally)
                if (!IsLand(map, c1x, c1y, w, h) || !IsLand(map, c2x, c2y, w, h))
                    continue;

                CarveWaterAt(map, c1x, c1y, w, h, riverTiles);
                CarveWaterAt(map, c2x, c2y, w, h, riverTiles);
            }
        }

        private static bool IsLand(MapData map, int x, int y, int w, int h)
        {
            if (x < 0 || y < 0 || x >= w || y >= h)
                return false;
            TileData t = map.GetTile(new WorldCoordinates(x, y));
            return t != null && !t.IsWater;
        }

        private static void CarveWaterAt(
            MapData map, int x, int y, int w, int h, HashSet<int> riverTiles)
        {
            if (x < 0 || y < 0 || x >= w || y >= h)
                return;

            TileData tile = map.GetTile(new WorldCoordinates(x, y));
            if (tile == null)
                return;

            if (tile.capitalOf != 0)
                return;
            if (tile.HasImprovement(ImprovementData.Type.City) && tile.owner != 0)
                return;

            tile.terrain = Polytopia.Data.TerrainData.Type.Water;
            tile.altitude = -1;
            tile.resource = null;

            if (tile.improvement != null
                && tile.improvement.type != ImprovementData.Type.City)
            {
                tile.improvement = null;
            }

            if (tile.HasImprovement(ImprovementData.Type.City) && tile.owner == 0)
                tile.improvement = null;

            riverTiles.Add(map.GetIndexUnsafe(x, y));
        }

        // Center-band meander
        private static List<WorldCoordinates> BuildMeanderPath(
            MapData map,
            Il2CppSystem.Random rng,
            bool horizontal)
        {
            int w = map.Width;
            int h = map.Height;
            var path = new List<WorldCoordinates>();

            int length = horizontal ? w : h;
            int crossMax = horizontal ? h : w;

            int center = crossMax / 2;
            int maxOffset = Math.Max(1, crossMax / 12);
            int cross = center + rng.Range(-maxOffset, maxOffset + 1);

            int bandMin = Math.Max(2, crossMax / 3);
            int bandMax = Math.Min(crossMax - 3, (crossMax * 2) / 3);
            cross = Mathf.Clamp(cross, bandMin, bandMax);

            for (int t = 0; t < length; t++)
            {
                if (t > 0 && t < length - 1)
                {
                    int step = rng.Range(0, 100);
                    if (step < 25) cross--;
                    else if (step > 75) cross++;

                    if (cross < center && rng.Range(0, 100) < 40) cross++;
                    if (cross > center && rng.Range(0, 100) < 40) cross--;

                    cross = Mathf.Clamp(cross, bandMin, bandMax);
                }

                int x = horizontal ? t : cross;
                int y = horizontal ? cross : t;
                path.Add(new WorldCoordinates(x, y));
            }

            return path;
        }

        private static int[] BuildWidths(int length, Il2CppSystem.Random rng, int minNecks)
        {
            int[] widths = new int[length];
            for (int i = 0; i < length; i++)
                widths[i] = rng.Range(0, 100) < 55 ? 2 : 3;

            int necks = Math.Max(minNecks, length / 3);
            int spacing = Math.Max(4, length / (necks + 1));

            for (int n = 1; n <= necks; n++)
            {
                int idx = Mathf.Clamp(n * spacing + rng.Range(-1, 2), 2, length - 3);
                widths[idx] = 1;
                if (idx > 0) widths[idx - 1] = Math.Max(widths[idx - 1], 2);
                if (idx < length - 1) widths[idx + 1] = Math.Max(widths[idx + 1], 2);
            }

            return widths;
        }

        private static int CountNecks(int[] widths)
        {
            int c = 0;
            for (int i = 0; i < widths.Length; i++)
                if (widths[i] == 1) c++;
            return c;
        }

        private static void CollectNeckBanks(
            MapData map,
            WorldCoordinates center,
            bool horizontal,
            Il2CppSystem.Collections.Generic.List<TileData> outTiles)
        {
            WorldCoordinates[] offsets = horizontal
                ? new[]
                {
                    new WorldCoordinates(0, -1),
                    new WorldCoordinates(0, 1),
                    new WorldCoordinates(-1, 0),
                    new WorldCoordinates(1, 0),
                }
                : new[]
                {
                    new WorldCoordinates(-1, 0),
                    new WorldCoordinates(1, 0),
                    new WorldCoordinates(0, -1),
                    new WorldCoordinates(0, 1),
                };

            foreach (var o in offsets)
            {
                var p = new WorldCoordinates(center.X + o.X, center.Y + o.Y);
                if (!p.IsValid(map.Width, map.Height))
                    continue;

                TileData t = map.GetTile(p);
                if (t == null || t.IsWater) continue;
                if (t.improvement != null) continue;
                if (MapDataExtensions.DistanceToEdge(map, t.coordinates) < 1)
                    continue;

                outTiles.Add(t);
            }
        }

        private static int PlaceFishOnRiver(
            MapData map,
            HashSet<int> riverTiles,
            Il2CppSystem.Random rng)
        {
            int w = map.Width;
            int placed = 0;

            foreach (int idx in riverTiles)
            {
                int x = idx % w;
                int y = idx / w;

                TileData tile = map.GetTile(new WorldCoordinates(x, y));
                if (tile == null || !tile.IsWater)
                    continue;

                // Skip if already has something
                if (tile.resource != null)
                    continue;
                if (tile.improvement != null)
                    continue;

                // ~35% of river tiles get fish
                if (rng.Range(0, 100) >= 35)
                    continue;

                tile.resource = new ResourceState
                {
                    type = ResourceData.Type.Fish
                };
                placed++;
            }

            return placed;
        }

        private static int PlaceBridgeVillages(
            GameState gameState,
            MapGenerator gen,
            MapData map,
            Il2CppSystem.Collections.Generic.List<TileData> candidates,
            HashSet<int> riverIndices)
        {
            try { gen.Shuffle(candidates); }
            catch { }

            int placed = 0;
            var used = new HashSet<int>();

            foreach (TileData tile in candidates)
            {
                if (tile == null) continue;

                int idx = map.GetIndexUnsafe(tile.coordinates.X, tile.coordinates.Y);
                if (used.Contains(idx) || riverIndices.Contains(idx))
                    continue;

                if (tile.IsWater) continue;

                bool nearWater = false;
                foreach (TileData n in map.GetTileNeighbors(tile.coordinates))
                {
                    if (n != null && n.IsWater)
                    {
                        nearWater = true;
                        break;
                    }
                }
                if (!nearWater) continue;

                bool tooClose = false;
                foreach (TileData other in map.GetArea(tile.coordinates, 2, true, true))
                {
                    if (other != null && other.HasImprovement(ImprovementData.Type.City))
                    {
                        tooClose = true;
                        break;
                    }
                }
                if (tooClose) continue;

                if (tile.improvement != null)
                    continue;

                gen.SetTileAsCity(tile);
                used.Add(idx);
                placed++;
            }

            return placed;
        }
    }
}