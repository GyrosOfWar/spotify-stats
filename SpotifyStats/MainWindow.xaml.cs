using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Entity;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace SpotifyStats {
    [Table("Song")]
    internal class Song {
        public Song(string artist, string song) {
            TimeStamp = DateTime.Now;
            ArtistName = artist;
            SongName = song;
        }

        public Song() {
        }

        [Key]
        public int Id { get; set; }

        public DateTime TimeStamp { get; set; }
        public string ArtistName { get; set; }
        public string SongName { get; set; }

        public override string ToString() {
            return ArtistName + " " + MainWindow.SONG_SEPARATOR + " " + SongName;
        }
    }

    internal class SongContext : DbContext {
        public SongContext(string connectionString)
            : base(connectionString) {
        }

        public DbSet<Song> Songs { get; set; }
    }


    /// <summary>
    ///     Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow {
        private const string DATABASE_NAME = "stats.sqlite";
        public const char SONG_SEPARATOR = '–';
        private readonly SongContext dataContext;
        private readonly List<Song> newSongs;
        private readonly int refreshInterval;
        private bool currentlyUpdating;
        private int spotifyProcessId;
        private CancellationTokenSource tokenSource;
        private DisplayMode currentMode;

        private enum DisplayMode {
            RecentSongs = 0,
            MostPlayedArtists = 1
        }

        public MainWindow() {
            InitializeComponent();
            newSongs = new List<Song>();
            refreshInterval = 5;
            var spotifyId = findSpotifyProcessId();
            if (!spotifyId.HasValue) {
                statusText.Text = "Spotify not running/not playing a song.";
            } else {
                spotifyProcessId = spotifyId.Value;
            }
            if (File.Exists(DATABASE_NAME)) {
                // Load database
                dataContext = new SongContext(DATABASE_NAME);
                Debug.WriteLine("loading existing database");
            } else {
                // Create new database, then load it
                SQLiteConnection.CreateFile(DATABASE_NAME);
                dataContext = new SongContext(DATABASE_NAME);
                Debug.WriteLine("creating new database");
            }
            dataGrid.ItemsSource = newSongs;
            currentlyUpdating = true;
            tokenSource = new CancellationTokenSource();
            currentMode = DisplayMode.RecentSongs;
        }

        private void getSong() {
            try {
                var result = Process.GetProcessById(spotifyProcessId);
                var song = result.MainWindowTitle.Substring(10);
                var lastSong = newSongs.FirstOrDefault();
                if (lastSong == null || lastSong.ToString() != song) {
                    var s = parseSong(result);
                    persistSong(s);
                }
                // If GetProcessById fails, try to find the Spotify process, try to find it again.
            } catch (ArgumentException) {
                var id = findSpotifyProcessId();
                if (id.HasValue) {
                    spotifyProcessId = id.Value;
                    statusText.Text = "";

                    var result = Process.GetProcessById(spotifyProcessId);
                    var s = parseSong(result);
                    persistSong(s);
                } else {
                    statusText.Text = "Spotify not running/not playing a song.";
                }
            }

        }

        private static int? findSpotifyProcessId() {
            var processes = Process.GetProcesses();
            var spotify = processes.FirstOrDefault(p => p.MainWindowTitle.StartsWith("Spotify - "));
            if (spotify == null) {
                return null;
            }
            return spotify.Id;
        }

        private IEnumerable<Tuple<string, int>> mostPlayedArtists() {
            // ugh
            var allSongs = dataContext.Songs.ToList();
            var counts = allSongs.Select(
                song => allSongs.Count(s => s.ArtistName == song.ArtistName));
            var result = allSongs.Zip(counts, (s, t) => Tuple.Create(s.ArtistName, t)).Distinct().OrderByDescending(s => s.Item2);

            return result;
        }

        private static async Task RepeatActionEvery(Action action, TimeSpan interval,
                                                    CancellationToken cancellationToken) {
            while (true) {
                action();
                var task = Task.Delay(interval, cancellationToken);
                try {
                    await task;
                } catch (TaskCanceledException) {
                    return;
                }
            }
        }

        private static Song parseSong(Process process) {
            var song = process.MainWindowTitle.Substring(10);
            var split = song.Split(SONG_SEPARATOR);
            var artist = split[0].Trim();
            var songName = split[1].Trim();
            return new Song(artist, songName);
        }

        private void persistSong(Song s) {
            var stopwatch = Stopwatch.StartNew();
            newSongs.Insert(0, s);
            if (currentMode == DisplayMode.RecentSongs) {
                dataGrid.Items.Refresh();
            }
            dataContext.Songs.Add(s);
            dataContext.SaveChanges();
            stopwatch.Stop();
            Debug.WriteLine("persistSong: " + stopwatch.Elapsed);
        }

        private async void MainWindow_Loaded(object sender, EventArgs e) {
            await RepeatActionEvery(getSong, TimeSpan.FromSeconds(refreshInterval), tokenSource.Token);
        }

        private async void UpdatingButton_Click(object sender, RoutedEventArgs e) {
            if (currentlyUpdating) {
                currentlyUpdating = false;
                updatingButton.Content = "Start updating";
                tokenSource.Cancel();
            } else {
                currentlyUpdating = true;
                updatingButton.Content = "Stop updating";
                tokenSource = new CancellationTokenSource();
                await RepeatActionEvery(getSong, TimeSpan.FromSeconds(refreshInterval), tokenSource.Token);
            }
        }

        private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            switch (modeSelector.SelectedIndex) {
                case 0:
                    currentMode = DisplayMode.RecentSongs;
                    dataGrid.ItemsSource = newSongs;
                    break;
                case 1:
                    currentMode = DisplayMode.MostPlayedArtists;
                    var mostPlayed = mostPlayedArtists();
                    dataGrid.ItemsSource = mostPlayed;
                    break;
            }
        }
    }
}