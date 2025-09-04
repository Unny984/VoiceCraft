using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using VoiceCraft.Client.Services;
using VoiceCraft.Core;
using VoiceCraft.Core.Interfaces;

namespace VoiceCraft.Client.iOS.Audio;

public class NativeAudioService : AudioService
{
    public NativeAudioService() : base(
        Array.Empty<RegisteredAutomaticGainController>(),
        Array.Empty<RegisteredEchoCanceler>(),
        Array.Empty<RegisteredDenoiser>())
    {
    }

    public override Task<List<string>> GetInputDevicesAsync()
    {
        var devices = new List<string>();
        
        try
        {
            // On iOS, audio routing is controlled by the system
            // We can query available input routes
            var audioSession = AVAudioSessionGetSharedInstance();
            if (audioSession != IntPtr.Zero)
            {
                var availableInputs = GetAvailableInputs(audioSession);
                devices.AddRange(availableInputs);
            }
            else
            {
                // Fallback if AVAudioSession is not available
                devices.Add("Built-in Microphone");
            }
        }
        catch
        {
            // If we can't enumerate devices, add a default entry
            devices.Add("Built-in Microphone");
        }

        if (devices.Count == 0)
        {
            devices.Add("Built-in Microphone");
        }

        return Task.FromResult(devices);
    }

    public override Task<List<string>> GetOutputDevicesAsync()
    {
        var devices = new List<string>();
        
        try
        {
            // On iOS, audio routing is controlled by the system
            // We can query available output routes
            var audioSession = AVAudioSessionGetSharedInstance();
            if (audioSession != IntPtr.Zero)
            {
                var availableOutputs = GetAvailableOutputs(audioSession);
                devices.AddRange(availableOutputs);
            }
            else
            {
                // Fallback if AVAudioSession is not available
                devices.Add("Built-in Speaker");
            }
        }
        catch
        {
            // If we can't enumerate devices, add a default entry
            devices.Add("Built-in Speaker");
        }

        if (devices.Count == 0)
        {
            devices.Add("Built-in Speaker");
        }

        return Task.FromResult(devices);
    }

    public override IAudioRecorder CreateAudioRecorder(int sampleRate, int channels, AudioFormat format)
    {
        return new AudioRecorder(sampleRate, channels, format);
    }

    public override IAudioPlayer CreateAudioPlayer(int sampleRate, int channels, AudioFormat format)
    {
        return new AudioPlayer(sampleRate, channels, format);
    }

    private List<string> GetAvailableInputs(IntPtr audioSession)
    {
        var inputs = new List<string>();
        
        try
        {
            // Query current route for input
            var currentRoute = AVAudioSessionGetCurrentRoute(audioSession);
            if (currentRoute != IntPtr.Zero)
            {
                // Get input descriptions
                var inputDescriptions = GetRouteInputs(currentRoute);
                inputs.AddRange(inputDescriptions);
                
                CFRelease(currentRoute);
            }
            
            // Add common iOS input types
            if (inputs.Count == 0)
            {
                inputs.Add("Built-in Microphone");
                inputs.Add("Headset Microphone");
                inputs.Add("Bluetooth Microphone");
            }
        }
        catch
        {
            inputs.Add("Built-in Microphone");
        }
        
        return inputs;
    }

    private List<string> GetAvailableOutputs(IntPtr audioSession)
    {
        var outputs = new List<string>();
        
        try
        {
            // Query current route for output
            var currentRoute = AVAudioSessionGetCurrentRoute(audioSession);
            if (currentRoute != IntPtr.Zero)
            {
                // Get output descriptions
                var outputDescriptions = GetRouteOutputs(currentRoute);
                outputs.AddRange(outputDescriptions);
                
                CFRelease(currentRoute);
            }
            
            // Add common iOS output types
            if (outputs.Count == 0)
            {
                outputs.Add("Built-in Speaker");
                outputs.Add("Built-in Receiver");
                outputs.Add("Headphones");
                outputs.Add("Bluetooth Speaker");
            }
        }
        catch
        {
            outputs.Add("Built-in Speaker");
        }
        
        return outputs;
    }

    private List<string> GetRouteInputs(IntPtr route)
    {
        var inputs = new List<string>();
        
        try
        {
            // This is a simplified implementation
            // In a full implementation, you would iterate through the route's inputs
            // and get their port names and types
            
            // For now, we'll return common input types based on the route
            inputs.Add("Device Input");
        }
        catch
        {
            // Ignore errors in route parsing
        }
        
        return inputs;
    }

    private List<string> GetRouteOutputs(IntPtr route)
    {
        var outputs = new List<string>();
        
        try
        {
            // This is a simplified implementation
            // In a full implementation, you would iterate through the route's outputs
            // and get their port names and types
            
            // For now, we'll return common output types based on the route
            outputs.Add("Device Output");
        }
        catch
        {
            // Ignore errors in route parsing
        }
        
        return outputs;
    }

    // AVAudioSession P/Invoke declarations
    [DllImport("/System/Library/Frameworks/AVFoundation.framework/AVFoundation")]
    private static extern IntPtr AVAudioSessionGetSharedInstance();

    [DllImport("/System/Library/Frameworks/AVFoundation.framework/AVFoundation")]
    private static extern IntPtr AVAudioSessionGetCurrentRoute(IntPtr session);

    // Core Foundation P/Invoke declarations
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(IntPtr cfObject);
}
