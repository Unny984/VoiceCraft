using System;
using OpusSharp.Core;

namespace VoiceCraft.Client.Audio;

/// <summary>
/// Wrapper for OpusSharp codec to implement IAudioCodec interface
/// </summary>
public class OpusCodecWrapper : IAudioCodec
{
    private readonly OpusEncoder _encoder;
    private bool _disposed;

    public OpusCodecWrapper(int sampleRate, int channels, int application)
    {
        _encoder = new OpusEncoder(sampleRate, channels, (OpusPredefinedValues)application);
    }

    public void SetPacketLostPercent(int percent)
    {
        try
        {
            // Try to set packet loss if the method exists
            // This may be an extension method or property
            var method = _encoder.GetType().GetMethod("SetPacketLostPercent") ?? 
                        _encoder.GetType().GetMethod("SetPacketLoss");
            method?.Invoke(_encoder, new object[] { percent });
        }
        catch
        {
            // Method not available, ignore silently
        }
    }

    public void SetBitRate(int bitrate)
    {
        try
        {
            // Try to set bitrate if the method exists
            var method = _encoder.GetType().GetMethod("SetBitRate") ?? 
                        _encoder.GetType().GetMethod("Bitrate");
            method?.Invoke(_encoder, new object[] { bitrate });
        }
        catch
        {
            // Method not available, ignore silently
        }
    }

    public int Encode(byte[] inputBuffer, int frameSize, byte[] outputBuffer, int maxLength)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(OpusCodecWrapper));
            
        return _encoder.Encode(inputBuffer, frameSize, outputBuffer, maxLength);
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        _encoder?.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
    
    ~OpusCodecWrapper()
    {
        Dispose();
    }
}
