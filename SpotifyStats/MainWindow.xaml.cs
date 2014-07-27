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
            return ArtistName + " – " + SongName;
        }
    }

    internal class SongContext: DbContext {
        public SongContext(string connectionString): base(connectionString) {
        }

        public DbSet<Song> Songs { get; set; }
    }


    /// <summary>
    ///     Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow: Window {
        private const string DATABASE_NAME = "stats.db";
        private const char SONG_SEPARATOR = '–';
        private readonly SongContext dataContext;
        private readonly List<Song> newSongs;
        private readonly int refreshInterval;
        private int spotifyProcessId;

        public MainWindow() {
            InitializeComponent();
            PresentationTraceSources.SetTraceLevel(dataGrid.ItemContainerGenerator, PresentationTraceLevel.High);
            newSongs = new List<Song>();
            refreshInterval = 5;

            var spotifyId = findSpotifyProcessId();
            if (!spotifyId.HasValue) {
                // TODO NotifyArea or whatever
            }
            else {
                spotifyProcessId = spotifyId.Value;
            }
            if (File.Exists(DATABASE_NAME)) {
                Debug.WriteLine(Path.GetFullPath(DATABASE_NAME));
                // Load database
                dataContext = new SongContext(DATABASE_NAME);
                Debug.WriteLine("loading existing database");
            }
            else {
                // Create new database, then load it
                SQLiteConnection.CreateFile(DATABASE_NAME);
                dataContext = new SongContext(DATABASE_NAME);
                Debug.WriteLine("creating new database");
            }
            dataGrid.ItemsSource = newSongs;
        }

        private static int? findSpotifyProcessId() {
            var processes = Process.GetProcesses();
            var spotify = processes.FirstOrDefault(p => p.MainWindowTitle.StartsWith("Spotify - "));
            if (spotify == null) {
                return null;
            }
            return spotify.Id;
        }

        private static async Task RepeatActionEvery(Action action, TimeSpan interval,
                                                    CancellationToken cancellationToken) {
            while (true) {
                action();
                var task = Task.Delay(interval, cancellationToken);
                try {
                    await task;
                }
                catch (TaskCanceledException) {
                    return;
                }
            }
        }

        private async void MainWindow_OnContentRendered(object sender, EventArgs e) {
            Action getSong = () => {
                try {
                    var result = Process.GetProcessById(spotifyProcessId);
                    var song = result.MainWindowTitle.Substring(10);
                    var lastSong = newSongs.LastOrDefault();
                    if (lastSong == null || lastSong.ToString() != song) {
                        var split = song.Split(SONG_SEPARATOR);
                        var artist = split[0].Trim();
                        var songName = split[1].Trim();
                        var s = new Song(artist, songName);
                        newSongs.Add(s);
                        dataGrid.Items.Refresh();
                        dataContext.Songs.Add(s);
                        dataContext.SaveChanges();
                    }
                    // If GetProcessById fails, try to find the Spotify process, try to find it again.
                }
                catch (ArgumentException) {
                    var id = findSpotifyProcessId();
                    if (id.HasValue) {
                        spotifyProcessId = id.Value;
                    }
                }
            };
            await RepeatActionEvery(getSong, TimeSpan.FromSeconds(refreshInterval), new CancellationToken());
        }
    }
}