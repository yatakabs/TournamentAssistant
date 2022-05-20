﻿#pragma warning disable 1998
using Discord;
using Discord.Interactions;
using Microsoft.EntityFrameworkCore.Internal;
using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TournamentAssistantCore.Discord.Helpers;
using TournamentAssistantCore.Discord.Services;
using TournamentAssistantShared;
using TournamentAssistantShared.BeatSaver;
using TournamentAssistantShared.Models;
using TournamentAssistantShared.Models.Packets;
using static TournamentAssistantShared.Models.GameplayModifiers;
using static TournamentAssistantShared.Models.PlayerSpecificSettings;
using static TournamentAssistantShared.Constants;

namespace TournamentAssistantCore.Discord.Modules
{
    public class QualifierModule : InteractionModuleBase
    {
        private static Random random = new();

        public DatabaseService DatabaseService { get; set; }
        public ScoresaberService ScoresaberService { get; set; }
        public SystemServerService ServerService { get; set; }

        private GameplayParameters FindSong(List<GameplayParameters> songPool, string levelId, string characteristic, int beatmapDifficulty, int gameOptions, int playerOptions)
        {
            return songPool.FirstOrDefault(x => x.Beatmap.LevelId == levelId && x.Beatmap.Characteristic.SerializedName == characteristic && x.Beatmap.Difficulty == beatmapDifficulty && x.GameplayModifiers.Options == (GameOptions)gameOptions && x.PlayerSettings.Options == (PlayerOptions)playerOptions);
        }

        private List<GameplayParameters> RemoveSong(List<GameplayParameters> songPool, string levelId, string characteristic, int beatmapDifficulty, int gameOptions, int playerOptions)
        {
            songPool.RemoveAll(x => x.Beatmap.LevelId == levelId && x.Beatmap.Characteristic.SerializedName == characteristic && x.Beatmap.Difficulty == beatmapDifficulty && x.GameplayModifiers.Options == (GameOptions)gameOptions && x.PlayerSettings.Options == (PlayerOptions)playerOptions);
            return songPool;
        }

        private string SanitizeSongId(string songId)
        {
            if (songId.StartsWith("https://beatsaver.com/") || songId.StartsWith("https://bsaber.com/"))
            {
                //Strip off the trailing slash if there is one
                if (songId.EndsWith("/")) songId = songId[..^1];

                //Strip off the beginning of the url to leave the id
                songId = songId[(songId.LastIndexOf("/", StringComparison.Ordinal) + 1)..];
            }

            if (songId.Contains('&'))
            {
                songId = songId[..songId.IndexOf("&", StringComparison.Ordinal)];
            }

            return songId;
        }

        private bool SongExists(List<GameplayParameters> songPool, string levelId, string characteristic, int beatmapDifficulty, int gameOptions, int playerOptions)
        {
            return FindSong(songPool, levelId, characteristic, beatmapDifficulty, gameOptions, playerOptions) != null;
        }

        [SlashCommand("create-event", "Create a Qualifier event for your guild")]
        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.ManageChannels)]
        public async Task CreateEventAsync(string name, string hostAddress, string infoChannelId, QualifierEvent.EventSettings settings = QualifierEvent.EventSettings.None)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(hostAddress))
            {
                await ReplyAsync(embed: "Usage: `/createEvent name: \"Event Name\" host: \"[host address]:[port]\"`\nTo find available hosts, please run `listHosts`\nYou can also set your desired event settings here. For example, add `settings: HideScoresFromPlayers` to the command!".ErrorEmbed());
            }
            else
            {
                var server = ServerService.GetServer();
                if (server == null)
                {
                    await ReplyAsync(embed: "The Server is not running, so we can't can't add events to it".ErrorEmbed());
                }
                else
                {
                    var host = server.State.KnownHosts.FirstOrDefault(x => $"{x.Address}:{x.Port}" == hostAddress);

                    var response = await server.SendCreateQualifierEvent(host, DatabaseService.DatabaseContext.ConvertDatabaseToModel(null, new Database.Event
                    {
                        EventId = Guid.NewGuid().ToString(),
                        GuildId = Context.Guild.Id,
                        GuildName = Context.Guild.Name,
                        Name = name,
                        InfoChannelId = ulong.Parse(infoChannelId ?? "0"),
                        Flags = (int)settings
                    }));
                    switch (response.Type)
                    {
                        case Response.ResponseType.Success:
                            await ReplyAsync(embed: response.Message.SuccessEmbed());
                            break;
                        case Response.ResponseType.Fail:
                            await ReplyAsync(embed: response.Message.ErrorEmbed());
                            break;
                        default:
                            await ReplyAsync(embed: "An unknown error occurred".ErrorEmbed());
                            break;
                    }
                }
            }
        }

        [SlashCommand("set-score-channel", "Sets a score channel for the ongoing event")]
        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.ManageChannels)]
        public async Task SetScoreChannelAsync(IGuildChannel channel, string eventId)
        {
            if (string.IsNullOrEmpty(eventId))
            {
                await ReplyAsync(embed: "Usage: `setScoreChannel #channel -eventId \"[event id]\"`\nTo find event ids, please run `listEvents`".ErrorEmbed());
            }
            else
            {
                var server = ServerService.GetServer();
                if (server == null)
                {
                    await ReplyAsync(embed: "The Server is not running, so we can't can't add events to it".ErrorEmbed());
                }
                else
                {
                    var knownPairs = await HostScraper.ScrapeHosts(server.State.KnownHosts.ToArray(), $"{server.CoreServer.Address}:{server.CoreServer.Port}", 0);
                    var targetPair = knownPairs.FirstOrDefault(x => x.Value.Events.Any(y => y.EventId.ToString() == eventId));

                    if (targetPair.Key != null)
                    {
                        var targetEvent = targetPair.Value.Events.First(x => x.EventId.ToString() == eventId);
                        targetEvent.InfoChannel = new TournamentAssistantShared.Models.Discord.Channel
                        {
                            Id = (int)(channel?.Id ?? 0),
                            Name = channel?.Name ?? ""
                        };

                        var response = await server.SendUpdateQualifierEvent(targetPair.Key, targetEvent);
                        if (response.Type == Response.ResponseType.Success)
                        {
                            await ReplyAsync(embed: response.Message.SuccessEmbed());
                        }
                        else if (response.Type == Response.ResponseType.Fail)
                        {
                            await ReplyAsync(embed: response.Message.ErrorEmbed());
                        }
                    }
                    else await ReplyAsync(embed: "Could not find an event with that ID".ErrorEmbed());
                }
            }
        }

        [SlashCommand("list-options", "Lists all available options for adding a song")]
        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.ManageChannels)]
        public async Task ListOptionsAsync()
        {
            var gameOptions = Enum.GetValues(typeof(GameOptions)).Cast<object>().Select(option => $"`{option}`").ToArray();
            var playerOptions = Enum.GetValues(typeof(PlayerOptions)).Cast<object>().Select(option => $"`{option}`").ToArray();

            await ReplyAsync(embed: $"Available Game Options: {string.Join(", ", gameOptions)}\nAvailable Player Options: {string.Join(", ", playerOptions)}".InfoEmbed());
        }

        [SlashCommand("add-song", "Add a song to the currently running event")]
        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.ManageChannels)]
        public async Task AddSongAsync(string eventId, string songId, BeatmapDifficulty difficulty, string characteristic, string gameOptionsString = null, string playerOptionsString = null)
        {
            //Load up the GameOptions and PlayerOptions
            var gameOptions = Enum.GetValues(typeof(GameOptions)).Cast<GameOptions>()
                .Where(o => !string.IsNullOrWhiteSpace(gameOptionsString.ParseArgs(o.ToString())))
                .Aggregate(GameOptions.None, (current, o) => current | o);

            var playerOptions = Enum.GetValues(typeof(PlayerOptions)).Cast<PlayerOptions>()
                .Where(o => !string.IsNullOrWhiteSpace(playerOptionsString.ParseArgs(o.ToString())))
                .Aggregate(PlayerOptions.None, (current, o) => current | o);

            //Sanitize input
            songId = SanitizeSongId(songId);

            var server = ServerService.GetServer();
            if (server == null)
            {
                await ReplyAsync(embed: "The Server is not running, so we can't can't add songs to it".ErrorEmbed());
            }
            else
            {
                //Get the hash for the song
                var hash = BeatSaverDownloader.GetHashFromID(songId);
                var knownPairs = await HostScraper.ScrapeHosts(server.State.KnownHosts.ToArray(), $"{server.CoreServer.Address}:{server.CoreServer.Port}", 0);
                var targetPair = knownPairs.FirstOrDefault(x => x.Value.Events.Any(y => y.EventId.ToString() == eventId));
                var targetEvent = targetPair.Value.Events.FirstOrDefault(x => x.EventId.ToString() == eventId);
                if (targetEvent == null)
                {
                    await ReplyAsync(embed: "Could not find an event with that ID".ErrorEmbed());
                    return;
                }

                var songPool = targetEvent.QualifierMaps.ToList();

                GameplayParameters parameters = new()
                {
                    GameplayModifiers = new GameplayModifiers
                    {
                        Options = gameOptions
                    },
                    PlayerSettings = new PlayerSpecificSettings
                    {
                        Options = playerOptions
                    }
                };

                int responseType;
                bool exists;
                string songName;

                if (OstHelper.IsOst(hash))
                {
                    exists = SongExists(songPool, hash, characteristic, (int)difficulty, (int)gameOptions, (int)playerOptions);
                    songName = OstHelper.GetOstSongNameFromLevelId(hash);
                    parameters.Beatmap = new Beatmap
                    {
                        Name = songName,
                        LevelId = hash,
                        Characteristic = new Characteristic
                        {
                            SerializedName = characteristic
                        },
                        Difficulty = (int)difficulty
                    };
                    responseType = 0;
                }
                else
                {
                    var songInfo = await BeatSaverDownloader.GetSongInfo(songId);
                    songName = songInfo.name;

                    if (!songInfo.HasDifficulty(characteristic, difficulty))
                    {
                        difficulty = songInfo.GetClosestDifficultyPreferLower(characteristic, difficulty);
                        responseType = 1;
                    }
                    else
                    {
                        responseType = 2;

                    }

                    exists = SongExists(songPool, $"custom_level_{hash.ToUpper()}", characteristic, (int)difficulty, (int)gameOptions, (int)playerOptions);


                    parameters.Beatmap = new Beatmap
                    {
                        Name = songName,
                        LevelId = $"custom_level_{hash.ToUpper()}",
                        Characteristic = new Characteristic
                        {
                            SerializedName = characteristic
                        },
                        Difficulty = (int)difficulty
                    };
                }

                if (!exists)
                {
                    if (responseType == 1)
                        await ReplyAsync(embed: $"{songName} doesn't have that difficulty, and {difficulty} is already in the event".ErrorEmbed());
                    else
                        await ReplyAsync(embed: "Song is already active in the database".ErrorEmbed());
                    return;
                }

                songPool.Add(parameters);
                targetEvent.QualifierMaps.Clear();
                targetEvent.QualifierMaps.AddRange(songPool);

                var response = await server.SendUpdateQualifierEvent(targetPair.Key, targetEvent);
                switch (response.Type)
                {
                    case Response.ResponseType.Success:
                        var replyString = responseType switch
                        {
                            0 => $"Added: {parameters.Beatmap.Name} ({difficulty}) ({characteristic})",
                            1 => $"{songName} doesn't have that difficulty, using {difficulty} instead.\nAdded to the song list",
                            2 => $"{songName} ({difficulty}) ({characteristic}) downloaded and added to song list",
                            _ => throw new ArgumentOutOfRangeException()
                        };

                        await ReplyAsync(embed: (replyString + 
                                                 $"{(gameOptions != GameOptions.None ? $" with game options: ({gameOptions})" : "")}" +
                                                 $"{(playerOptions != PlayerOptions.None ? $" with player options: ({playerOptions})" : "!")}").SuccessEmbed());
                        break;
                    case Response.ResponseType.Fail:
                        await ReplyAsync(embed: response.Message.ErrorEmbed());
                        break;
                    default:
                        await ReplyAsync(embed: "An unknown error occurred".ErrorEmbed());
                        break;
                }
            }
        }

        [SlashCommand("list-songs", "List the currently active songs for the current event")]
        [RequireContext(ContextType.Guild)]
        public async Task ListSongsAsync(string eventId)
        {
            var server = ServerService.GetServer();
            if (server == null)
            {
                await ReplyAsync(embed: "The Server is not running, so we can't can't get any event info".ErrorEmbed());
            }
            else
            {
                if (string.IsNullOrEmpty(eventId))
                {
                    await ReplyAsync(embed: ("Usage: `listSongs -eventId \"[event id]\"`\n" +
                        "To find event ids, please run `listEvents`\n").ErrorEmbed());
                    return;
                }

                var knownPairs = await HostScraper.ScrapeHosts(server.State.KnownHosts.ToArray(), $"{server.CoreServer.Address}:{server.CoreServer.Port}", 0);
                var targetPair = knownPairs.FirstOrDefault(x => x.Value.Events.Any(y => y.EventId.ToString() == eventId));
                var targetEvent = targetPair.Value.Events.FirstOrDefault(x => x.EventId.ToString() == eventId);
                var songPool = targetEvent.QualifierMaps.ToList();

                var builder = new EmbedBuilder
                {
                    Title = "<:page_with_curl:735592941338361897> Song List",
                    Color = new Color(random.Next(255), random.Next(255), random.Next(255))
                };

                var titleField = new EmbedFieldBuilder
                {
                    Name = "Title",
                    Value = "```",
                    IsInline = true
                };

                var difficultyField = new EmbedFieldBuilder
                {
                    Name = "Difficulty",
                    Value = "```",
                    IsInline = true
                };

                var modifierField = new EmbedFieldBuilder
                {
                    Name = "Modifiers",
                    Value = "```",
                    IsInline = true
                };

                foreach (var song in songPool)
                {
                    titleField.Value += $"\n{song.Beatmap.Name}";
                    difficultyField.Value += $"\n{song.Beatmap.Difficulty}";
                    modifierField.Value += $"\n{song.GameplayModifiers.Options}";
                }

                titleField.Value += "```";
                difficultyField.Value += "```";
                modifierField.Value += "```";

                builder.AddField(titleField);
                builder.AddField(difficultyField);
                builder.AddField(modifierField);

                await ReplyAsync(embed: builder.Build());
            }
        }

        [SlashCommand("remove-song", "Remove a song from the currently running event")]
        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.ManageChannels)]
        public async Task RemoveSongAsync(string eventId, string songId, BeatmapDifficulty difficulty, string characteristic, string gameOptionsString = null, string playerOptionsString = null)
        {
            var server = ServerService.GetServer();
            if (server == null)
            {
                await ReplyAsync(embed: "The Server is not running, so we can't can't get any event info".ErrorEmbed());
            }
            else
            {
                if (string.IsNullOrEmpty(eventId))
                {
                    await ReplyAsync(embed: ("Usage: `/removeSong eventId: \"[event id]\" song: [song link]`\n" +
                        "To find event ids, please run `listEvents`\n" +
                        "Note: You may also need to include difficulty and modifier info to be sure you remove the right song").ErrorEmbed());
                    return;
                }

                var knownPairs = await HostScraper.ScrapeHosts(server.State.KnownHosts.ToArray(), $"{server.CoreServer.Address}:{server.CoreServer.Port}", 0);
                var targetPair = knownPairs.FirstOrDefault(x => x.Value.Events.Any(y => y.EventId.ToString() == eventId));
                var targetEvent = targetPair.Value.Events.FirstOrDefault(x => x.EventId.ToString() == eventId);
                var songPool = targetEvent.QualifierMaps.ToList();

                var gameOptions = Enum.GetValues(typeof(GameOptions)).Cast<GameOptions>()
                    .Where(o => !string.IsNullOrWhiteSpace(gameOptionsString.ParseArgs(o.ToString())))
                    .Aggregate(GameOptions.None, (current, o) => current | o);

                var playerOptions = Enum.GetValues(typeof(PlayerOptions)).Cast<PlayerOptions>()
                    .Where(o => !string.IsNullOrWhiteSpace(playerOptionsString.ParseArgs(o.ToString())))
                    .Aggregate(PlayerOptions.None, (current, o) => current | o);

                //Sanitize input
                songId = SanitizeSongId(songId);

                //Get the hash for the song
                var hash = BeatSaverDownloader.GetHashFromID(songId);

                var song = FindSong(songPool, $"custom_level_{hash.ToUpper()}", characteristic, (int)difficulty, (int)gameOptions, (int)playerOptions);
                if (song != null)
                {
                    targetEvent.QualifierMaps.Clear();
                    targetEvent.QualifierMaps.AddRange(RemoveSong(songPool, $"custom_level_{hash.ToUpper()}", characteristic, (int)difficulty, (int)gameOptions, (int)playerOptions).ToArray());

                    var response = await server.SendUpdateQualifierEvent(targetPair.Key, targetEvent);
                    switch (response.Type)
                    {
                        case Response.ResponseType.Success:
                            await ReplyAsync(embed: ($"Removed {song.Beatmap.Name} ({difficulty}) ({characteristic}) from the song list" +
                                                     $"{(gameOptions != GameOptions.None ? $" with game options: ({gameOptions})" : "")}" +
                                                     $"{(playerOptions != PlayerOptions.None ? $" with player options: ({playerOptions})" : "!")}").SuccessEmbed());
                            break;
                        case Response.ResponseType.Fail:
                            await ReplyAsync(embed: response.Message.ErrorEmbed());
                            break;
                        default:
                            await ReplyAsync(embed: "An unknown error occurred".ErrorEmbed());
                            break;
                    }
                }
                else await ReplyAsync(embed: $"Specified song does not exist with that difficulty / characteristic / gameOptions / playerOptions ({difficulty} {characteristic} {gameOptions} {playerOptions})".ErrorEmbed());
            }

        }

        [SlashCommand("end-event", "End the current event")]
        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.ManageChannels)]
        public async Task EndEventAsync(string eventId)
        {
            //Make server backup
            /*Logger.Warning($"BACKING UP DATABASE...");
            File.Copy("BotDatabase.db", $"EventDatabase_bak_{DateTime.Now.Day}_{DateTime.Now.Hour}_{DateTime.Now.Minute}_{DateTime.Now.Second}.db");
            Logger.Success("Database backed up succsessfully.");*/

            var server = ServerService.GetServer();
            if (server == null)
            {
                await ReplyAsync(embed: "The Server is not running, so we can't can't get any event info".ErrorEmbed());
            }
            else
            {
                if (string.IsNullOrEmpty(eventId))
                {
                    await ReplyAsync(embed: ("Usage: `/endEvent eventId: \"[event id]\"`\n" +
                        "To find event ids, please run `listEvents`").ErrorEmbed());
                    return;
                }

                var knownPairs = await HostScraper.ScrapeHosts(server.State.KnownHosts.ToArray(), $"{server.CoreServer.Address}:{server.CoreServer.Port}", 0);
                var targetPair = knownPairs.FirstOrDefault(x => x.Value.Events.Any(y => y.EventId.ToString() == eventId));
                var targetEvent = targetPair.Value.Events.FirstOrDefault(x => x.EventId.ToString() == eventId);

                var response = await server.SendDeleteQualifierEvent(targetPair.Key, targetEvent);
                switch (response.Type)
                {
                    case Response.ResponseType.Success:
                        await ReplyAsync(embed: response.Message.SuccessEmbed());
                        break;
                    case Response.ResponseType.Fail:
                        await ReplyAsync(embed: response.Message.ErrorEmbed());
                        break;
                    default:
                        await ReplyAsync(embed: "An unknown error occurred".ErrorEmbed());
                        break;
                }
            }
        }

        [SlashCommand("list-events", "Show all events we can find info about")]
        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.ManageChannels)]
        public async Task ListEventsAsync()
        {
            var server = ServerService.GetServer();
            if (server == null)
            {
                await ReplyAsync(embed: "The Server is not running, so we can't can't get any event info".ErrorEmbed());
            }
            else
            {
                var knownEvents = (await HostScraper.ScrapeHosts(server.State.KnownHosts.ToArray(), $"{server.CoreServer.Address}:{server.CoreServer.Port}", 0)).Select(x => x.Value).Where(x => x.Events != null).SelectMany(x => x.Events);

                var builder = new EmbedBuilder
                {
                    Title = "<:page_with_curl:735592941338361897> Events",
                    Color = new Color(random.Next(255), random.Next(255), random.Next(255))
                };

                foreach (var @event in knownEvents)
                {
                    builder.AddField(@event.Name, $"```fix\n{@event.EventId}```\n" +
                        $"```css\n({@event.Guild.Name})```", true);
                }

                await ReplyAsync(embed: builder.Build());
            }
        }

        [SlashCommand("list-hosts", "Show all hosts we can find info about")]
        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.ManageChannels)]
        public async Task ListHostsAsync()
        {
            var server = ServerService.GetServer();
            if (server == null)
            {
                await ReplyAsync(embed: "The Server is not running, so we can't can't get any host info".ErrorEmbed());
            }
            else
            {
                var builder = new EmbedBuilder
                {
                    Title = "<:page_with_curl:735592941338361897> Hosts",
                    Color = new Color(random.Next(255), random.Next(255), random.Next(255))
                };

                foreach (var host in server.State.KnownHosts)
                {
                    builder.AddField(host.Name, $"```\n{host.Address}:{host.Port}```", true);
                }

                await ReplyAsync(embed: builder.Build());
            }
        }

        [SlashCommand("dumb-leaderboards", "Show leaderboards from the currently running event, unformatted to allow for larger messages")]
        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.ManageChannels)]
        public async Task DumbLeaderboardsAsync(string eventId)
        {
            var server = ServerService.GetServer();
            if (server == null)
            {
                await ReplyAsync(embed: "The Server is not running, so we can't can't get any host info".ErrorEmbed());
            }
            else
            {
                var knownPairs = await HostScraper.ScrapeHosts(server.State.KnownHosts.ToArray(), $"{server.CoreServer.Address}:{server.CoreServer.Port}", 0);
                var targetPair = knownPairs.FirstOrDefault(x => x.Value.Events.Any(y => y.EventId.ToString() == eventId));
                var targetEvent = targetPair.Value.Events.FirstOrDefault(x => x.EventId.ToString() == eventId);

                var playerNames = new List<string>();
                var playerScores = new List<string>();

                var leaderboardText = string.Empty;
                foreach (var map in targetEvent.QualifierMaps)
                {
                    var scores = (await HostScraper.RequestResponse(targetPair.Key, new Packet
                    {
                        ScoreRequest = new ScoreRequest
                        {
                            EventId = eventId,
                            Parameters = map
                        }
                    },
                    Packet.packetOneofCase.ScoreRequestResponse,
                    $"{server.CoreServer.Address}:{server.CoreServer.Port}", 0)).ScoreRequestResponse;

                    leaderboardText += $"{map.Beatmap.Name}:\n```{string.Join("\n", scores.Scores.Select(x => $"{x.Username} {x.score} {(x.FullCombo ? "FC" : "")}\n"))}```";
                }

                await ReplyAsync(leaderboardText);
            }
        }

        [SlashCommand("excel-leaderboards", "Show leaderboards from the currently running event, exported to excel")]
        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.ManageChannels)]
        public async Task ExcelLeaderboardsAsync(string eventId)
        {
            var server = ServerService.GetServer();
            if (server == null)
            {
                await ReplyAsync(embed: "The Server is not running, so we can't can't get any host info".ErrorEmbed());
            }
            else
            {
                ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
                var excel = new ExcelPackage();

                var knownPairs = await HostScraper.ScrapeHosts(server.State.KnownHosts.ToArray(), $"{server.CoreServer.Address}:{server.CoreServer.Port}", 0);
                var targetPair = knownPairs.FirstOrDefault(x => x.Value.Events.Any(y => y.EventId.ToString() == eventId));
                var targetEvent = targetPair.Value.Events.FirstOrDefault(x => x.EventId.ToString() == eventId);

                foreach (var map in targetEvent.QualifierMaps)
                {
                    var workSheet = excel.Workbook.Worksheets.Add(map.Beatmap.Name);
                    var scores = (await HostScraper.RequestResponse(targetPair.Key, new Packet
                    {
                        ScoreRequest = new ScoreRequest
                        {
                            EventId = eventId,
                            Parameters = map
                        }
                    },
                    Packet.packetOneofCase.ScoreRequestResponse,
                    $"{server.CoreServer.Address}:{server.CoreServer.Port}", 0)).ScoreRequestResponse;

                    var row = 0;
                    foreach (var score in scores.Scores)
                    {
                        row++;
                        workSheet.SetValue(row, 1, score.score);
                        workSheet.SetValue(row, 2, score.Username);
                        workSheet.SetValue(row, 3, score.FullCombo ? "FC" : "");
                    }
                }

                await Context.Channel.SendFileAsync(new MemoryStream(excel.GetAsByteArray()), "Leaderboards.xlsx");
            }
        }

        [SlashCommand("leaderboards", "Show leaderboards from the currently running event")]
        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.ManageChannels)]
        public async Task LeaderboardsAsync(string eventId)
        {
            var server = ServerService.GetServer();
            if (server == null)
            {
                await ReplyAsync(embed: "The Server is not running, so we can't can't get any host info".ErrorEmbed());
            }
            else
            {
                var knownPairs = await HostScraper.ScrapeHosts(server.State.KnownHosts.ToArray(), $"{server.CoreServer.Address}:{server.CoreServer.Port}", 0);
                var targetPair = knownPairs.FirstOrDefault(x => x.Value.Events.Any(y => y.EventId.ToString() == eventId));
                var targetEvent = targetPair.Value.Events.FirstOrDefault(x => x.EventId.ToString() == eventId);

                var builder = new EmbedBuilder
                {
                    Title = "<:page_with_curl:735592941338361897> Leaderboards",
                    Color = new Color(random.Next(255), random.Next(255), random.Next(255))
                };

                var playerNames = new List<string>();
                var playerScores = new List<string>();

                foreach (var map in targetEvent.QualifierMaps)
                {
                    var scores = (await HostScraper.RequestResponse(targetPair.Key, new Packet
                    {
                        ScoreRequest = new ScoreRequest
                        {
                            EventId = eventId,
                            Parameters = map
                        }
                    },
                    Packet.packetOneofCase.ScoreRequestResponse,
                    $"{server.CoreServer.Address}:{server.CoreServer.Port}", 0)).ScoreRequestResponse;

                    builder.AddField(map.Beatmap.Name, $"```\n{string.Join("\n", scores.Scores.Select(x => $"{x.Username} {x.score} {(x.FullCombo ? "FC" : "")}\n"))}```", true);
                }

                await ReplyAsync(embed: builder.Build());
            }
        }
    }
}
