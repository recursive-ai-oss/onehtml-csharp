using System;
using System.Collections.Generic;
using RecursiveAI.OneHtml.Css;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Rules;

/// <summary>
/// The media rules. Whether a recording has audio, what it shows and whether its captions are right cannot be read from
/// markup, so these rules find the elements the question applies to and ask a person (needs-review). The one thing markup
/// can settle is the control for audio that starts by itself: native <c>controls</c> on the element.
/// </summary>
internal static class MediaRuleSet
{
    private const string VideoQuestion = "Check that ";

    internal static IEnumerable<IAccessibilityRule> Create()
    {
        // WCAG 1.2.2 Captions (Prerecorded); ACT eac66b. axe never fails this rule: it asks for review.
        yield return new MediaReviewRule("video-caption", "A video that has speech or sound must have captions.", "Add a captions track (track kind=captions) or burn captions into the video.", axe: true, ["eac66b"], [], ["video"],
            "Check that the video's speech and sounds are available as captions or a text alternative, if it has audio.", CaptionDetail);
        // WCAG 1.2.1 Audio-only (Prerecorded); ACT 2eb176 and afb423.
        yield return new MediaReviewRule("audio-caption", "Audio must have a transcript or another text alternative.", "Provide a transcript next to the audio, or link to one.", axe: true, ["2eb176", "afb423"], [], ["audio"],
            "Check that the audio has a transcript or other text alternative on the page.", null);
        yield return new AutoplayRule("no-autoplay-audio", "Audio or video that starts by itself and plays sound for more than three seconds must have a way to pause or silence it.", "Add the controls attribute, a pause button, or mute the media.", axe: true, ["80f0bf"], []);
        yield return new AutoplayRule("autoplay-audio-control", "Audio or video that starts by itself with sound must have a way to pause, stop or silence it.", "Add the controls attribute, or put a visible, named pause or mute button on the page.", axe: false, ["4c31df"], ["1.4.2"]);
        yield return new MediaReviewRule("autoplay-audio-duration", "Audio or video that starts by itself should not play sound for more than three seconds.", "Shorten the sound, mute the media, or do not autoplay it.", axe: false, ["aaa1bf"], ["1.4.2"], ["audio", "video"],
            "Check that the sound that plays by itself lasts no more than three seconds.", AutoplayDetail, requiresAutoplay: true);

        // ACT rules about video: each asks one question about what the recording needs.
        yield return new MediaReviewRule("video-captions", "A video with sound should have captions.", "Add a captions track or burn captions into the video.", axe: false, ["f51b46"], ["1.2.2"], ["video"],
            VideoQuestion + "the video's speech and sounds are captioned, if it has audio.", CaptionDetail);
        yield return new MediaReviewRule("video-only-alternative", "A video without sound should have a text or audio alternative.", "Add a transcript, a media alternative for text, or an audio track that describes the visuals.", axe: false, ["c3232f"], [], ["video"],
            VideoQuestion + "a video with no sound has a text alternative or an audio track that describes it.", null);
        yield return new MediaReviewRule("video-only-transcript", "A video without sound should have a transcript.", "Add a transcript that describes the visuals next to the video.", axe: false, ["ee13b5"], ["1.2.1"], ["video"],
            VideoQuestion + "a video with no sound has a transcript that describes what it shows.", null);
        yield return new MediaReviewRule("video-only-media-alternative", "A video without sound should have a media alternative for text.", "Add a media alternative, such as a described version, next to the video.", axe: false, ["fd26cf"], ["1.2.1"], ["video"],
            VideoQuestion + "a video with no sound has a media alternative for text.", null);
        yield return new MediaReviewRule("video-only-audio-track", "A video without sound should have an audio track that describes it.", "Add an audio recording that describes the visuals.", axe: false, ["d7ba54"], ["1.2.1"], ["video"],
            VideoQuestion + "a video with no sound has an audio track that describes what it shows.", null);
        yield return new MediaReviewRule("video-only-description-track", "A video without sound should have a description track.", "Add a track of kind=descriptions.", axe: false, ["ac7dc6"], ["1.2.1"], ["video"],
            VideoQuestion + "a video with no sound has a description track.", DescriptionDetail);
        yield return new MediaReviewRule("video-description-track", "A video should have a description track.", "Add a track of kind=descriptions.", axe: false, ["f196ce"], ["1.2.5"], ["video"],
            VideoQuestion + "the video has a description track for its visuals.", DescriptionDetail);
        yield return new MediaReviewRule("video-audio-description", "A video's visuals should have an audio description.", "Provide a version of the video with audio description, or an audio track that describes the visuals.", axe: false, ["1ea59c"], ["1.2.5"], ["video"],
            VideoQuestion + "the visuals of the video are described in audio.", null);
        yield return new MediaReviewRule("video-media-alternative", "A video should have a media alternative for text.", "Add a media alternative, such as a described version, next to the video.", axe: false, ["ab4d13"], ["1.2.3"], ["video"],
            VideoQuestion + "the video has a media alternative for text.", null);
        yield return new MediaReviewRule("video-transcript", "A video's audio and visuals should have a transcript.", "Add a transcript with both the speech and the descriptions of the visuals.", axe: false, ["1a02b0"], ["1.2.8"], ["video"],
            VideoQuestion + "the video has a transcript covering both its sound and its visuals.", null);
        yield return new MediaReviewRule("video-strict-alternative", "A video's visuals need an audio description and a media alternative.", "Provide audio description and a media alternative for text.", axe: false, ["1ec09b"], ["1.2.5"], ["video"],
            VideoQuestion + "the video has both an audio description and a media alternative for text.", null);
        yield return new MediaReviewRule("video-visual-alternative", "A video's visuals should have an accessible alternative.", "Provide an audio description, a transcript or a media alternative.", axe: false, ["c5a4ea"], ["1.2.3"], ["video"],
            VideoQuestion + "the video's visuals have an audio description, a transcript or a media alternative.", null);
        yield return new MediaReviewRule("audio-alternative", "Audio should have a text alternative.", "Provide a transcript or media alternative next to the audio.", axe: false, ["e7aa44"], ["1.2.1"], ["audio"],
            "Check that the audio has a transcript or a media alternative for text.", null);
    }

    private static string? CaptionDetail(Node element)
    {
        var kinds = new List<string>();
        foreach (Node c in element.Children)
        {
            if (c.IsElement && c.NamespaceURI == TextSupport.Html && c.LocalName == "track" && c.GetAttribute("kind") is { } kind)
            {
                kinds.Add(kind.Trim().ToLowerInvariantAscii());
            }
        }

        bool captions = kinds.Contains("captions") || kinds.Contains("subtitles");
        return captions ? "The video has a captions or subtitles track; check that it is accurate and complete." : "The video has no captions track; captions may be burned into the picture or given elsewhere.";
    }

    private static string? DescriptionDetail(Node element)
    {
        foreach (Node c in element.Children)
        {
            if (c.IsElement && c.NamespaceURI == TextSupport.Html && c.LocalName == "track" && c.GetAttribute("kind")?.Trim().ToLowerInvariantAscii() == "descriptions")
            {
                return "The video has a descriptions track; check that it describes what the picture shows.";
            }
        }

        return "The video has no descriptions track.";
    }

    private static string? AutoplayDetail(Node element) => element.HasAttribute("controls") ? "The element has native controls." : "The element has no native controls.";

    internal static string ToLowerInvariantAscii(this string s)
    {
        var chars = s.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (chars[i] is >= 'A' and <= 'Z')
            {
                chars[i] = (char)(chars[i] + 32);
            }
        }

        return new string(chars);
    }
}

// A rule that finds the audio or video elements it applies to and asks a person.
internal sealed class MediaReviewRule : AriaRule
{
    private readonly string[] _elements;
    private readonly string _message;
    private readonly Func<Node, string?>? _detail;
    private readonly bool _requiresAutoplay;

    public MediaReviewRule(string id, string description, string help, bool axe, string[] actIds, string[] criteria, string[] elements, string message, Func<Node, string?>? detail, bool requiresAutoplay = false)
        : base(id, description, help, null, "Whether captions, transcripts and descriptions exist and are right depends on the recording, which only a person can judge.", axe: axe, actIds: actIds, criteria: criteria)
    {
        _elements = elements;
        _message = message;
        _detail = detail;
        _requiresAutoplay = requiresAutoplay;
    }

    // ACT 80f0bf, aaa1bf, 4c31df: the element has an autoplay attribute and no muted attribute (HTML Standard 4.8.11.1, 4.8.11.8).
    internal static bool PlaysByItself(Node e) => e.HasAttribute("autoplay") && !e.HasAttribute("muted");

    // The element is shown: it has a tree node that is not hidden by display or the hidden attribute, and is not visibility hidden.
    internal static bool IsShown(CheckContext context, Node e) => TextSupport.IsDrawn(context, e, context.Styles.GetStyle(e));

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        int counter = 0;
        foreach (Node e in Elements(context))
        {
            if ((++counter & 255) == 0)
            {
                reporter.CancellationToken.ThrowIfCancellationRequested();
            }

            if (e.NamespaceURI != TextSupport.Html || Array.IndexOf(_elements, e.LocalName) < 0 || !IsShown(context, e) || (_requiresAutoplay && !PlaysByItself(e)))
            {
                continue;
            }

            string? detail = _detail?.Invoke(e);
            reporter.NeedsReview(e, _message, detail is null ? [] : [new Finding(null, null, detail)]);
        }
    }
}

// ACT 80f0bf (WCAG 1.4.2 Audio Control), 4c31df: audio or video that plays by itself needs a way to pause, stop or silence it.
// Native controls are such a way. Without them the page may have its own button, or the sound may be short or absent: a person decides.
internal sealed class AutoplayRule : AriaRule
{
    public AutoplayRule(string id, string description, string help, bool axe, string[] actIds, string[] criteria)
        : base(id, description, help, null, "Adding controls, muting or removing autoplay changes how the media behaves, which is the author's decision.", axe: axe, actIds: actIds, criteria: criteria)
    {
    }

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        int counter = 0;
        foreach (Node e in Elements(context))
        {
            if ((++counter & 255) == 0)
            {
                reporter.CancellationToken.ThrowIfCancellationRequested();
            }

            if (e.NamespaceURI != TextSupport.Html || e.LocalName is not ("audio" or "video") || !MediaReviewRule.PlaysByItself(e) || !MediaReviewRule.IsShown(context, e))
            {
                continue;
            }

            if (e.HasAttribute("controls"))
            {
                reporter.Passed(e, "The element starts by itself and has native controls to pause or silence it.");
            }
            else
            {
                reporter.NeedsReview(e, "The element starts by itself and has no native controls. Check that the page has a visible, named control to pause or silence it, unless it has no sound or the sound lasts three seconds or less.");
            }
        }
    }
}
