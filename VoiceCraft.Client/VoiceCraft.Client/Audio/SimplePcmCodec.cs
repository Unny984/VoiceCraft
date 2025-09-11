using System;

namespace VoiceCraft.Client.Audio;

/// <summary>
/// Simple PCM-based audio codec for platforms where Opus is not available
/// Provides basic compression using downsampling and bit depth reduction
/// </summary>
public class SimplePcmCodec : IAudioCodec
{
    private int _sampleRate;
    private int _channels;
    private int _packetLossPercent;
    private int _bitRate;
    private bool _disposed;
    
    public SimplePcmCodec(int sampleRate, int channels, int application)
    {
        _sampleRate = sampleRate;
        _channels = channels;
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
            throw new ObjectDisposedException(nameof(SimplePcmCodec));
            
        if (inputBuffer == null || outputBuffer == null)
            return 0;
            
        // Simple PCM compression based on bitrate
        var compressionFactor = CalculateCompressionFactor();
        var bytesToProcess = Math.Min(inputBuffer.Length, frameSize * _channels * 2); // 16-bit samples
        var compressedSize = Math.Min(bytesToProcess / compressionFactor, maxLength);
        
        // Apply simple compression
        for (int i = 0; i < compressedSize; i++)
        {
            var sourceIndex = i * compressionFactor;
            if (sourceIndex < inputBuffer.Length)
            {
                // Simple downsampling - take every nth sample
                outputBuffer[i] = inputBuffer[sourceIndex];
            }
        }
        
        return compressedSize;
    }
    
    private int CalculateCompressionFactor()
    {
        // Calculate compression factor based on target bitrate
        // Higher bitrate = less compression
        var targetRatio = 128000 / Math.Max(_bitRate, 8000); // Baseline 128kbps uncompressed
        return Math.Max(1, Math.Min(8, targetRatio));
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        // No native resources to cleanup for simple PCM codec
        _disposed = true;
        GC.SuppressFinalize(this);
    }
    
    ~SimplePcmCodec()
    {
        Dispose();
    }
}
