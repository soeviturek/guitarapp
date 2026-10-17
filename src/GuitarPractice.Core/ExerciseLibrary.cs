namespace GuitarPractice.Core;

public static class ExerciseLibrary
{
    private static readonly int[] PositionStarts = [0, 5, 10, 15, 20];

    public static IReadOnlyList<PracticeExercise> All { get; } = Array.AsReadOnly(
        new[]
        {
            CreateDiatonicArpeggios(),
            CreateScale(
                "c-major", "C 大调音阶 · 全指板", 0, [0, 2, 4, 5, 7, 9, 11],
                "C D E F G A B，上下行覆盖 0–24 品的五个区域。标准调弦，4/4 拍，八分音符。"),
            CreateScale(
                "a-minor-pentatonic", "A 小调五声音阶 · 全指板", 9, [0, 3, 5, 7, 10],
                "A C D E G，上下行覆盖 0–24 品的五个区域。标准调弦，4/4 拍，八分音符。"),
            CreateChromatic()
        });

    private static PracticeExercise CreateDiatonicArpeggios()
    {
        var chords = new (string Name, int Root, int[] Intervals)[]
        {
            ("C", 0, [0, 4, 7]),
            ("Dm", 2, [0, 3, 7]),
            ("Em", 4, [0, 3, 7]),
            ("F", 5, [0, 4, 7]),
            ("G", 7, [0, 4, 7]),
            ("Am", 9, [0, 3, 7]),
            ("Bdim", 11, [0, 3, 6])
        };
        var measures = new List<ExerciseMeasure>();
        foreach (var start in PositionStarts)
        {
            foreach (var chord in chords)
            {
                AppendPhrase(
                    measures,
                    $"{chord.Name} · {start}–{start + 4} 品",
                    CreatePositionPhrase(chord.Root, chord.Intervals, start));
            }
        }

        return new PracticeExercise(
            "c-diatonic-arpeggios",
            "C 大调顺阶琶音 · 全指板",
            "C → Dm → Em → F → G → Am → Bdim，在五个区域练完 0–24 品。每段从根音出发，"
                + "上下行后回到根音；段尾留休止。标准调弦，4/4 拍，八分音符。",
            measures);
    }

    private static PracticeExercise CreateScale(
        string id,
        string name,
        int root,
        int[] intervals,
        string description)
    {
        var measures = new List<ExerciseMeasure>();
        foreach (var start in PositionStarts)
        {
            AppendPhrase(measures, $"{start}–{start + 4} 品", CreatePositionPhrase(root, intervals, start));
        }

        return new PracticeExercise(id, name, description, measures);
    }

    private static List<FretNote> CreatePositionPhrase(int root, int[] intervals, int startFret)
    {
        var pitches = intervals.Select(interval => (root + interval) % 12).ToHashSet();
        var notes = Enumerable.Range(1, 6)
            .Reverse()
            .SelectMany(stringNumber => Enumerable.Range(startFret, 5)
                .Select(fret => new FretNote(stringNumber, fret)))
            .Where(note => pitches.Contains(note.MidiNote % 12))
            .OrderBy(note => note.MidiNote)
            .ThenByDescending(note => note.StringNumber)
            .DistinctBy(note => note.MidiNote)
            .ToList();
        var rootIndex = notes.FindIndex(note => note.MidiNote % 12 == root);
        if (rootIndex < 0)
        {
            throw new InvalidOperationException("The position does not contain the requested root.");
        }

        return notes.Skip(rootIndex)
            .Concat(notes.AsEnumerable().Reverse().Skip(1))
            .Concat(notes.Skip(1).Take(rootIndex))
            .ToList();
    }

    private static PracticeExercise CreateChromatic()
    {
        var measures = new List<ExerciseMeasure>();
        for (var start = 1; start <= 21; start += 4)
        {
            var ascending = Enumerable.Range(1, 6)
                .Reverse()
                .SelectMany(stringNumber => Enumerable.Range(start, 4)
                    .Select(fret => new FretNote(stringNumber, fret)))
                .ToList();
            AppendPhrase(
                measures,
                $"1-2-3-4 · {start}–{start + 3} 品",
                ascending.Concat(ascending.AsEnumerable().Reverse().Skip(1)).ToList());
        }

        return new PracticeExercise(
            "chromatic-four-fingers",
            "半音阶爬格子 · 1–24 品",
            "每根弦连续四品，按 1-2-3-4 指上下行；每段换一个区域。标准调弦，4/4 拍，八分音符。",
            measures);
    }

    private static void AppendPhrase(
        List<ExerciseMeasure> measures,
        string label,
        IReadOnlyList<FretNote> phrase)
    {
        for (var start = 0; start < phrase.Count; start += ExerciseMeasure.StepsPerMeasure)
        {
            var notes = new FretNote?[ExerciseMeasure.StepsPerMeasure];
            for (var index = 0; index < notes.Length && start + index < phrase.Count; index++)
            {
                notes[index] = phrase[start + index];
            }

            measures.Add(new ExerciseMeasure(label, notes));
        }
    }
}
