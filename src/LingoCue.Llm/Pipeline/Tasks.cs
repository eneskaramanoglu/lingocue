using LingoCue.Core.Subtitles;

namespace LingoCue.Llm.Pipeline;

/// <summary>
/// Fixes speech-recognition output in its own language: misheard words, proper nouns, punctuation,
/// spelling and casing. Never rewrites, summarizes or moves text between lines.
/// </summary>
public sealed class CorrectionTask(bool keepCircumflex = false) : LineTaskBase
{
    public override string Name => "Düzeltme";

    public override string SystemPrompt(TaskContext context)
    {
        var language = LanguageName(context.SourceLanguage);
        var turkish = context.SourceLanguage.StartsWith("tr", StringComparison.OrdinalIgnoreCase);
        return $"""
            You are a meticulous {language} subtitle proofreader. The lines come from automatic speech
            recognition of a video. Fix ONLY:
            - misrecognized words (use the context, the summary and the glossary to infer the intended word),
            - proper nouns and terms (follow the glossary exactly),
            - punctuation, capitalization and spelling according to standard {language} orthography{(turkish ? " (TDK)" : string.Empty)},
            - obvious duplicated words caused by recognition.
            Rules:
            - Keep the speaker's words, style, register and meaning. Do not rephrase, summarize, translate,
              censor or add content. Keep filler words only if they carry meaning.
            - Each line stays the same line: never move words between ids, never merge or split ids.
            - A line may contain a line break ("\n") for two-line subtitles; keep at most 2 lines.
            - If a line is already correct, return it unchanged.
            {(turkish ? TurkishRules(keepCircumflex) : string.Empty)}
            Video context:
            {ContextBlock(context.Project)}
            Glossary (terms marked [required] must be used exactly):
            {GlossaryBlock(context.Glossary, translation: false)}
            Answer with JSON only.
            """;
    }

    public override string ReflectPrompt(TaskContext context)
        => SystemPrompt(context) + """

            You now receive the source lines and a draft correction. Review the draft critically against
            the rules: undo any change that alters meaning or style, fix remaining recognition errors,
            punctuation and spelling. Return the final text for every id.
            """;

    public override bool NeedsReview(LineItem source, string output, PipelineOptions options)
        => TextDistance.ChangeRatio(source.Text, output) > options.ReviewChangeRatio;

    private static string TurkishRules(bool keepCircumflex) => $"""
        Turkish specifics:
        - Use correct dotted/dotless i (i/İ, ı/I) and ğ, ş, ç, ö, ü.
        - Apostrophe before suffixes of proper nouns (İstanbul'da, Boğaz'ın); "de/da" and "ki" spelled
          separately when they are conjunctions, attached when they are suffixes.
        - {(keepCircumflex ? "Keep circumflex spellings where standard (rüzgâr, kâr, hâlâ)." : "Use the common modern spellings without circumflex (rüzgar, kar, hala) unless the meaning requires it.")}
        - Questions end with "?"; use "mı/mi/mu/mü" spelled separately.

        """;
}

/// <summary>
/// Translates edited subtitle lines line-by-line (timing is copied from the source track by the caller).
/// Optimized for reading: natural, concise target-language subtitles rather than literal translation.
/// </summary>
public sealed class TranslationTask : LineTaskBase
{
    public override string Name => "Çeviri";

    public override string SystemPrompt(TaskContext context)
    {
        var source = LanguageName(context.SourceLanguage);
        var target = LanguageName(context.TargetLanguage ?? "en");
        return $"""
            You are a professional subtitle translator from {source} to {target}. Translate each line so it
            reads naturally as a {target} subtitle while keeping the meaning, tone and register of the video.
            Rules:
            - One output line per id; never move meaning between ids, never merge or split ids.
            - Subtitles must be readable: prefer concise, idiomatic wording over literal translation.
            - At most 2 lines per subtitle (use "\n" between them).
            - Keep names as in the glossary; follow glossary translations marked [required] exactly.
            - Do not translate song lyrics' rhyme literally; do not add explanations or notes.
            Video context:
            {ContextBlock(context.Project)}
            Glossary:
            {GlossaryBlock(context.Glossary, translation: true)}
            Answer with JSON only.
            """;
    }

    public override string ReflectPrompt(TaskContext context)
        => SystemPrompt(context) + """

            You now receive the source lines and a draft translation. Review it as a native editor: fix
            mistranslations, unnatural phrasing, inconsistent terminology and overly long lines. Return the
            final text for every id.
            """;

    public override string? Validate(LineItem source, string output, TaskContext context, PipelineOptions options)
    {
        var baseError = base.Validate(source, output, context, options);
        if (baseError is not null)
        {
            return baseError;
        }

        // A translation identical to a long source line usually means "not translated".
        return output.Trim().Length > 12 && string.Equals(output.Trim(), source.Text.Trim(), StringComparison.OrdinalIgnoreCase)
            ? $"Line {source.Id} was returned untranslated."
            : null;
    }
}

/// <summary>Shortens lines that read too fast, keeping the meaning (subtitle condensation).</summary>
public sealed class ShortenTask : LineTaskBase
{
    public override string Name => "Kısaltma";

    public override string SystemPrompt(TaskContext context)
    {
        var language = LanguageName(context.TargetLanguage ?? context.SourceLanguage);
        return $"""
            You condense {language} subtitles that are too long to read in their display time. For every
            line, keep the essential meaning and the speaker's voice, drop redundancy and filler, prefer
            shorter synonyms. Never change facts, names or numbers. Keep the language {language}.
            Each line's "maxChars" is a hard limit for its visible characters (line breaks excluded).
            Answer with JSON only.
            """;
    }

    public override string ReflectPrompt(TaskContext context) => SystemPrompt(context);

    public override string? Validate(LineItem source, string output, TaskContext context, PipelineOptions options)
    {
        var limit = MaxChars(source, options.Rules);
        var length = SubtitleText.VisibleLength(output);
        return length > limit + 3
            ? $"Line {source.Id} has {length} characters; shorten it to at most {limit}."
            : base.Validate(source, output, context, options);
    }

    internal static int MaxChars(LineItem line, ReadingRules rules)
        => Math.Max(8, (int)Math.Floor(line.Duration.TotalSeconds * rules.TargetCps));
}
