﻿using Newtonsoft.Json;
using SimpleJSON;
using SongLoaderPlugin;
using SongLoaderPlugin.OverrideClasses;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace BeatSaverDownloader.Misc
{
    public static class PlaylistsCollection
    {
        public static List<Playlist> loadedPlaylists = new List<Playlist>();

        public static void ReloadPlaylists()
        {
            try
            {
                loadedPlaylists.Clear();

                List<string> playlistFiles = new List<string>();

                if (PluginConfig.beatDropInstalled)
                {
                    string[] beatDropJSONPlaylists = Directory.GetFiles(Path.Combine(PluginConfig.beatDropPlaylistsLocation, "playlists"), "*.json");
                    string[] beatDropBPLISTPlaylists = Directory.GetFiles(Path.Combine(PluginConfig.beatDropPlaylistsLocation, "playlists"), "*.bplist");
                    playlistFiles.AddRange(beatDropJSONPlaylists);
                    playlistFiles.AddRange(beatDropBPLISTPlaylists);
                    Logger.Log($"Found {beatDropJSONPlaylists.Length + beatDropBPLISTPlaylists.Length} playlists in BeatDrop folder");
                }

                string[] localJSONPlaylists = Directory.GetFiles(Path.Combine(Environment.CurrentDirectory, "Playlists"), "*.json");
                string[] localBPLISTPlaylists = Directory.GetFiles(Path.Combine(Environment.CurrentDirectory, "Playlists"), "*.bplist");
                playlistFiles.AddRange(localJSONPlaylists);
                playlistFiles.AddRange(localBPLISTPlaylists);

                Logger.Log($"Found {localJSONPlaylists.Length + localBPLISTPlaylists.Length} playlists in Playlists folder");

                foreach (string path in playlistFiles)
                {
                    try
                    {
                        Playlist playlist = Playlist.LoadPlaylist(path);
                        if (Path.GetFileName(path) == "favorites.json" && playlist.playlistTitle == "Your favorite songs")
                            return;
                        loadedPlaylists.Add(playlist);
                        Logger.Log($"Found \"{playlist.playlistTitle}\" by {playlist.playlistAuthor}");
                    }
                    catch (Exception e)
                    {
                        Logger.Log($"Unable to parse playlist @ {path}! Exception: {e}");
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Exception("Unable to load playlists! Exception: " + e);
            }
        }

        public static void AddSongToPlaylist(Playlist playlist, PlaylistSong song)
        {
            playlist.songs.Add(song);
            if(playlist.playlistTitle == "Your favorite songs")
            {
                playlist.SavePlaylist();
            }
        }

        public static void RemoveLevelFromPlaylists(string levelId)
        {
            foreach (Playlist playlist in loadedPlaylists)
            {
                if (playlist.songs.Where(y => y.level != null).Any(x => x.level.levelID == levelId))
                {
                    PlaylistSong song = playlist.songs.First(x => x.level != null && x.level.levelID == levelId);
                    song.level = null;
                    song.levelId = "";
                }
                if (playlist.playlistTitle == "Your favorite songs")
                {
                    playlist.SavePlaylist();
                }
            }
        }

        public static void RemoveLevelFromPlaylist(Playlist playlist, string levelId)
        {
            if (playlist.songs.Where(y => y.level != null).Any(x => x.level.levelID == levelId))
            {
                PlaylistSong song = playlist.songs.First(x => x.level != null && x.level.levelID == levelId);
                song.level = null;
                song.levelId = "";
            }
            if (playlist.playlistTitle == "Your favorite songs")
            {
                playlist.SavePlaylist();
            }
        }

        public static void MatchSongsForPlaylist(Playlist playlist, bool matchAll = false)
        {
            if (!SongLoader.AreSongsLoaded || SongLoader.AreSongsLoading || playlist.playlistTitle == "All songs" || playlist.playlistTitle == "Your favorite songs") return;
            if (!playlist.songs.All(x => x.level != null) || matchAll)
            {
                playlist.songs.ForEach(x =>
                {
                    if (x.level == null || matchAll)
                    {
                        x.level = SongLoader.CustomLevels.FirstOrDefault(y => (y.customSongInfo.path.Contains(x.key) && Directory.Exists(y.customSongInfo.path)) || (string.IsNullOrEmpty(x.levelId) ? false : y.levelID.StartsWith(x.levelId)));
                    }
                });
            }
        }

        public static void MatchSongsForAllPlaylists(bool matchAll = false)
        {
            Logger.Log("Matching songs for all playlists!");
            foreach (Playlist playlist in loadedPlaylists)
            {
                MatchSongsForPlaylist(playlist, matchAll);
            }
        }
    }

    public class PlaylistSong
    {
        public string key { get; set; }
        public string songName { get; set; }
        public string levelId { get; set; }

        [NonSerialized]
        public LevelSO level;
        [NonSerialized]
        public bool oneSaber;
        [NonSerialized]
        public string path;

        public IEnumerator MatchKey()
        {
            if (!string.IsNullOrEmpty(key))
                yield break;
            
            if (!string.IsNullOrEmpty(levelId))
            {
                ScrappedSong song = ScrappedData.Songs.FirstOrDefault(x => levelId.StartsWith(x.Hash));
                if (song != null)
                    key = song.Key;
                else
                    yield return SongDownloader.Instance.RequestSongByLevelIDCoroutine(levelId.Substring(0, Math.Min(32, levelId.Length)), (Song bsSong) => { key = bsSong.id; });
            }else if (level != null)
            {
                ScrappedSong song = ScrappedData.Songs.FirstOrDefault(x => level.levelID.StartsWith(x.Hash));
                if (song != null)
                    key = song.Key;
                else
                    yield return SongDownloader.Instance.RequestSongByLevelIDCoroutine(level.levelID.Substring(0, Math.Min(32, level.levelID.Length)), (Song bsSong) => { key = bsSong.id; });
            }
        }
    }

    public class Playlist
    {
        public string playlistTitle { get; set; }
        public string playlistAuthor { get; set; }
        public string image { get; set; }
        public int songCount { get; set; }
        public List<PlaylistSong> songs { get; set; }
        public string fileLoc { get; set; }
        public string customDetailUrl { get; set; }
        public string customArchiveUrl { get; set; }

        [NonSerialized]
        public Sprite icon;

        public Playlist()
        {

        }

        public Playlist(JSONNode playlistNode)
        {
            string image = playlistNode["image"].Value;
            if (!string.IsNullOrEmpty(image))
            {
                try
                {
                    icon = Base64Sprites.Base64ToSprite(image.Substring(image.IndexOf(",") + 1));
                }
                catch
                {
                    Logger.Exception("Unable to convert playlist image to sprite!");
                    icon = Base64Sprites.BeastSaberLogo;
                }
            }
            else
            {
                icon = Base64Sprites.BeastSaberLogo;
            }
            playlistTitle = playlistNode["playlistTitle"];
            playlistAuthor = playlistNode["playlistAuthor"];
            customDetailUrl = playlistNode["customDetailUrl"];
            customArchiveUrl = playlistNode["customArchiveUrl"];
            if (!string.IsNullOrEmpty(customDetailUrl))
            {
                if (!customDetailUrl.EndsWith("/"))
                    customDetailUrl += "/";
                Logger.Log("Found playlist with customDetailUrl! Name: " + playlistTitle + ", CustomDetailUrl: " + customDetailUrl);
            }
            if (!string.IsNullOrEmpty(customArchiveUrl) && customArchiveUrl.Contains("[KEY]"))
            {
                Logger.Log("Found playlist with customArchiveUrl! Name: " + playlistTitle + ", CustomArchiveUrl: " + customArchiveUrl);
            }

            songs = new List<PlaylistSong>();

            foreach (JSONNode node in playlistNode["songs"].AsArray)
            {
                PlaylistSong song = new PlaylistSong();
                song.key = node["key"];
                song.songName = node["songName"];
                song.levelId = node["levelId"];

                songs.Add(song);
            }

            if (playlistNode["playlistSongCount"] != null)
            {
                songCount = playlistNode["playlistSongCount"].AsInt;
            }
            if (playlistNode["fileLoc"] != null)
                fileLoc = playlistNode["fileLoc"];

            if (playlistNode["playlistURL"] != null)
                fileLoc = playlistNode["playlistURL"];
        }

        public static Playlist LoadPlaylist(string path)
        {
            return new Playlist(JSON.Parse(File.ReadAllText(path)));
        }

        public void SavePlaylist(string path = "")
        {
            if(ScrappedData.Songs.Count > 0)
                SharedCoroutineStarter.instance.StartCoroutine(SavePlaylistCoroutine(path));
        }

        public IEnumerator SavePlaylistCoroutine(string path = "")
        {
            Logger.Log($"Saving playlist \"{playlistTitle}\"...");
            image = Base64Sprites.SpriteToBase64(icon);
            songCount = songs.Count;
            
            foreach (PlaylistSong song in songs)
            {
                yield return song.MatchKey();
            }
            
            if (!string.IsNullOrEmpty(path))
            {
                fileLoc = Path.GetFullPath(path);
            }
            
            File.WriteAllText(fileLoc, JsonConvert.SerializeObject(this));

            Logger.Log("Playlist saved!");
        }
    }
}
