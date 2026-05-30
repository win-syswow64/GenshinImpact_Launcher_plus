using System;
using System.Collections.ObjectModel;
using System.Globalization;
using GenShin_Launcher_Plus.Models;

namespace GenShin_Launcher_Plus.ViewModels
{
    public class ScreenshotDayGroup
    {
        public DateTime Date { get; }
        public string DateHeader { get; }
        public string CountText { get; }
        public ObservableCollection<ScreenshotItem> Items { get; } = new();

        public ScreenshotDayGroup(DateTime date, int count)
        {
            Date = date.Date;
            DateHeader = FormatDateHeader(date.Date);
            CountText = $"{count}\u5F20\u7167\u7247"; // "N张照片"
        }

        private static string FormatDateHeader(DateTime date)
        {
            var today = DateTime.Today;
            var culture = CultureInfo.CurrentCulture;

            if (date == today)
                return "\u4ECA\u5929"; // 今天
            if (date == today.AddDays(-1))
                return "\u6628\u5929"; // 昨天

            int daysDiff = (today - date).Days;
            if (daysDiff < 7)
            {
                // "N天前" format
                return $"{daysDiff}\u5929\u524D";
            }

            if (date.Year == today.Year)
            {
                // "1月15日 周三"
                var dayOfWeek = date.DayOfWeek switch
                {
                    DayOfWeek.Monday => "\u5468\u4E00",
                    DayOfWeek.Tuesday => "\u5468\u4E8C",
                    DayOfWeek.Wednesday => "\u5468\u4E09",
                    DayOfWeek.Thursday => "\u5468\u56DB",
                    DayOfWeek.Friday => "\u5468\u4E94",
                    DayOfWeek.Saturday => "\u5468\u516D",
                    DayOfWeek.Sunday => "\u5468\u65E5",
                    _ => ""
                };
                return $"{date.Month}\u6708{date.Day}\u65E5 {dayOfWeek}";
            }

            // "2023年12月25日 周一"
            var dow = date.DayOfWeek switch
            {
                DayOfWeek.Monday => "\u5468\u4E00",
                DayOfWeek.Tuesday => "\u5468\u4E8C",
                DayOfWeek.Wednesday => "\u5468\u4E09",
                DayOfWeek.Thursday => "\u5468\u56DB",
                DayOfWeek.Friday => "\u5468\u4E94",
                DayOfWeek.Saturday => "\u5468\u516D",
                DayOfWeek.Sunday => "\u5468\u65E5",
                _ => ""
            };
            return $"{date.Year}\u5E74{date.Month}\u6708{date.Day}\u65E5 {dow}";
        }
    }
}