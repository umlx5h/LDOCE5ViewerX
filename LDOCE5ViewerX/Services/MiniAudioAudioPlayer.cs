using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;

using MiniAudioEx.Core.StandardAPI;

namespace LDOCE5ViewerX.Services;

/// <summary>
/// Plays dictionary MP3 audio with MiniAudioEx.
/// </summary>
public sealed class MiniAudioAudioPlayer : IAudioPlayer
{
    private const uint SampleRate = 44100;
    private const uint Channels = 2;
    private const int MaximumSourceVoices = 1;
    private const int MaximumCachedClips = 32;

    private readonly Dictionary<string, AudioClip> _clips = [];
    private readonly List<string> _clipOrder = [];
    private readonly Lock _syncRoot = new();

    private AudioSource? _source;
    private string? _currentClipKey;
    private bool _isInitialized;
    private bool _isDisposed;

    /// <summary>
    /// Plays MP3 audio bytes from memory.
    /// </summary>
    /// <param name="data">Encoded MP3 bytes.</param>
    /// <param name="mediaType">Audio media type.</param>
    /// <param name="volume">Linear playback volume, where <c>1.0</c> is the backend default.</param>
    public void Play(byte[] data, string mediaType, double volume)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (data.Length == 0)
        {
            return;
        }

        lock (_syncRoot)
        {
            EnsureInitialized();
            string key = Convert.ToHexString(SHA256.HashData(data));
            AudioClip clip = GetOrCreateClip(key, data);
            _currentClipKey = key;
            AudioSource source = ReplaceSource();
            source.Volume = Math.Clamp((float)volume, 0f, 1f);
            source.Play(clip);
            TrimClipCache();
        }
    }

    /// <summary>
    /// Releases MiniAudioEx resources.
    /// </summary>
    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _source?.Stop();
            _source?.Dispose();
            _source = null;
            foreach (AudioClip clip in _clips.Values)
            {
                clip.Dispose();
            }

            _clips.Clear();
            _clipOrder.Clear();
            if (_isInitialized)
            {
                AudioContext.Deinitialize();
                _isInitialized = false;
            }
        }
    }

    private void EnsureInitialized()
    {
        if (_isInitialized)
        {
            return;
        }

        AudioContext.Initialize(SampleRate, Channels);
        _isInitialized = true;
    }

    private AudioSource ReplaceSource()
    {
        _source?.Stop();
        _source?.Dispose();
        _source = new AudioSource(maxSources: MaximumSourceVoices);
        return _source;
    }

    private AudioClip GetOrCreateClip(string key, byte[] data)
    {
        if (_clips.TryGetValue(key, out AudioClip? cached))
        {
            _clipOrder.Remove(key);
            _clipOrder.Add(key);
            return cached;
        }

        AudioClip clip = new(data);
        _clips.Add(key, clip);
        _clipOrder.Add(key);
        return clip;
    }

    private void TrimClipCache()
    {
        while (_clips.Count > MaximumCachedClips)
        {
            string? removableKey = _clipOrder.Find(key => key != _currentClipKey);
            if (removableKey is null)
            {
                return;
            }

            _clipOrder.Remove(removableKey);
            _clips[removableKey].Dispose();
            _clips.Remove(removableKey);
        }
    }
}
