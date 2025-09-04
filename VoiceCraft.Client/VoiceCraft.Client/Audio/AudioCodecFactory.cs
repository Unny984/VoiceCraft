using System;
using System.Runtime.InteropServices;

namespace VoiceCraft.Client.Audio;

public static class AudioCodecFactory
{
    /// <summary>
    /// Creates the appropriate audio codec for the current platform
    /// </summary>
    /// <param name="sampleRate">Sample rate for audio encoding</param>
    /// <param name="channels">Number of audio channels</param>
    /// <param name="application">Application type for encoding optimization</param>
    /// <returns>Platform-specific audio codec implementation</returns>
    public static IAudioCodec CreateCodec(int sampleRate, int channels, int application)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // macOS - use native audio (SimplePcmCodec), NOT OpusSharp
            return new SimplePcmCodec(sampleRate, channels, application);
        }
        
        if (IsIOS())
        {
            // iOS - use iOS-specific codec
            try
            {
                // Use reflection to create iOS codec to avoid compile-time dependency on other platforms
                var iOSCodecType = Type.GetType("VoiceCraft.Client.iOS.Audio.iOSAudioCodec, VoiceCraft.Client.iOS");
                if (iOSCodecType != null)
                {
                    return (IAudioCodec)Activator.CreateInstance(iOSCodecType, sampleRate, channels, application)!;
                }
            }
            catch
            {
                // Fall through to SimplePcmCodec
            }
            
            return new SimplePcmCodec(sampleRate, channels, application);
        }
        
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Windows - use Opus
            try
            {
                return new OpusCodecWrapper(sampleRate, channels, application);
            }
            catch
            {
                return new SimplePcmCodec(sampleRate, channels, application);
            }
        }
        
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Linux - use Opus
            try
            {
                return new OpusCodecWrapper(sampleRate, channels, application);
            }
            catch
            {
                return new SimplePcmCodec(sampleRate, channels, application);
            }
        }
        
        // Default fallback
        return new SimplePcmCodec(sampleRate, channels, application);
    }
    
    private static bool IsIOS()
    {
        // Check if we're running on iOS
        try
        {
            // Check for iOS-specific framework assemblies
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var assembly in assemblies)
            {
                if (assembly.FullName?.Contains("VoiceCraft.Client.iOS") == true)
                    return true;
            }
            
            // Check runtime information
            return RuntimeInformation.OSDescription.Contains("iOS") ||
                   RuntimeInformation.FrameworkDescription.Contains("iOS") ||
                   RuntimeInformation.RuntimeIdentifier.Contains("ios");
        }
        catch
        {
            return false;
        }
    }
}
