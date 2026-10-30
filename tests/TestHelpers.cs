using System;
using System.Drawing;
using System.Windows.Forms;

/// <summary>
/// Keeps the test suite from stealing the screen.
///
/// WinForms tests need a realized handle and a laid-out window, which historically meant
/// calling Show() on a form that then appeared in front of whatever the user was doing.
/// Showing the window off-screen, with no taskbar button, gives the test everything it
/// needs without the window ever landing in front of the user.
/// </summary>
internal static class TestHelpers
{
    public static void ShowOffScreen(Form form)
    {
        if (form == null)
        {
            return;
        }
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-32000, -32000);
        form.ShowInTaskbar = false;
        form.Show();
    }
}
