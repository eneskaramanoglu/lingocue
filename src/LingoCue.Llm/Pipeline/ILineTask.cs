using System.Globalization;
using System.Text;
using LingoCue.Core.Projects;
using LingoCue.Core.Text;

namespace LingoCue.Llm.Pipeline;

/// <summary>
/// What a line pass does (correct, translate). The batch runner owns batching, context, validation,
/// retries and concurrency; a task only supplies prompts and task-specific checks.
/// </summary>
public interface ILineTask
{
    string Name { get; }

    string SystemPrompt(TaskContext context);

    /// <summary>Prompt for the optional second "review and improve" pass.</summary>
    string ReflectPrompt(TaskContext context);

    /// <summary>Extra validation for one line; returns an English error the model can act on, or null.</summary>
    string? Validate(LineItem source, string output, TaskContext context, PipelineOptions options);

    /// <summary>Whether a valid output should still be flagged for human review.</summary>
    bool NeedsReview(LineItem source, string output, PipelineOptions options);
}

/// <summary>Shared prompt and validation building blocks.</summary>
public abstract class LineTaskBase : ILineTask
{
    public abstract string Name { get; }

    public abstract string SystemPrompt(TaskContext context);

    public abstract string ReflectPrompt(TaskContext context);

    public virtual string? Validate(LineItem source, string output, TaskContext context, PipelineOptions options)
    {
        if (string.IsNullOrWhiteSpace(output) && !string.IsNullOrWhiteSpace(source.Text))
        {
            return $"Line {source.Id} is empty; return the full line.";
        }

        var lines = output.Split('\n');
        if (lines.Length > options.Rules.MaxLines)
        {
            return $"Line {source.Id} has {lines.Length} lines; use at most {options.Rules.MaxLines}.";
        }

        return CheckGlossary(source, output, context);
    }

    public virtual bool NeedsReview(LineItem source, string output, PipelineOptions options) => false;

    /// <summary>Locked glossary terms present in the source must appear in their agreed form.</summary>
    protected virtual string? CheckGlossary(LineItem source, string output, TaskContext context)
    {
        var culture = TurkishText.Culture;
        foreach (var entry in context.Glossary.Where(e => e.Locked && !string.IsNullOrWhiteSpace(e.Effective)))
        {
            var inSource = culture.CompareInfo.IndexOf(source.Text, entry.Term, CompareOptions.IgnoreCase) >= 0;
            var inOutput = culture.CompareInfo.IndexOf(output, entry.Effective, CompareOptions.IgnoreCase) >= 0;
            if (inSource && !inOutput)
            {
                return $"Line {source.Id} must use the glossary form \"{entry.Effective}\" for \"{entry.Term}\".";
            }
        }

        return null;
    }

    protected static string GlossaryBlock(IEnumerable<GlossaryEntry> entries, bool translation)
    {
        var sb = new StringBuilder();
        foreach (var e in entries)
        {
            sb.Append("- ").Append(e.Term);
            if (!string.IsNullOrWhiteSpace(e.Preferred))
            {
                sb.Append(translation ? " → " : " (write as: ").Append(e.Preferred).Append(translation ? string.Empty : ")");
            }

            if (e.Locked)
            {
                sb.Append(" [required]");
            }

            if (!string.IsNullOrWhiteSpace(e.Note))
            {
                sb.Append(" — ").Append(e.Note);
            }

            sb.Append('\n');
        }

        return sb.Length == 0 ? "(none)\n" : sb.ToString();
    }

    protected static string ContextBlock(ProjectContext c)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(c.Summary))
        {
            sb.Append("Summary: ").Append(c.Summary).Append('\n');
        }

        if (!string.IsNullOrWhiteSpace(c.Tone))
        {
            sb.Append("Tone: ").Append(c.Tone).Append('\n');
        }

        if (!string.IsNullOrWhiteSpace(c.Address))
        {
            sb.Append("Form of address: ").Append(c.Address).Append('\n');
        }

        if (c.Speakers.Count > 0)
        {
            sb.Append("Speakers: ").Append(string.Join(", ", c.Speakers)).Append('\n');
        }

        return sb.Length == 0 ? "(unknown)\n" : sb.ToString();
    }

    protected static string LanguageName(string code) => code.Split('-')[0].ToLowerInvariant() switch
    {
        "tr" => "Turkish",
        "en" => "English",
        "de" => "German",
        "fr" => "French",
        "es" => "Spanish",
        "ar" => "Arabic",
        "ru" => "Russian",
        "ko" => "Korean",
        "ja" => "Japanese",
        "it" => "Italian",
        "az" => "Azerbaijani",
        _ => code,
    };
}
