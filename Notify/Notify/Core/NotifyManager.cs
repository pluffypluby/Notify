using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Notify
{
    public static class NotifyManager
    {
        private static List<NotifyForm> notifications = new List<NotifyForm>();
        private static int maxCount = 5;
        private static int margin = 20;
        private static int spacing = 5;
        private static Point basePos;

        static NotifyManager()
        {
            var screen = Screen.PrimaryScreen.WorkingArea;
            basePos = new Point(screen.Right - 340 - margin, screen.Bottom - margin - 70);
        }

        public static void ShowNotification(string message, string appName = "Notify",
            float durationSeconds = 3f,
            NotifyState state = NotifyState.Success)
        {
            Color color;
            switch (state)
            {
                case NotifyState.Success: color = NotifyColors.Success; break;
                case NotifyState.Warning: color = NotifyColors.Warning; break;
                case NotifyState.Error: color = NotifyColors.Error; break;
                default: color = Color.White; break;
            }
            ShowNotification(message, appName, durationSeconds, color);
        }

        public static void ShowNotification(string message, string appName = "Notify",
            float durationSeconds = 3f,
            Color progressColor = default)
        {
            if (progressColor == default)
                progressColor = Color.White;

            if (notifications.Count >= maxCount)
            {
                var oldest = notifications[0];
                notifications.RemoveAt(0);
                oldest.StartClosing();
            }

            var newNote = new NotifyForm(message, appName, durationSeconds, progressColor);
            newNote.FormClosed += (s, e) =>
            {
                notifications.Remove(newNote);
                RecalculatePositions();
            };

            notifications.Add(newNote);
            RecalculatePositions();

            Point start = new Point(Screen.PrimaryScreen.WorkingArea.Right + 340, basePos.Y);
            newNote.ShowAnimated(start, basePos);
        }

        private static void RecalculatePositions()
        {
            int count = notifications.Count;
            for (int i = 0; i < count; i++)
            {
                int y = basePos.Y - (count - 1 - i) * (70 + spacing);
                notifications[i].SetTarget(new Point(basePos.X, y));
            }
        }
    }
}