using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using AutoScrollWheel;

internal static class BackgroundScrollTests
{
    private class QuietForm : Form
    {
        protected override bool ShowWithoutActivation { get { return true; } }
    }

    private sealed class WheelBox : TextBox
    {
        internal int Count;
        internal int Delta;
        internal Point LastPoint;

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x020A)
            {
                Count++;
                Delta += unchecked((short)((message.WParam.ToInt64() >> 16) & 0xffff));
                LastPoint = new Point(unchecked((short)(message.LParam.ToInt64() & 0xffff)), unchecked((short)((message.LParam.ToInt64() >> 16) & 0xffff)));
            }
            base.WndProc(ref message);
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    private static void Assert(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        Console.WriteLine("PASS " + name);
    }

    [STAThread]
    private static int Main()
    {
        try
        {
            Application.EnableVisualStyles();
            Run();
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void Run()
    {
        using (QuietForm window = new QuietForm())
        using (QuietForm cover = new QuietForm())
        {
            window.StartPosition = FormStartPosition.Manual;
            window.Bounds = new Rectangle(100, 100, 600, 400);
            WheelBox other = new WheelBox { Multiline = true, Bounds = new Rectangle(0, 0, 220, 300) };
            WheelBox chat = new WheelBox { Multiline = true, Bounds = new Rectangle(240, 0, 300, 300) };
            window.Controls.Add(other);
            window.Controls.Add(chat);
            window.Show();
            Application.DoEvents();

            Point selected = chat.PointToScreen(new Point(100, 90));
            Native.POINT point = new Native.POINT { x = selected.X, y = selected.Y };
            string error;
            // These disposable test controls intentionally live in this test process.
            FixedScrollTarget target = FixedScrollTarget.CaptureWindow(chat.Handle, point, -1, out error);
            Assert(target != null, "select chat control: " + error);

            cover.StartPosition = FormStartPosition.Manual;
            cover.Bounds = window.Bounds;
            cover.Show();
            Application.DoEvents();
            IntPtr foreground = GetForegroundWindow();
            Native.POINT cursorBefore, cursorAfter;
            Native.GetCursorPos(out cursorBefore);
            Assert(target.Scroll(-120, out error), "scroll covered target: " + error);
            Native.GetCursorPos(out cursorAfter);
            Assert(chat.Count == 1 && chat.Delta == -120 && other.Count == 0, "deliver wheel only to selected control");
            Assert(chat.LastPoint == selected, "use selected chat coordinates");
            Assert(cursorBefore.x == cursorAfter.x && cursorBefore.y == cursorAfter.y, "preserve cursor position");
            Assert(GetForegroundWindow() == foreground, "preserve foreground window");

            Assert(target.Scroll(120, out error), "scroll upward: " + error);
            Assert(chat.Count == 2 && chat.Delta == 0, "deliver one upward wheel event");
            window.Location = new Point(200, 150);
            selected = chat.PointToScreen(new Point(100, 90));
            Assert(target.Scroll(-120, out error) && chat.LastPoint == selected, "follow window movement");

            window.Width += 80;
            Assert(!target.Scroll(-120, out error), "stop after window resize");
            point = new Native.POINT { x = selected.X, y = selected.Y };
            target = FixedScrollTarget.CaptureWindow(chat.Handle, point, -1, out error);
            Assert(target != null, "select again after resize");
            window.WindowState = FormWindowState.Minimized;
            Assert(!target.Scroll(-120, out error), "stop when target is minimized");
            window.WindowState = FormWindowState.Normal;
            window.Close();
            Assert(!target.Scroll(-120, out error), "stop when target is closed");
            cover.Close();
        }
    }
}
