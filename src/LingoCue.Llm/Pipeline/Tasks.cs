using System.Globalization;
using LingoCue.Core.Subtitles;
using LingoCue.Core.Text;

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
        var language = context.IsMixed
            ? "multilingual (" + string.Join(", ", context.SourceLanguages!.Select(LanguageName)) + ")"
            : LanguageName(context.SourceLanguage);
        var turkish = context.IsMixed
            ? context.SourceLanguages!.Contains("tr")
            : context.SourceLanguage.StartsWith("tr", StringComparison.OrdinalIgnoreCase);
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
            {MixedRule(context)}
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

    /// <summary>Lines of a mixed-language video each stay in their own language.</summary>
    internal static string MixedRule(TaskContext context)
        => context.IsMixed
            ? """
              - The video mixes languages. Each line has "lang", the language it is spoken in: correct it with
                that language's spelling and punctuation, and NEVER translate a line into another language.
                Language-specific rules below apply only to lines in that language.
              """
            : string.Empty;

    public override string? Validate(LineItem source, string output, TaskContext context, PipelineOptions options)
    {
        var baseError = base.Validate(source, output, context, options);
        if (baseError is not null || !context.IsMixed || source.Language is null)
        {
            return baseError;
        }

        // The source reads as its language but the answer as another one: the model translated it.
        var before = TextLanguageGuess.Guess(source.Text, context.SourceLanguages!);
        var after = TextLanguageGuess.Guess(output, context.SourceLanguages!);
        return before == source.Language && after is not null && after != source.Language
            ? $"Line {source.Id} was translated; keep it in its own language ({source.Language})."
            : null;
    }

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
        var source = context.IsMixed
            ? "several languages (" + string.Join(", ", context.SourceLanguages!.Select(LanguageName)) + "; each line's \"lang\" says which)"
            : LanguageName(context.SourceLanguage);
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
            - "text" is the edited, authoritative line: translate it. Some lines also carry "original", what
              speech recognition heard in the source audio; it may contain recognition errors. Use it only to
              resolve ambiguity, never to overrule "text".
            {PronounRule(context)}
            {TargetRules(context)}
            Video context:
            {ContextBlock(context.Project)}
            Glossary:
            {GlossaryBlock(context.Glossary, translation: true)}
            Answer with JSON only.
            """;
    }

    /// <summary>
    /// Turkish "o" (and its forms) has no gender; English and many other targets need one. Without a
    /// rule models guess "he" and guess inconsistently.
    /// </summary>
    internal static string PronounRule(TaskContext context)
        => context.SourceLanguage.StartsWith("tr", StringComparison.OrdinalIgnoreCase)
            ? """
              - Turkish third-person pronouns have no gender. Infer he/she/they from the video context, the
                speakers list and glossary notes; keep the choice consistent for the same person. When it is
                truly unknown, prefer wording that needs no gendered pronoun (name, role, or "they").
              """
            : string.Empty;

    /// <summary>Conventions of the target language that models get wrong without being told.</summary>
    internal static string TargetRules(TaskContext context)
        => IsKorean(context.TargetLanguage)
            ? """
              - Korean subtitles: use the polite informal style (해요체) for dialogue by default. Use 합니다체 for
                narration, presentations or a formal "Form of address", and 반말 only when the context shows close
                friends or family talking. Keep the same speech level between the same people.
              - Write people's and places' names in Hangul with a consistent standard transliteration (follow the
                glossary when it gives a form); keep brand names that are normally written in Latin letters.
              - Follow standard Korean spacing (띄어쓰기). Keep lines short: about 16 Korean characters per line,
                where Latin letters, digits, spaces and punctuation count half.
              """
            : string.Empty;

    internal static bool IsKorean(string? language) => language is not null && language.StartsWith("ko", StringComparison.OrdinalIgnoreCase);

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
            Each line's "maxChars" is a hard limit for its visible characters (line breaks excluded).{(TranslationTask.IsKorean(context.TargetLanguage ?? context.SourceLanguage)
                ? " Count Hangul as 1 and Latin letters, digits, spaces and punctuation as 0.5."
                : string.Empty)}
            Answer with JSON only.
            """;
    }

    public override string ReflectPrompt(TaskContext context) => SystemPrompt(context);

    public override string? Validate(LineItem source, string output, TaskContext context, PipelineOptions options)
    {
        var limit = MaxChars(source, options.Rules);
        var length = options.Rules.TextLength(output);
        return length > limit + 3
            ? string.Create(CultureInfo.InvariantCulture, $"Line {source.Id} has {length:0.#} characters; shorten it to at most {limit}.")
            : base.Validate(source, output, context, options);
    }

    internal static int MaxChars(LineItem line, ReadingRules rules)
        => Math.Max(8, (int)Math.Floor(line.Duration.TotalSeconds * rules.TargetCps));
}
