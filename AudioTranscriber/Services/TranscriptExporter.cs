using System.Text;
using AudioTranscriber.Data;

namespace AudioTranscriber.Services;

// Turns stored segments into subtitle or plain text files
public static class TranscriptExporter
{
    public static readonly string[] SupportedFormats = { "srt", "vtt", "txt" };

    public static string ToSrt(List<TranscriptSegment> segments)
    {
        var builder = new StringBuilder();

        for (var i = 0; i < segments.Count; i++)
        {
            // SRT numbers each block from 1 and uses a comma before the milliseconds
            builder.AppendLine((i + 1).ToString());
            builder.AppendLine($"{FormatTime(segments[i].Start, ',')} --> {FormatTime(segments[i].End, ',')}");
            builder.AppendLine(segments[i].Text);
            builder.AppendLine();
        }

        return builder.ToString();
    }

    public static string ToVtt(List<TranscriptSegment> segments)
    {
        var builder = new StringBuilder();
        builder.AppendLine("WEBVTT");
        builder.AppendLine();

        foreach (var segment in segments)
        {
            // VTT uses a dot before the milliseconds
            builder.AppendLine($"{FormatTime(segment.Start, '.')} --> {FormatTime(segment.End, '.')}");
            builder.AppendLine(segment.Text);
            builder.AppendLine();
        }

        return builder.ToString();
    }

    public static string ToTxt(List<TranscriptSegment> segments)
    {
        var builder = new StringBuilder();

        foreach (var segment in segments)
        {
            builder.AppendLine(segment.Text);
        }

        return builder.ToString();
    }

    // Formats seconds as hh:mm:ss,mmm (SRT) or hh:mm:ss.mmm (VTT)
    private static string FormatTime(double seconds, char millisecondSeparator)
    {
        var time = TimeSpan.FromSeconds(seconds);
        return $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}{millisecondSeparator}{time.Milliseconds:000}";
    }
}
