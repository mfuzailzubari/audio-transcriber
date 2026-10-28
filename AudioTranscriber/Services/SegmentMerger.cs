namespace AudioTranscriber.Services;

// Joins the segments of each chunk into one transcript for the whole file.
// Chunks overlap by a few seconds, so text near the boundaries is heard twice and has to be removed once.
public static class SegmentMerger
{
    public static void AddChunk(List<TranscribedSegment> merged, List<TranscribedSegment> chunkSegments,
        double chunkStartSeconds)
    {
        // Audio at the very end of a chunk is often cut mid-word and misheard.
        // The new chunk hears that same audio with more context, so drop what the previous chunk said there.
        merged.RemoveAll(s => s.Start >= chunkStartSeconds);

        foreach (var segment in chunkSegments)
        {
            // Chunk timestamps start at 0, so move them to their position in the full file
            var shifted = new TranscribedSegment
            {
                Start = segment.Start + chunkStartSeconds,
                End = segment.End + chunkStartSeconds,
                Text = segment.Text.Trim()
            };

            var last = merged.Count > 0 ? merged[^1] : null;

            // Only a segment that overlaps the previous one in time can contain repeated words
            if (last != null && shifted.Start < last.End)
            {
                shifted.Text = RemoveRepeatedWords(last.Text, shifted.Text);
                shifted.Start = last.End;
            }

            if (string.IsNullOrWhiteSpace(shifted.Text) || shifted.End <= shifted.Start)
            {
                continue;
            }

            merged.Add(shifted);
        }
    }

    // If the start of "next" repeats the end of "previous", remove those words from "next".
    // Example: "...the new product launch" + "product launch is in March" => "is in March"
    private static string RemoveRepeatedWords(string previous, string next)
    {
        var previousWords = previous.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var nextWords = next.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Try the longest possible overlap first
        for (var count = Math.Min(previousWords.Length, nextWords.Length); count > 0; count--)
        {
            var matches = true;
            for (var i = 0; i < count; i++)
            {
                var previousWord = CleanWord(previousWords[previousWords.Length - count + i]);
                var nextWord = CleanWord(nextWords[i]);
                if (previousWord != nextWord)
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                return string.Join(' ', nextWords.Skip(count));
            }
        }

        return next;
    }

    // Lower case without punctuation, so "March." and "march" count as the same word
    private static string CleanWord(string word)
    {
        return new string(word.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }
}
