namespace SpeakerRouter;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.ThreadException += (_, e) =>
            MessageBox.Show(e.Exception.Message, "SpeakerRouter", MessageBoxButtons.OK, MessageBoxIcon.Error);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception exception)
            {
                MessageBox.Show(exception.Message, "SpeakerRouter", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };

        using var mutex = new Mutex(true, @"Local\SpeakerRouter-5F9B87D7", out var created);
        if (!created)
        {
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayAppContext());
    }
}
