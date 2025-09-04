using System;
using UIKit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel;
using VoiceCraft.Client.iOS.Audio;
using VoiceCraft.Client.Services;
using MauiPermissions = Microsoft.Maui.ApplicationModel.Permissions;

namespace VoiceCraft.Client.iOS;

public class Application
{
    // This is the main entry point of the application.
    static void Main(string[] args)
    {
        // Configure platform-specific services before app starts
        ConfigurePlatformServices();
        
        // if you want to use a different Application Delegate class from "AppDelegate"
        // you can specify it here.
        UIApplication.Main(args, null, typeof(AppDelegate));
    }

    private static void ConfigurePlatformServices()
    {
        // Add platform-specific services to the main App's ServiceCollection
        // Register audio service
        App.ServiceCollection.AddSingleton<AudioService, NativeAudioService>();
        
        // Register storage service
        App.ServiceCollection.AddSingleton<StorageService, NativeStorageService>();
        
        // Register background service
        App.ServiceCollection.AddSingleton<BackgroundService, NativeBackgroundService>();
        
        // Register permissions - iOS specific microphone permission
        App.ServiceCollection.AddSingleton(typeof(MauiPermissions.Microphone), typeof(VoiceCraft.Client.iOS.Permissions.MauiMicrophonePermission));
        
        // Fallback factory in case other permissions are requested
        App.ServiceCollection.AddSingleton<Func<Type, MauiPermissions.BasePermission>>(provider => type =>
        {
            if (type == typeof(MauiPermissions.Microphone))
            {
                return (MauiPermissions.BasePermission)provider.GetRequiredService(typeof(MauiPermissions.Microphone));
            }
            return (MauiPermissions.BasePermission)Activator.CreateInstance(type)!;
        });
    }
}