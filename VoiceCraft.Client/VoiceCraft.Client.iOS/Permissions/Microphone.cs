using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;

namespace VoiceCraft.Client.iOS.Permissions;

public static class Microphone
{
    public static Task<PermissionStatus> CheckStatusAsync()
    {
        var status = GetMicrophoneAuthorizationStatus();
        return Task.FromResult(ConvertAuthorizationStatus(status));
    }

    public static async Task<PermissionStatus> RequestAsync()
    {
        var currentStatus = await CheckStatusAsync();
        
        if (currentStatus == PermissionStatus.Granted)
            return currentStatus;

        if (currentStatus == PermissionStatus.Denied)
            return currentStatus; // Simulator may cache Denied; surface it immediately

        var tcs = new TaskCompletionSource<PermissionStatus>();
        
        RequestMicrophoneAuthorization((granted) =>
        {
            var status = granted ? PermissionStatus.Granted : PermissionStatus.Denied;
            tcs.SetResult(status);
        });

        return await tcs.Task;
    }

    public static bool ShouldShowRationale()
    {
        return false;
    }

    private static PermissionStatus ConvertAuthorizationStatus(int status)
    {
        return status switch
        {
            0 => PermissionStatus.Unknown,
            1 => PermissionStatus.Restricted,
            2 => PermissionStatus.Denied,
            3 => PermissionStatus.Granted,
            _ => PermissionStatus.Unknown
        };
    }

    // AVFoundation microphone permission P/Invoke
    [DllImport("/System/Library/Frameworks/AVFoundation.framework/AVFoundation")]
    private static extern int AVCaptureDevice_authorizationStatusForMediaType(IntPtr mediaType);

    [DllImport("/System/Library/Frameworks/AVFoundation.framework/AVFoundation")]
    private static extern void AVCaptureDevice_requestAccessForMediaType(IntPtr mediaType, IntPtr completionHandler);

    // Core Foundation P/Invoke for CFString
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, string cStr, uint encoding);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(IntPtr cfObject);

    // Objective-C runtime P/Invoke
    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr objc_getClass(string className);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr sel_registerName(string selectorName);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr arg1);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void PermissionCallback(bool granted);

    private static int GetMicrophoneAuthorizationStatus()
    {
        try
        {
            var mediaTypeAudio = GetAVMediaTypeAudio();
            var status = AVCaptureDevice_authorizationStatusForMediaType(mediaTypeAudio);
            CFRelease(mediaTypeAudio);
            return status;
        }
        catch
        {
            return 0;
        }
    }

    private static void RequestMicrophoneAuthorization(PermissionCallback callback)
    {
        try
        {
            var mediaTypeAudio = GetAVMediaTypeAudio();
            var blockWrapper = CreateBlockWrapper(callback);
            AVCaptureDevice_requestAccessForMediaType(mediaTypeAudio, blockWrapper);
            CFRelease(mediaTypeAudio);
        }
        catch
        {
            callback(false);
        }
    }

    private static IntPtr GetAVMediaTypeAudio()
    {
        const uint kCFStringEncodingUTF8 = 0x08000100;
        return CFStringCreateWithCString(IntPtr.Zero, "soun", kCFStringEncodingUTF8);
    }

    private static IntPtr CreateBlockWrapper(PermissionCallback callback)
    {
        var handle = GCHandle.Alloc(callback);
        return GCHandle.ToIntPtr(handle);
    }
}

// Derived permission that MAUI will recognize as the Microphone permission type
public sealed class MauiMicrophonePermission : Microsoft.Maui.ApplicationModel.Permissions.Microphone
{
}
