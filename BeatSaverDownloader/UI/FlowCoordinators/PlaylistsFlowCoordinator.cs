﻿using BeatSaverDownloader.Misc;
using BeatSaverDownloader.UI.ViewControllers;
using CustomUI.BeatSaber;
using SimpleJSON;
using SongLoaderPlugin;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using VRUI;
using Logger = BeatSaverDownloader.Misc.Logger;

namespace BeatSaverDownloader.UI.FlowCoordinators
{
    class PlaylistsFlowCoordinator : FlowCoordinator
    {
        public event Action<Playlist> didFinishEvent;

        public FlowCoordinator parentFlowCoordinator;

        private BackButtonNavigationController _playlistsNavigationController;
        private PlaylistListViewController _playlistListViewController;
        private PlaylistDetailViewController _playlistDetailViewController;
        private DownloadQueueViewController _downloadQueueViewController;

        private bool _downloadingPlaylist;

        private Playlist _lastPlaylist;

        public void Awake()
        {
            if (_playlistsNavigationController == null)
            {
                _playlistsNavigationController = BeatSaberUI.CreateViewController<BackButtonNavigationController>();

                GameObject _playlistDetailGameObject = Instantiate(Resources.FindObjectsOfTypeAll<StandardLevelDetailViewController>().First(), _playlistsNavigationController.rectTransform, false).gameObject;
                _playlistDetailViewController = _playlistDetailGameObject.AddComponent<PlaylistDetailViewController>();
                Destroy(_playlistDetailGameObject.GetComponent<StandardLevelDetailViewController>());
                _playlistDetailViewController.name = "PlaylistDetailViewController";
            }
        }

        protected override void DidActivate(bool firstActivation, ActivationType activationType)
        {
            if (firstActivation && activationType == ActivationType.AddedToHierarchy)
            {
                title = "Playlists";

                _playlistsNavigationController.didFinishEvent += _playlistsNavigationController_didFinishEvent;

                _playlistListViewController = BeatSaberUI.CreateViewController<PlaylistListViewController>();
                _playlistListViewController.didSelectRow += _playlistListViewController_didSelectRow;
                
                _playlistDetailViewController.downloadButtonPressed += _playlistDetailViewController_downloadButtonPressed;
                _playlistDetailViewController.selectButtonPressed += _playlistDetailViewController_selectButtonPressed;

                _downloadQueueViewController = BeatSaberUI.CreateViewController<DownloadQueueViewController>();

                SetViewControllersToNavigationConctroller(_playlistsNavigationController, new VRUIViewController[]
                {
                _playlistListViewController
                });

                ProvideInitialViewControllers(_playlistsNavigationController, _downloadQueueViewController, null);
            }
            _downloadingPlaylist = false;
            _playlistListViewController.SetContent(PlaylistsCollection.loadedPlaylists);

            _downloadQueueViewController.allSongsDownloaded += _downloadQueueViewController_allSongsDownloaded;
        }

        protected override void DidDeactivate(DeactivationType deactivationType)
        {
            _downloadQueueViewController.allSongsDownloaded -= _downloadQueueViewController_allSongsDownloaded;
            
        }

        private void _downloadQueueViewController_allSongsDownloaded()
        {
            SongLoader.Instance.RefreshSongs(false);
            _downloadingPlaylist = false;
        }

        private void _playlistListViewController_didSelectRow(Playlist playlist)
        {
            if (!_playlistDetailViewController.isInViewControllerHierarchy)
            {
                PushViewControllerToNavigationController(_playlistsNavigationController, _playlistDetailViewController);
            }

            _lastPlaylist = playlist;
            _playlistDetailViewController.SetContent(playlist);
        }

        private void _playlistDetailViewController_selectButtonPressed(Playlist playlist)
        {
            if (!_downloadQueueViewController.queuedSongs.Any(x => x.songQueueState == SongQueueState.Downloading || x.songQueueState == SongQueueState.Queued))
            {                
                if (_playlistsNavigationController.viewControllers.IndexOf(_playlistDetailViewController) >= 0)
                {
                    PopViewControllerFromNavigationController(_playlistsNavigationController, null, true);
                }

                parentFlowCoordinator.InvokePrivateMethod("DismissFlowCoordinator", new object[] { this, null, false });
                didFinishEvent?.Invoke(playlist);
            }
        }

        private void _playlistDetailViewController_downloadButtonPressed(Playlist playlist)
        {
            if(!_downloadingPlaylist)
                StartCoroutine(DownloadPlaylist(playlist));
        }

        public IEnumerator DownloadPlaylist(Playlist playlist)
        {
            PlaylistsCollection.MatchSongsForPlaylist(playlist, true);

            List<PlaylistSong> needToDownload = playlist.songs.Where(x => x.level == null).ToList();
            Logger.Log($"Need to download {needToDownload.Count} songs");

            _downloadingPlaylist = true;
            foreach (var item in needToDownload)
            {
                Song beatSaverSong = null;

                if (String.IsNullOrEmpty(playlist.customArchiveUrl))
                {
                    Logger.Log("Obtaining hash and url for " + item.key + ": " + item.songName);
                    yield return GetInfoForSong(playlist, item, (Song song) => { beatSaverSong = song;  });
                }
                else
                {
                    string archiveUrl = playlist.customArchiveUrl.Replace("[KEY]", item.key);

                    beatSaverSong = new Song()
                    {
                        songName = item.songName,
                        id = item.key,
                        downloadingProgress = 0f,
                        hash = (item.levelId == null ? "" : item.levelId),
                        downloadUrl = archiveUrl
                    };
                }

                if (beatSaverSong != null && !SongLoader.CustomLevels.Any(x => x.levelID.Substring(0, 32) == beatSaverSong.hash.ToUpper()))
                {
                    _downloadQueueViewController.EnqueueSong(beatSaverSong, true);
                }
            }
            _downloadingPlaylist = false;
        }

        public IEnumerator GetInfoForSong(Playlist playlist, PlaylistSong song, Action<Song> songCallback)
        {
            string url = $"{PluginConfig.beatsaverURL}/api/songs/detail/{song.key}";
            if (!string.IsNullOrEmpty(playlist.customDetailUrl))
            {
                url = playlist.customDetailUrl + song.key;
            }

            UnityWebRequest www = UnityWebRequest.Get(url);
            www.timeout = 15;
            yield return www.SendWebRequest();

            if (www.isNetworkError || www.isHttpError)
            {
                Logger.Error($"Unable to connect to {PluginConfig.beatsaverURL}! " + (www.isNetworkError ? $"Network error: {www.error}" : (www.isHttpError ? $"HTTP error: {www.error}" : "Unknown error")));
            }
            else
            {
                try
                {
                    JSONNode node = JSON.Parse(www.downloadHandler.text);
                    songCallback?.Invoke(new Song(node["song"]));
                }
                catch (Exception e)
                {
                    Logger.Exception("Unable to parse response! Exception: " + e);
                }
            }
        }

        private void _playlistsNavigationController_didFinishEvent()
        {
            if (!_downloadQueueViewController.queuedSongs.Any(x => x.songQueueState == SongQueueState.Downloading || x.songQueueState == SongQueueState.Queued))
            {
                if(_downloadQueueViewController.queuedSongs.Any(x => x.songQueueState == SongQueueState.Downloading || x.songQueueState == SongQueueState.Queued))
                    _downloadQueueViewController.AbortDownloads();

                if (_playlistsNavigationController.viewControllers.IndexOf(_playlistDetailViewController) >= 0)
                {
                    PopViewControllerFromNavigationController(_playlistsNavigationController, null, true);
                }

                parentFlowCoordinator.InvokePrivateMethod("DismissFlowCoordinator", new object[] { this, null, false });
                didFinishEvent?.Invoke(null);
            }
        }
    }
}
