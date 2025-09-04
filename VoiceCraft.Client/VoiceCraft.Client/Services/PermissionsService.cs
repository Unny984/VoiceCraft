using System;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using MauiPermissions = Microsoft.Maui.ApplicationModel.Permissions;

namespace VoiceCraft.Client.Services;

public class PermissionsService(NotificationService notificationService, Func<Type, MauiPermissions.BasePermission> getPermission)
{
    public async Task<PermissionStatus> CheckAndRequestPermission<TPermission>(string? rationalDescription = null)
        where TPermission : MauiPermissions.BasePermission, new()
    {
        var permission = getPermission(typeof(TPermission));
        var status = await permission.CheckStatusAsync();

        switch (status)
        {
            case PermissionStatus.Granted:
                return status;
            case PermissionStatus.Denied when DeviceInfo.Platform == DevicePlatform.iOS:
                // Prompt the user to turn on in settings
                // On iOS once a permission has been denied it may not be requested again from the application
                return status;
            case PermissionStatus.Unknown:
            case PermissionStatus.Disabled:
            case PermissionStatus.Restricted:
            case PermissionStatus.Limited:
            default:
                break;
        }

        status = await permission.RequestAsync();

        if (permission.ShouldShowRationale() && !string.IsNullOrWhiteSpace(rationalDescription)) notificationService.SendErrorNotification(rationalDescription);
        return status;
    }
}