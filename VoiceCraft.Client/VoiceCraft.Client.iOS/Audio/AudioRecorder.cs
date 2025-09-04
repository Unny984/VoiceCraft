using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using AVFoundation;
using Foundation;
using VoiceCraft.Core;
using VoiceCraft.Core.Interfaces;

namespace VoiceCraft.Client.iOS.Audio;

public class AudioRecorder : IAudioRecorder
{
    // Static callback management for AOT compatibility
    private static readonly ConcurrentDictionary<IntPtr, AudioRecorder> _instances = new();
    private static long _nextInstanceId = 1;
    
    private readonly Lock _lockObj = new();
    private readonly SynchronizationContext? _synchronizationContext = SynchronizationContext.Current;
    private int _bufferMilliseconds;
    private int _channels;
    private bool _disposed;
    private IntPtr _audioQueue = IntPtr.Zero;
    private IntPtr[] _audioBuffers = Array.Empty<IntPtr>();
    private int _sampleRate;
    private bool _isRecording;
    private readonly IntPtr _instanceId;

    public AudioRecorder(int sampleRate, int channels, AudioFormat format)
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
    public CaptureState CaptureState { get; private set; }

    public event Action<byte[], int>? OnDataAvailable;
    public event Action<Exception?>? OnRecordingStopped;

    public void Initialize()
    {
        _lockObj.Enter();

        try
        {
            ThrowIfDisposed();

            if (CaptureState != CaptureState.Stopped)
                throw new InvalidOperationException(Locales.Locales.Audio_Recorder_InitFailed);

            CleanupRecorder();

            // Set up audio session for iOS recording
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

            // Create audio queue for input with static callback and instance ID as userData
            int result;
            unsafe
            {
                result = AudioQueueNewInput(ref audioFormat, &StaticInputCallback, _instanceId, IntPtr.Zero, IntPtr.Zero, 0, out _audioQueue);
            }
            if (result != 0)
                throw new InvalidOperationException($"Failed to create audio input queue: {result}");

            // Create buffers
            const int numberOfBuffers = 3;
            var bufferSize = (uint)Math.Max(1024, SampleRate * Channels * (BitDepth / 8) * BufferMilliseconds / 1000);
            _audioBuffers = new IntPtr[numberOfBuffers];

            for (int i = 0; i < numberOfBuffers; i++)
            {
                result = AudioQueueAllocateBuffer(_audioQueue, bufferSize, out _audioBuffers[i]);
                if (result != 0)
                    throw new InvalidOperationException($"Failed to allocate audio buffer: {result}");

                // Enqueue the buffer for recording
                result = AudioQueueEnqueueBuffer(_audioQueue, _audioBuffers[i], 0, IntPtr.Zero);
                if (result != 0)
                    throw new InvalidOperationException($"Failed to enqueue audio buffer: {result}");
            }
        }
        catch
        {
            CleanupRecorder();
            throw;
        }
        finally
        {
            _lockObj.Exit();
        }
    }

    public void Start()
    {
        _lockObj.Enter();

        try
        {
            ThrowIfDisposed();
            ThrowIfNotInitialized();

            if (CaptureState != CaptureState.Stopped) return;

            CaptureState = CaptureState.Starting;
            _isRecording = true;

            // Start the audio queue
            var result = AudioQueueStart(_audioQueue, IntPtr.Zero);
            if (result != 0)
            {
                // Attempt a soft restart after a brief delay
                Thread.Sleep(50);
                result = AudioQueueStart(_audioQueue, IntPtr.Zero);
            }
            if (result != 0)
                throw new InvalidOperationException($"Failed to start audio input queue: {result}");

            CaptureState = CaptureState.Capturing;
        }
        catch
        {
            CaptureState = CaptureState.Stopped;
            throw;
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

            if (CaptureState != CaptureState.Capturing) return;

            CaptureState = CaptureState.Stopping;
            _isRecording = false;

            AudioQueueStop(_audioQueue, true);
            CaptureState = CaptureState.Stopped;
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

    ~AudioRecorder()
    {
        Dispose(false);
    }

    private void SetupAudioSession()
    {
        try
        {
            // Prefer AVFoundation binding over P/Invoke
            var session = AVAudioSession.SharedInstance();
            NSError? err;
            session.SetPreferredSampleRate(SampleRate, out err);
            session.SetCategory(AVAudioSessionCategory.PlayAndRecord, AVAudioSessionCategoryOptions.DefaultToSpeaker, out err);
            session.SetActive(true, out err);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error setting up AVAudioSession: {ex.Message}");
        }
    }

    private void CleanupRecorder()
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

        _isRecording = false;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(typeof(AudioRecorder).ToString());
    }

    private void ThrowIfNotInitialized()
    {
        if (_audioQueue == IntPtr.Zero)
            throw new InvalidOperationException(Locales.Locales.Audio_Recorder_Init);
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

    // AOT-compatible static callback
    [UnmanagedCallersOnly]
    private static void StaticInputCallback(IntPtr userData, IntPtr audioQueue, IntPtr audioBuffer, 
        IntPtr startTime, uint numPackets, IntPtr packetDescs)
    {
        try
        {
            if (_instances.TryGetValue(userData, out var instance))
            {
                instance.InstanceInputCallback(audioQueue, audioBuffer, startTime, numPackets, packetDescs);
            }
        }
        catch
        {
            // Ignore callback errors to prevent crashes
        }
    }
    
    private void InstanceInputCallback(IntPtr audioQueue, IntPtr audioBuffer, 
        IntPtr startTime, uint numPackets, IntPtr packetDescs)
    {
        if (!_isRecording || CaptureState != CaptureState.Capturing) return;

        try
        {
            var buffer = Marshal.PtrToStructure<AudioQueueBuffer>(audioBuffer);
            if (buffer.AudioDataByteSize > 0)
            {
                var managedBuffer = new byte[buffer.AudioDataByteSize];
                Marshal.Copy(buffer.AudioData, managedBuffer, 0, (int)buffer.AudioDataByteSize);

                // Invoke data available event
                InvokeDataAvailable(managedBuffer, (int)buffer.AudioDataByteSize);
            }

            // Re-enqueue the buffer for continued recording
            if (_isRecording)
            {
                AudioQueueEnqueueBuffer(audioQueue, audioBuffer, 0, IntPtr.Zero);
            }
        }
        catch (Exception e)
        {
            InvokeRecordingStopped(e);
        }
    }

    private void InvokeDataAvailable(byte[] buffer, int count)
    {
        var handler = OnDataAvailable;
        if (handler == null) return;

        if (_synchronizationContext == null)
            handler(buffer, count);
        else
            _synchronizationContext.Post(_ => handler(buffer, count), null);
    }

    private void InvokeRecordingStopped(Exception? exception = null)
    {
        CaptureState = CaptureState.Stopped;
        var handler = OnRecordingStopped;
        if (handler == null) return;

        if (_synchronizationContext == null)
            handler(exception);
        else
            _synchronizationContext.Post(_ => handler(exception), null);
    }

    private void Dispose(bool disposing)
    {
        if (!disposing || _disposed) return;
        CleanupRecorder();
        
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
    private const string AVAudioSessionCategoryRecord = "AVAudioSessionCategoryRecord";

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
    private static extern unsafe int AudioQueueNewInput(ref AudioStreamBasicDescription format, delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, uint, IntPtr, void> callback, IntPtr userData, IntPtr cfRunLoop, IntPtr cfRunLoopMode, uint flags, out IntPtr audioQueue);

    [DllImport("/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox")]
    private static extern int AudioQueueAllocateBuffer(IntPtr audioQueue, uint bufferByteSize, out IntPtr buffer);

    [DllImport("/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox")]
    private static extern int AudioQueueEnqueueBuffer(IntPtr audioQueue, IntPtr buffer, uint numPacketDescs, IntPtr packetDescs);

    [DllImport("/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox")]
    private static extern int AudioQueueStart(IntPtr audioQueue, IntPtr startTime);

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
