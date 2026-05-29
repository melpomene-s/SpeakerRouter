namespace SpeakerRouter;

internal static class AppIcon
{
    public static Icon Load()
    {
        return Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
    }
}
