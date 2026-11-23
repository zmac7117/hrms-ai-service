using System;
using System.Collections.Generic;
using System.Text.Json;

// Fake attendance data for testing the AI side without the HRMS.
// In Program.cs, in the RunAction handler, answer "get_attendance_summary" with FakeAttendance.Data() (or Json()).
internal static class FakeAttendance
{
    public static string Json() => JsonSerializer.Serialize(Data());

    public static object Data()
    {
        var today = DateTime.Today;
        var days = new List<object>();
        for (var d = new DateTime(today.Year, today.Month, 1); d <= today; d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) days.Add(Row(d, "Weekly Off", null, null, null));
            else if (d == today) days.Add(Row(d, "Present", "09:05", null, null));          // not checked out yet
            else if (d.Day == 9) days.Add(Row(d, "Leave", null, null, null));
            else if (d.Day == 12) days.Add(Row(d, "Absent", null, null, null));
            else if (d.Day == 16) days.Add(Row(d, "Half Day", "09:10", "13:30", 4.33));
            else
            {
                var i = 540 + d.Day * 7 % 25;
                var o = 1080 + d.Day * 11 % 40;
                days.Add(Row(d, "Present", Hm(i), Hm(o), Math.Round((o - i) / 60.0, 2)));
            }
        }
        return new { month = today.ToString("yyyy-MM"), today = today.ToString("yyyy-MM-dd"), days };
    }

    private static string Hm(int m) => $"{m / 60:00}:{m % 60:00}";

    private static object Row(DateTime d, string status, string? checkIn, string? checkOut, double? hours) =>
        new { date = d.ToString("yyyy-MM-dd"), status, checkIn, checkOut, hours };
}