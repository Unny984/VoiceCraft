using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using VoiceCraft.Core;
using VoiceCraft.Core.Interfaces;

namespace VoiceCraft.Client.iOS.Audio;

public class AudioPlayer : IAudioPlayer
{
    // Static callback management for AOT compatibility
    private static readonly ConcurrentDictionary<IntPtr, AudioPlayer> _instances = new();
    private static long _nextInstanceId = 1;
    
    private readonly Lock _lockObj = new();
    private readonly SynchronizationContext? _synchronizationContext = SynchronizationContext.Current;
    private int _bufferMilliseconds;
    private int _channels;
    private bool _disposed;
    private IntPtr _audioQueue = IntPtr.Zero;
    private IntPtr[] _audioBuffers = Array.Empty<IntPtr>();
    private Func<byte[], int, int>? _playerCallback;
    private int _sampleRate;
    private bool _isPlaying;
    private Thread? _playbackThread;
    private readonly ManualResetEventSlim _stopEvent = new(false);
    private readonly IntPtr _instanceId;



    public AudioPlayer(int sampleRate, int channels, AudioFormat format)
    {
        SampleRate = sampleRate;
        Channels = channels;
        Format = format;
        
        // Create unique instance ID for AOT callback lookup
        _instanceId = new IntPtr(Interlocked.Increment(ref _nextInstanceId));
        _instances[_instanceId] = this;
    }

    public int SampleRate
    {
        get => _sampleRate;
        set
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), value, "Sample rate must be greater than or equal to zero!");
            _sampleRate = value;
        }
    }

    public int Channels
    {
        get => _channels;
        set
        {
            if (value < 1)
                throw new ArgumentOutOfRangeException(nameof(value), value, "Channels must be greater than or equal to one!");
            _channels = value;
        }
    }

    public int BitDepth
    {
        get
        {
            return Format switch
            {
                AudioFormat.Pcm8 => 8,
                AudioFormat.Pcm16 => 16,
                AudioFormat.PcmFloat => 32,
                _ => throw new ArgumentOutOfRangeException(nameof(Format))
            };
        }
    }

    public AudioFormat Format { get; set; }

    public int BufferMilliseconds
    {
        get => _bufferMilliseconds;
        set
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), value, "Buffer milliseconds must be greater than or equal to zero!");
            _bufferMilliseconds = value;
        }
    }

    public string? SelectedDevice { get; set; }
    public PlaybackState PlaybackState { get; private set; }
    public event Action<Exception?>? OnPlaybackStopped;

    public void Initialize(Func<byte[], int, int> playerCallback)
    {
        _lockObj.Enter();

        try
        {
            ThrowIfDisposed();

            if (PlaybackState != PlaybackState.Stopped)
                throw new InvalidOperationException(Locales.Locales.Audio_Player_InitFailed);

            CleanupPlayer();
            _playerCallback = playerCallback;

            // Set up audio session for iOS
            SetupAudioSession();

            // Create AudioStreamBasicDescription
            var audioFormat = new AudioStreamBasicDescription
            {
                SampleRate = SampleRate,
                FormatID = kAudioFormatLinearPCM,
                FormatFlags = GetFormatFlags(),
                BytesPerPacket = (uint)(Channels * (BitDepth / 8)),
                FramesPerPacket = 1,
                BytesPerFrame = (uint)(Channels * (BitDepth / 8)),
                ChannelsPerFrame = (uint)Channels,
                BitsPerChannel = (uint)BitDepth,
                Reserved = 0
            };

            // Create audio queue with static callback and instance ID as userData
            int result;
            unsafe
            {
                result = AudioQueueNewOutput(ref audioFormat, &StaticOutputCallback, _instanceId, IntPtr.Zero, IntPtr.Zero, 0, out _audioQueue);
            }
            if (result != 0)
                throw new InvalidOperationException($"Failed to create audio queue: {result}");

            // Create buffers
            const int numberOfBuffers = 3;
            var bufferSize = (uint)(SampleRate * Channels * (BitDepth / 8) * BufferMilliseconds / 1000);
            _audioBuffers = new IntPtr[numberOfBuffers];

            for (int i = 0; i < numberOfBuffers; i++)
            {
                result = AudioQueueAllocateBuffer(_audioQueue, bufferSize, out _audioBuffers[i]);
                if (result != 0)
                    throw new InvalidOperationException($"Failed to allocate audio buffer: {result}");
            }
        }
        catch
        {
            CleanupPlayer();
            throw;
        }
        finally
        {
            _lockObj.Exit();
        }
    }

    public void Play()
    {
        _lockObj.Enter();

        try
        {
            ThrowIfDisposed();
            ThrowIfNotInitialized();

            if (PlaybackState != PlaybackState.Stopped) return;

            PlaybackState = PlaybackState.Starting;
            _stopEvent.Reset();
            _isPlaying = true;

            // Start the audio queue
            var result = AudioQueueStart(_audioQueue, IntPtr.Zero);
            if (result != 0)
                throw new InvalidOperationException($"Failed to start audio queue: {result}");

            // Start playback thread
            _playbackThread = new Thread(PlaybackLoop) { IsBackground = true };
            _playbackThread.Start();

            PlaybackState = PlaybackState.Playing;
        }
        catch
        {
            PlaybackState = PlaybackState.Stopped;
            throw;
        }
        finally
        {
            _lockObj.Exit();
        }
    }

    public void Pause()
    {
        _lockObj.Enter();

        try
        {
            ThrowIfDisposed();
            ThrowIfNotInitialized();

            if (PlaybackState != PlaybackState.Playing) return;

            AudioQueuePause(_audioQueue);
            PlaybackState = PlaybackState.Paused;
        }
        finally
        {
            _lockObj.Exit();
        }
    }

    public void Stop()
    {
        _lockObj.Enter();

        try
        {
            ThrowIfDisposed();
            ThrowIfNotInitialized();

            if (PlaybackState is not (PlaybackState.Playing or PlaybackState.Paused)) return;

            PlaybackState = PlaybackState.Stopping;
            _isPlaying = false;
            _stopEvent.Set();

            AudioQueueStop(_audioQueue, true);
        }
        finally
        {
            _lockObj.Exit();
        }
    }

    public void Dispose()
    {
        _lockObj.Enter();

        try
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        finally
        {
            _lockObj.Exit();
        }
    }

    ~AudioPlayer()
    {
        Dispose(false);
    }

    private void SetupAudioSession()
    {
        try
        {
            // Set up iOS audio session for playback
            var audioSession = AVAudioSessionGetSharedInstance();
            if (audioSession == IntPtr.Zero)
            {
                Console.WriteLine("Warning: AVAudioSession not available. Audio may not work properly.");
                return; // Continue without audio session setup
            }
            
            // Set category to playback
            var categoryResult = AVAudioSessionSetCategory(audioSession, AVAudioSessionCategoryPlayback, 0);
            if (categoryResult != 0)
            {
                Console.WriteLine($"Warning: Failed to set audio session category. Error code: {categoryResult}");
            }
            
            // Activate the audio session
            var activateResult = AVAudioSessionSetActive(audioSession, true);
            if (activateResult != 0)
            {
                Console.WriteLine($"Warning: Failed to activate audio session. Error code: {activateResult}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error setting up audio session: {ex.Message}");
            // Continue without audio session setup - AudioQueue might still work
        }
    }

    private void CleanupPlayer()
    {
        if (_audioQueue != IntPtr.Zero)
        {
            AudioQueueStop(_audioQueue, true);
            
            if (_audioBuffers.Length > 0)
            {
                foreach (var buffer in _audioBuffers)
                {
                    if (buffer != IntPtr.Zero)
                        AudioQueueFreeBuffer(_audioQueue, buffer);
                }
                _audioBuffers = Array.Empty<IntPtr>();
            }

            AudioQueueDispose(_audioQueue, true);
            _audioQueue = IntPtr.Zero;
        }

        _isPlaying = false;
        _stopEvent.Set();
        
        if (_playbackThread != null && _playbackThread.IsAlive)
        {
            _playbackThread.Join(1000);
            _playbackThread = null;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(typeof(AudioPlayer).ToString());
    }

    private void ThrowIfNotInitialized()
    {
        if (_audioQueue == IntPtr.Zero)
            throw new InvalidOperationException(Locales.Locales.Audio_Player_Init);
    }

    private uint GetFormatFlags()
    {
        return Format switch
        {
            AudioFormat.Pcm8 => kAudioFormatFlagIsPacked,
            AudioFormat.Pcm16 => kAudioFormatFlagIsSignedInteger | kAudioFormatFlagIsPacked,
            AudioFormat.PcmFloat => kAudioFormatFlagIsFloat | kAudioFormatFlagIsPacked,
            _ => throw new ArgumentOutOfRangeException(nameof(Format))
        };
    }

    private void PlaybackLoop()
    {
        try
        {
            while (_isPlaying && !_stopEvent.IsSet)
            {
                foreach (var buffer in _audioBuffers)
                {
                    if (!_isPlaying || _stopEvent.IsSet) break;
                    FillAndEnqueueBuffer(buffer);
                }

                if (!_isPlaying) break;
                Thread.Sleep(10); // Small delay to prevent busy waiting
            }
        }
        catch (Exception e)
        {
            InvokePlaybackStopped(e);
        }
        finally
        {
            PlaybackState = PlaybackState.Stopped;
        }
    }

    private void FillAndEnqueueBuffer(IntPtr buffer)
    {
        if (_playerCallback == null) return;

        var audioBuffer = Marshal.PtrToStructure<AudioQueueBuffer>(buffer);
        var bufferSize = (int)audioBuffer.AudioDataBytesCapacity;
        var managedBuffer = new byte[bufferSize];

        var bytesRead = _playerCallback(managedBuffer, bufferSize);
        if (bytesRead > 0)
        {
            Marshal.Copy(managedBuffer, 0, audioBuffer.AudioData, bytesRead);
            audioBuffer.AudioDataByteSize = (uint)bytesRead;
            Marshal.StructureToPtr(audioBuffer, buffer, false);

            AudioQueueEnqueueBuffer(_audioQueue, buffer, 0, IntPtr.Zero);
        }
    }

    // AOT-compatible static callback
    [UnmanagedCallersOnly]
    private static void StaticOutputCallback(IntPtr userData, IntPtr audioQueue, IntPtr audioBuffer)
    {
        try
        {
            if (_instances.TryGetValue(userData, out var instance))
            {
                instance.InstanceOutputCallback(audioQueue, audioBuffer);
            }
        }
        catch
        {
            // Ignore callback errors to prevent crashes
        }
    }
    
    private void InstanceOutputCallback(IntPtr audioQueue, IntPtr audioBuffer)
    {
        if (_isPlaying && !_stopEvent.IsSet)
        {
            FillAndEnqueueBuffer(audioBuffer);
        }
    }

    private void InvokePlaybackStopped(Exception? exception = null)
    {
        PlaybackState = PlaybackState.Stopped;
        var handler = OnPlaybackStopped;
        if (handler == null) return;

        if (_synchronizationContext == null)
            handler(exception);
        else
            _synchronizationContext.Post(_ => handler(exception), null);
    }

    private void Dispose(bool disposing)
    {
        if (!disposing || _disposed) return;
        CleanupPlayer();
        _stopEvent.Dispose();
        
        // Remove instance from static dictionary
        _instances.TryRemove(_instanceId, out _);
        
        _disposed = true;
    }

    // AudioQueue P/Invoke declarations
    private const uint kAudioFormatLinearPCM = 0x6C70636D; // 'lpcm'
    private const uint kAudioFormatFlagIsFloat = 1U << 0;
    private const uint kAudioFormatFlagIsSignedInteger = 1U << 2;
    private const uint kAudioFormatFlagIsPacked = 1U << 3;

    // AVAudioSession constants
    private const string AVAudioSessionCategoryPlayback = "AVAudioSessionCategoryPlayback";

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioStreamBasicDescription
    {
        public double SampleRate;
        public uint FormatID;
        public uint FormatFlags;
        public uint BytesPerPacket;
        public uint FramesPerPacket;
        public uint BytesPerFrame;
        public uint ChannelsPerFrame;
        public uint BitsPerChannel;
        public uint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioQueueBuffer
    {
        public uint AudioDataBytesCapacity;
        public IntPtr AudioData;
        public uint AudioDataByteSize;
        public IntPtr UserData;
        public uint PacketDescriptionCapacity;
        public IntPtr PacketDescriptions;
        public uint PacketDescriptionCount;
    }

    // AudioQueue P/Invoke declarations
    [DllImport("/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox")]
    private static extern unsafe int AudioQueueNewOutput(ref AudioStreamBasicDescription format, delegate* unmanaged<IntPtr, IntPtr, IntPtr, void> callback, IntPtr userData, IntPtr cfRunLoop, IntPtr cfRunLoopMode, uint flags, out IntPtr audioQueue);

    [DllImport("/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox")]
    private static extern int AudioQueueAllocateBuffer(IntPtr audioQueue, uint bufferByteSize, out IntPtr buffer);

    [DllImport("/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox")]
    private static extern int AudioQueueEnqueueBuffer(IntPtr audioQueue, IntPtr buffer, uint numPacketDescs, IntPtr packetDescs);

    [DllImport("/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox")]
    private static extern int AudioQueueStart(IntPtr audioQueue, IntPtr startTime);

    [DllImport("/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox")]
    private static extern int AudioQueuePause(IntPtr audioQueue);

    [DllImport("/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox")]
    private static extern int AudioQueueStop(IntPtr audioQueue, bool immediate);

    [DllImport("/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox")]
    private static extern int AudioQueueFreeBuffer(IntPtr audioQueue, IntPtr buffer);

    [DllImport("/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox")]
    private static extern int AudioQueueDispose(IntPtr audioQueue, bool immediate);

    // AVAudioSession P/Invoke declarations
    [DllImport("/System/Library/Frameworks/AVFoundation.framework/AVFoundation")]
    private static extern IntPtr AVAudioSessionGetSharedInstance();

    [DllImport("/System/Library/Frameworks/AVFoundation.framework/AVFoundation")]
    private static extern int AVAudioSessionSetCategory(IntPtr session, string category, uint options);

    [DllImport("/System/Library/Frameworks/AVFoundation.framework/AVFoundation")]
    private static extern int AVAudioSessionSetActive(IntPtr session, bool active);
}
