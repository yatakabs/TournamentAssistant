﻿using SongCore;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using TournamentAssistantShared;
using UnityEngine;
using UnityEngine.Windows.WebCam;
using Logger = TournamentAssistantShared.Logger;

namespace TournamentAssistant.Utilities
{
    public class SongUtils
    {
        private static BeatmapLevelsModel _beatmapLevelsModel;
        private static CancellationTokenSource getLevelCancellationTokenSource;
        private static CancellationTokenSource getStatusCancellationTokenSource;

        public static List<IPreviewBeatmapLevel> masterLevelList;

        public static void OnEnable()
        {
            Loader.SongsLoadedEvent += Loader_SongsLoadedEvent;
        }

        private static void Loader_SongsLoadedEvent(Loader _, ConcurrentDictionary<string, CustomPreviewBeatmapLevel> __)
        {
            RefreshLoadedSongs();
        }

        //Returns the closest difficulty to the one provided, preferring lower difficulties first if any exist
        public static IDifficultyBeatmap GetClosestDifficultyPreferLower(IBeatmapLevel level, BeatmapDifficulty difficulty, string characteristic)
        {
            //First, look at the characteristic parameter. If there's something useful in there, we try to use it, but fall back to Standard
            var desiredCharacteristic = level.previewDifficultyBeatmapSets.FirstOrDefault(x => x.beatmapCharacteristic.serializedName == characteristic).beatmapCharacteristic ?? level.previewDifficultyBeatmapSets.First().beatmapCharacteristic;

            IDifficultyBeatmap[] availableMaps =
                level
                .beatmapLevelData
                .difficultyBeatmapSets
                .FirstOrDefault(x => x.beatmapCharacteristic.serializedName == desiredCharacteristic.serializedName)
                .difficultyBeatmaps
                .OrderBy(x => x.difficulty)
                .ToArray();

            IDifficultyBeatmap ret = availableMaps.FirstOrDefault(x => x.difficulty == difficulty);
            if (ret is CustomDifficultyBeatmap)
            {
                var extras = Collections.RetrieveExtraSongData(ret.level.levelID);
                var requirements = extras?._difficulties.First(x => x._difficulty == ret.difficulty).additionalDifficultyData._requirements;
                //Logger.Debug($"{ret.level.songName} is a custom level, checking for requirements on {ret.difficulty}...");
                if (
                    (requirements?.Count() > 0) &&
                    (!requirements?.ToList().All(x => Collections.capabilities.Contains(x)) ?? false)
                ) ret = null;
                //Logger.Debug((ret == null ? "Requirement not met." : "Requirement met!"));
            }

            if (ret == null)
            {
                ret = GetLowerDifficulty(availableMaps, difficulty, desiredCharacteristic);
            }
            if (ret == null)
            {
                ret = GetHigherDifficulty(availableMaps, difficulty, desiredCharacteristic);
            }

            return ret;
        }

        //Returns the next-lowest difficulty to the one provided
        private static IDifficultyBeatmap GetLowerDifficulty(IDifficultyBeatmap[] availableMaps, BeatmapDifficulty difficulty, BeatmapCharacteristicSO characteristic)
        {
            var ret = availableMaps.TakeWhile(x => x.difficulty < difficulty).LastOrDefault();
            if (ret is CustomDifficultyBeatmap)
            {
                var extras = Collections.RetrieveExtraSongData(ret.level.levelID);
                var requirements = extras?._difficulties.First(x => x._difficulty == ret.difficulty).additionalDifficultyData._requirements;
                Logger.Debug($"{ret.level.songName} is a custom level, checking for requirements on {ret.difficulty}...");
                if (
                    (requirements?.Count() > 0) &&
                    (!requirements?.ToList().All(x => Collections.capabilities.Contains(x)) ?? false)
                ) ret = null;
                Logger.Debug((ret == null ? "Requirement not met." : "Requirement met!"));
            }
            return ret;
        }

        //Returns the next-highest difficulty to the one provided
        private static IDifficultyBeatmap GetHigherDifficulty(IDifficultyBeatmap[] availableMaps, BeatmapDifficulty difficulty, BeatmapCharacteristicSO characteristic)
        {
            var ret = availableMaps.SkipWhile(x => x.difficulty < difficulty).FirstOrDefault();
            if (ret is CustomDifficultyBeatmap)
            {
                var extras = Collections.RetrieveExtraSongData(ret.level.levelID);
                var requirements = extras?._difficulties.First(x => x._difficulty == ret.difficulty).additionalDifficultyData._requirements;
                Logger.Debug($"{ret.level.songName} is a custom level, checking for requirements on {ret.difficulty}...");
                if (
                    (requirements?.Count() > 0) &&
                    (!requirements?.ToList().All(x => Collections.capabilities.Contains(x)) ?? false)
                ) ret = null;
                Logger.Debug((ret == null ? "Requirement not met." : "Requirement met!"));
            }
            return ret;
        }

        public static void RefreshLoadedSongs()
        {
            if (_beatmapLevelsModel == null) _beatmapLevelsModel = Resources.FindObjectsOfTypeAll<BeatmapLevelsModel>().First();

            masterLevelList = new List<IPreviewBeatmapLevel>();

            foreach (var pack in _beatmapLevelsModel.allLoadedBeatmapLevelPackCollection.beatmapLevelPacks)
            {
                masterLevelList.AddRange(pack.beatmapLevelCollection.beatmapLevels);
            }

            //This snippet helps me build the hardcoded list that ends up in OstHelper.cs
            /*var output = string.Join("\n", _beatmapLevelsModel.allLoadedBeatmapLevelPackCollection.beatmapLevelPacks.Select(x => $@"
new Pack
{{
    PackID = ""{x.packID}"",
    PackName = ""{x.packName}"",
    SongDictionary = new Dictionary<string, string>
    {{{
                    string.Join(",\n", x.beatmapLevelCollection.beatmapLevels.Select(y => $"{{\"{y.levelID}\", \"{y.songName}\"}}").ToArray())
    }}}
}},
"));
            File.WriteAllText(Environment.CurrentDirectory + "\\songs.json", output);*/
        }

        public static async Task<bool> HasDLCLevel(string levelId, AdditionalContentModel additionalContentModel = null)
        {
            additionalContentModel = additionalContentModel ?? Resources.FindObjectsOfTypeAll<AdditionalContentModel>().FirstOrDefault();
            if (additionalContentModel != null)
            {
                getStatusCancellationTokenSource?.Cancel();
                getStatusCancellationTokenSource = new CancellationTokenSource();

                var token = getStatusCancellationTokenSource.Token;
                return await additionalContentModel.GetLevelEntitlementStatusAsync(levelId, token) == AdditionalContentModel.EntitlementStatus.Owned;
            }

            return false;
        }

        public static async Task<BeatmapLevelsModel.GetBeatmapLevelResult?> GetLevelFromPreview(IPreviewBeatmapLevel level, BeatmapLevelsModel beatmapLevelsModel = null)
        {
            beatmapLevelsModel = beatmapLevelsModel ?? Resources.FindObjectsOfTypeAll<BeatmapLevelsModel>().FirstOrDefault();

            if (beatmapLevelsModel != null)
            {
                getLevelCancellationTokenSource?.Cancel();
                getLevelCancellationTokenSource = new CancellationTokenSource();

                var token = getLevelCancellationTokenSource.Token;

                BeatmapLevelsModel.GetBeatmapLevelResult? result = null;
                try
                {
                    result = await beatmapLevelsModel.GetBeatmapLevelAsync(level.levelID, token);
                }
                catch (OperationCanceledException) { }
                if (result?.isError == true || result?.beatmapLevel == null) return null; //Null out entirely in case of error
                return result;
            }
            return null;
        }

        public static async void PlaySong(IPreviewBeatmapLevel level, BeatmapCharacteristicSO characteristic, BeatmapDifficulty difficulty, OverrideEnvironmentSettings overrideEnvironmentSettings = null, ColorScheme colorScheme = null, GameplayModifiers gameplayModifiers = null, PlayerSpecificSettings playerSettings = null, Action<StandardLevelScenesTransitionSetupDataSO, LevelCompletionResults> songFinishedCallback = null)
        {
            var startStandardLevelMethodOverloads = typeof(MenuTransitionsHelper)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(x => x.Name == nameof(MenuTransitionsHelper.StartStandardLevel))
                .Select(x => new
                {
                    Method = x,
                    Parameters = x.GetParameters(),
                })
                .OrderByDescending(x => x.Parameters.Length)
                .ToArray();

            var startStandardLevelMethod = 
                (startStandardLevelMethodOverloads
                    .FirstOrDefault(x => x.Parameters.Last().Name == "levelRestartedCallback") // 1.25.* - 
                    ?? startStandardLevelMethodOverloads
                        .FirstOrDefault(x => x.Parameters.Length == 14) // 1.22.* - 1.24.*
                    ?? startStandardLevelMethodOverloads
                        .FirstOrDefault()) // Unknown version
                .Method;

            Action <IBeatmapLevel> SongLoaded = (loadedLevel) =>
            {
                MenuTransitionsHelper _menuSceneSetupData = Resources.FindObjectsOfTypeAll<MenuTransitionsHelper>().First();

                var parameters = new object[]{
                    "Solo",
                    loadedLevel.beatmapLevelData.GetDifficultyBeatmap(characteristic, difficulty),
                    loadedLevel,
                    overrideEnvironmentSettings,
                    colorScheme,
                    gameplayModifiers ?? new GameplayModifiers(),
                    playerSettings ?? new PlayerSpecificSettings(),
                    null,
                    "Menu",
                    false,
                    false,  /* TODO: start paused? Worth looking into to replace the old hacky function */
                    null,
                    null,
                    (StandardLevelScenesTransitionSetupDataSO standardLevelScenesTransitionSetupData, LevelCompletionResults results) => {
                        songFinishedCallback?.Invoke(standardLevelScenesTransitionSetupData, results);
                    },
                    null
                 };

                startStandardLevelMethod.Invoke(
                    _menuSceneSetupData,
                    parameters
                        .Take(startStandardLevelMethod.GetParameters().Length)
                        .ToArray());


                //_menuSceneSetupData.StartStandardLevel(
                //    gameMode: "Solo",
                //    difficultyBeatmap: loadedLevel.beatmapLevelData.GetDifficultyBeatmap(characteristic, difficulty),
                //    previewBeatmapLevel: loadedLevel,
                //    overrideEnvironmentSettings: overrideEnvironmentSettings,
                //    overrideColorScheme: colorScheme,
                //    gameplayModifiers: gameplayModifiers ?? new GameplayModifiers(),
                //    playerSpecificSettings: playerSettings ?? new PlayerSpecificSettings(),
                //    practiceSettings: null,
                //    backButtonText: "Menu",
                //    useTestNoteCutSoundEffects: false,
                //    startPaused: false,  /* TODO: start paused? Worth looking into to replace the old hacky function */
                //    beforeSceneSwitchCallback: null,
                //    afterSceneSwitchCallback: null,
                //    levelFinishedCallback: (standardLevelScenesTransitionSetupData, results) => songFinishedCallback?.Invoke(standardLevelScenesTransitionSetupData, results)
                //);
            };

            if ((level is PreviewBeatmapLevelSO && await HasDLCLevel(level.levelID)) ||
                        level is CustomPreviewBeatmapLevel)
            {
                Logger.Debug("Loading DLC/Custom level...");
                var result = await GetLevelFromPreview(level);
                if (result != null && !(result?.isError == true))
                {
                    SongLoaded(result?.beatmapLevel);
                }
            }
            else if (level is BeatmapLevelSO)
            {
                Logger.Debug("Reading OST data without songloader...");
                SongLoaded(level as IBeatmapLevel);
            }
            else
            {
                Logger.Debug($"Skipping unowned DLC ({level.songName})");
            }
        }

        public static async void LoadSong(string levelId, Action<IBeatmapLevel> loadedCallback)
        {
            IPreviewBeatmapLevel level = masterLevelList.Where(x => x.levelID == levelId).First();

            //Load IBeatmapLevel
            if (level is PreviewBeatmapLevelSO || level is CustomPreviewBeatmapLevel)
            {
                if (level is PreviewBeatmapLevelSO)
                {
                    if (!await HasDLCLevel(level.levelID)) return; //In the case of unowned DLC, just bail out and do nothing
                }

                var result = await GetLevelFromPreview(level);
                if (result != null && !(result?.isError == true))
                {
                    loadedCallback(result?.beatmapLevel);
                }
            }
            else if (level is BeatmapLevelSO)
            {
                loadedCallback(level as IBeatmapLevel);
            }
        }
    }
}
