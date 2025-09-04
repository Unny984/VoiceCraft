using System;

namespace VoiceCraft.Client.Audio;

public interface IAudioCodec : IDisposable
{
    /// <summary>
    /// Sets the expected packet loss percentage for the codec
    /// </summary>
    /// <param name="percent">Packet loss percentage (0-100)</param>
    void SetPacketLostPercent(int percent);
    
    /// <summary>
    /// Sets the bitrate for encoding
    /// </summary>
    /// <param name="bitrate">Bitrate in bits per second</param>
    void SetBitRate(int bitrate);
    
    /// <summary>
    /// Encodes audio data
    /// </summary>
    /// <param name="inputBuffer">Input audio data</param>
    /// <param name="frameSize">Number of samples per frame</param>
    /// <param name="outputBuffer">Output buffer for encoded data</param>
    /// <param name="maxLength">Maximum length of output buffer</param>
    /// <returns>Number of bytes written to output buffer</returns>
    int Encode(byte[] inputBuffer, int frameSize, byte[] outputBuffer, int maxLength);
}
