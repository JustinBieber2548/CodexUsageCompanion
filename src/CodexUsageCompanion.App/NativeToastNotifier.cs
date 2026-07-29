using System.Security;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace CodexUsageCompanion.App;

public static class NativeToastNotifier
{
    public static bool TryShow(AppNotification notification)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10))
        {
            return false;
        }

        try
        {
            var xml = new XmlDocument();
            xml.LoadXml(
                $"""
                 <toast>
                   <visual>
                     <binding template="ToastGeneric">
                       <text>{Escape(notification.Title)}</text>
                       <text>{Escape(notification.Message)}</text>
                     </binding>
                   </visual>
                 </toast>
                 """);
            var toast = new ToastNotification(xml)
            {
                ExpirationTime = DateTimeOffset.UtcNow.AddMinutes(5)
            };
            ToastNotificationManager
                .CreateToastNotifier("CodexUsageCompanion.Desktop")
                .Show(toast);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string Escape(string value)
    {
        return SecurityElement.Escape(value) ?? string.Empty;
    }
}
