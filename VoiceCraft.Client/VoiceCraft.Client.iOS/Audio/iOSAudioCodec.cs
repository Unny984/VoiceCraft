using System;
using VoiceCraft.Client.Audio;

namespace VoiceCraft.Client.iOS.Audio;

/// <summary>
/// iOS-specific audio codec implementation using PCM compression
/// This provides basic audio compression for iOS where Opus is not available
/// </summary>
public class iOSAudioCodec : IAudioCodec
{
    private int _packetLossPercent;
    private int _bitRate;
    private bool _disposed;
    
    public iOSAudioCodec(int sampleRate, int channels, int application)
    {
        // Store parameters for iOS-specific initialization
        // For now, we'll use a simple PCM compression approach
        _packetLossPercent = 0;
        _bitRate = 32000; // Default bitrate
    }

    public void SetPacketLostPercent(int percent)
    {
        _packetLossPercent = Math.Clamp(percent, 0, 100);
    }

    public void SetBitRate(int bitrate)
    {
        _bitRate = bitrate;
    }

    public int Encode(byte[] inputBuffer, int frameSize, byte[] outputBuffer, int maxLength)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(iOSAudioCodec));
            
        if (inputBuffer == null || outputBuffer == null)
            return 0;
            
        // Simple compression approach for iOS:
        // Apply basic compression and copy to output buffer
        
        // Calculate compression ratio based on bitrate
        var compressionRatio = CalculateCompressionRatio();
        var bytesToCopy = Math.Min(inputBuffer.Length / compressionRatio, maxLength);
        
        // Simple downsampling compression
        for (int i = 0; i < bytesToCopy; i++)
        {
            if (i * compressionRatio < inputBuffer.Length)
            {
                outputBuffer[i] = inputBuffer[i * compressionRatio];
            }
        }
        
        return (int)bytesToCopy;
    }
    
    private int CalculateCompressionRatio()
    {
        // Basic compression ratio calculation based on bitrate
        // Higher bitrate = less compression (ratio closer to 1)
        // Lower bitrate = more compression (higher ratio)
        
        if (_bitRate >= 64000) return 1;  // Minimal compression
        if (_bitRate >= 32000) return 2;  // 2:1 compression
        if (_bitRate >= 16000) return 4;  // 4:1 compression
        return 8; // 8:1 compression for very low bitrates
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        // iOS-specific cleanup if needed
        _disposed = true;
        GC.SuppressFinalize(this);
    }
    
    ~iOSAudioCodec()
    {
        Dispose();
    }
}
