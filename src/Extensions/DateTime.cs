using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace GitIrcBot
{
    public static class Extensions
    {
        public static string ToRelativeDate(this DateTime dateTime)
        {
            return dateTime.ToRelativeDate(DateTime.Now);
        }

        public static string ToRelativeDateUTC(this DateTime dateTime)
        {
            return dateTime.ToRelativeDate(DateTime.UtcNow);
        }

        public static string ToRelativeDate(this DateTime dateTime, DateTime currentTime)
        {
            var timeSpan = currentTime - dateTime;
            if (timeSpan <= TimeSpan.FromSeconds(60))
                return string.Format("{0} seconds ago", timeSpan.Seconds);

            if (timeSpan <= TimeSpan.FromMinutes(60))
                return timeSpan.Minutes > 1 ? String.Format("about {0} minutes ago", timeSpan.Minutes) : "about a minute ago";

            if (timeSpan <= TimeSpan.FromHours(24))
                return timeSpan.Hours > 1 ? String.Format("about {0} hours ago", timeSpan.Hours) : "about an hour ago";

            if (timeSpan <= TimeSpan.FromDays(30))
                return timeSpan.Days > 1 ? String.Format("about {0} days ago", timeSpan.Days) : "yesterday";

            if (timeSpan <= TimeSpan.FromDays(365))
                return timeSpan.Days > 30 ? String.Format("about {0} months ago", timeSpan.Days / 30) : "about a month ago";

            return timeSpan.Days > 365 ? String.Format("about {0} years ago", timeSpan.Days / 365) : "about a year ago";
        }
    }
}
