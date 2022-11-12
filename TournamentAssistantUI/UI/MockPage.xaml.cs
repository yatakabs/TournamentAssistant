﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using TournamentAssistantShared;
using TournamentAssistantShared.Models;
using TournamentAssistantShared.Models.Packets;
using TournamentAssistantShared.Utilities;
using TournamentAssistantUI.Misc;

namespace TournamentAssistantUI.UI
{
    /// <summary>
    /// Interaction logic for MockClient.xaml
    /// </summary>
    public partial class MockPage : Page
    {
        private static Random r = new Random();

        private List<MockClient> mockPlayers;
        private ScoreboardClient scoreboardClient;

        public MockPage()
        {
            InitializeComponent();
        }

        private struct MockPlayer
        {
            public string Name { get; set; }
            public string UserId { get; set; }
        }

        /*List<Player> availableIds = new List<Player>(new Player[] {
                new Player()
                {
                    Name = "Astrella",
                    UserId = "2538637699496776"
                },
                new Player()
                {
                    Name = "AtomicX",
                    UserId = "76561198070511128"
                },
                new Player()
                {
                    Name = "Garsh",
                    UserId = "76561198187936410"
                },
                new Player()
                {
                    Name = "LSToast",
                    UserId = "76561198167393974"
                },
                new Player()
                {
                    Name = "CoolingCloset",
                    UserId = "76561198180044686"
                },
                new Player()
                {
                    Name = "miitchel",
                    UserId = "76561198301082541"
                },
                new Player()
                {
                    Name = "Shadow Ai",
                    UserId = "76561198117675143"
                },
                new Player()
                {
                    Name = "Silverhaze",
                    UserId = "76561198033166451"
                },
            });*/

        private MockPlayer GetRandomPlayer()
        {
            /*var ret = availableIds.ElementAt(0);
            availableIds.RemoveAt(0);
            return ret;*/
            return new MockPlayer()
            {
                Name = GenerateName(),
                UserId = $"{r.Next(int.MaxValue)}"
            };
        }

        private void Connect_Click(object sender, RoutedEventArgs e)
        {
            var clientCountValid = int.TryParse(ClientCountBox.Text, out var clientsToConnect);
            if (!clientCountValid)
            {
                return;
            }

            if (mockPlayers != null)
            {
                mockPlayers.ForEach(x => x.Shutdown());
            }

            mockPlayers = new List<MockClient>();

            var hostText = HostBox.Text.Split(':');

            for (int i = 0; i < clientsToConnect; i++)
            {
                var player = GetRandomPlayer();
                mockPlayers.Add(new MockClient(hostText[0], hostText.Length > 1 ? int.Parse(hostText[1]) : 2052, player.Name, player.UserId));
            }

            mockPlayers.ForEach(x => Task.Run(x.Start));
        }

        private void Disconnect_Click(object sender, RoutedEventArgs e)
        {
            mockPlayers.ForEach(x => x.Shutdown());
        }

        private static string GenerateName(int desiredLength = -1)
        {
            string[] consonants = { "b", "c", "d", "f", "g", "h", "j", "k", "l", "m", "l", "n", "p", "q", "r", "s", "sh", "zh", "t", "v", "w", "x" };
            string[] vowels = { "a", "e", "i", "o", "u", "ae", "y" };

            if (desiredLength < 0)
            {
                desiredLength = r.Next(6, 20);
            }

            string name = string.Empty;

            for (int i = 0; i < desiredLength; i++)
            {
                name += i % 2 == 0 ? consonants[r.Next(consonants.Length)] : vowels[r.Next(vowels.Length)];
                if (i == 0)
                {
                    name = name.ToUpper();
                }
            }

            return name;
        }

        private void QRButton_Click(object sender, RoutedEventArgs e)
        {
            var navigationService = NavigationService.GetNavigationService(this);
            navigationService.Navigate(new QRPage());
        }

        private async void Scoreboard_Connect_Click(object sender, RoutedEventArgs e)
        {
            scoreboardClient = new ScoreboardClient("jbsl-ta.yatakabs.com", 2052);
            await scoreboardClient.Start();

            scoreboardClient.RealtimeScoreReceived += ScoreboardClient_RealtimeScoreReceived;
            scoreboardClient.PlaySongSent += ScoreboardClient_PlaySongSent;
        }

        private void ScoreboardClient_PlaySongSent()
        {
            Dispatcher.Invoke(() => ResetLeaderboardClicked(null, null));
        }

        private List<(User, Push.RealtimeScore)> seenPlayers = new();
        private async Task ScoreboardClient_RealtimeScoreReceived(Push.RealtimeScore realtimeScore)
        {
            var player = scoreboardClient.State.Users.FirstOrDefault(x => x.Guid == realtimeScore.UserGuid);
            if (player.StreamDelayMs > 10)
            {
                await Task.Delay((int)player.StreamDelayMs);
            }

            lock (seenPlayers)
            {
                if (!seenPlayers.Any(x => x.Item1.UserEquals(player)))
                {
                    seenPlayers.Add((player, new Push.RealtimeScore()));
                }
                else
                {
                    var playerInList = seenPlayers.Find(x => x.Item1.UserEquals(player));
                    playerInList.Item2.ScoreWithModifiers = realtimeScore.ScoreWithModifiers;
                    playerInList.Item2.Accuracy = realtimeScore.Accuracy;
                }

                ScoreboardListBox.Dispatcher.Invoke(() =>
                {
                    seenPlayers = seenPlayers.OrderByDescending(x => x.Item2.Accuracy).ToList();
                    ScoreboardListBox.Items.Clear();
                    for (var i = 0; i < 20 && i < seenPlayers.Count; i++)
                    {
                        ScoreboardListBox.Items.Add($"{i + 1}: {seenPlayers[i].Item1.Name} \t {seenPlayers[i].Item2.ScoreWithModifiers} \t {seenPlayers[i].Item2.Accuracy.ToString("P", CultureInfo.InvariantCulture)}");
                    }
                });

                FlopListBox.Dispatcher.Invoke(() =>
                {
                    seenPlayers = seenPlayers.OrderBy(x => x.Item2.Accuracy).ToList();
                    FlopListBox.Items.Clear();
                    var tempList = new List<(User, Push.RealtimeScore)>();
                    for (var i = 0; i < 20 && i < seenPlayers.Count; i++)
                    {
                        tempList.Add(seenPlayers[i]);
                    }

                    tempList.Reverse();
                    for (var i = 0; i < 20 && i < tempList.Count; i++)
                    {
                        FlopListBox.Items.Add($"{Math.Max(seenPlayers.Count - 20, 0) + (i + 1)}: {tempList[i].Item1.Name} \t {tempList[i].Item2.ScoreWithModifiers} \t {tempList[i].Item2.Accuracy.ToString("P", CultureInfo.InvariantCulture)}");
                    }
                });
            }
        }

        private void ResetLeaderboardClicked(object sender, RoutedEventArgs e)
        {
            seenPlayers.Clear();
            ScoreboardListBox.Items.Clear();
        }

        private async void QualsScoreButton_Clicked(object sender, RoutedEventArgs e)
        {
            var response = (
                await HostScraper.RequestResponse(
                    new CoreServer
                    {
                        Address = "jbsl-ta.yatakabs.com",
                        Port = 2052,
                        Name = "JBSL 4"
                    },
                    new Packet
                    {
                        Push = new Push
                        {
                            leaderboard_score = new Push.LeaderboardScore
                            {
                                Score = new LeaderboardScore
                                {
                                    EventId = "333aa572-672c-4bf8-ae46-593faccb64da",
                                    Parameters = new GameplayParameters
                                    {
                                        Beatmap = new Beatmap
                                        {
                                            Characteristic = new Characteristic
                                            {
                                                SerializedName = "Standard"
                                            },
                                            Difficulty = (int)Constants.BeatmapDifficulty.Easy,
                                            LevelId = "custom_level_0B85BFB7912ADB4D6C42393AE350A6EAEF8E6AFC"
                                        },
                                        GameplayModifiers = new GameplayModifiers
                                        {
                                            Options = GameplayModifiers.GameOptions.NoFail
                                        },
                                        PlayerSettings = new PlayerSpecificSettings()
                                    },
                                    UserId = "76561198063268251",
                                    Username = "Moon",
                                    FullCombo = true,
                                    Score = int.Parse(ScoreBox.Text),
                                    Color = "#ffffff"
                                }
                            }
                        }
                    },
                    "Moon",
                    76561198063268251));

            var scores = response.Response.leaderboard_scores.Scores;

            ScoreboardListBox.Dispatcher.Invoke(() =>
            {
                var index = 0;
                ScoreboardListBox.Items.Clear();
                foreach (var score in scores)
                {
                    ScoreboardListBox.Items.Add($"{++index}: {score.Username} \t {score.Score}");
                }
            });
        }
    }
}