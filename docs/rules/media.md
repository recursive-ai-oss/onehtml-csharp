# Media rules

Whether a recording has sound, what it shows and whether its captions are right cannot be read from markup. These rules find the
`audio` and `video` elements a question applies to and answer **needs-review**; they never fail. axe-core's three media
rules also only ask for review. None has a suggested edit.

An element is considered when it is drawn: not `display: none`, `hidden` or `visibility: hidden`.

| Rule | Source | Elements | Question |
|---|---|---|---|
| `video-caption` | axe, ACT eac66b (1.2.2) | video | captions or another alternative for the sound; says whether a captions/subtitles `track` exists |
| `audio-caption` | axe, ACT 2eb176, afb423 (1.2.1) | audio | a transcript or other text alternative |
| `no-autoplay-audio` | axe, ACT 80f0bf (1.4.2) | audio, video with `autoplay` and no `muted` | passed with native `controls`; otherwise needs-review |
| `autoplay-audio-control` | ACT 4c31df | same | same as above |
| `autoplay-audio-duration` | ACT aaa1bf | same | does the sound last more than three seconds |
| `video-captions` | ACT f51b46 | video | captions for the sound |
| `video-only-alternative`, `video-only-transcript`, `video-only-media-alternative`, `video-only-audio-track`, `video-only-description-track` | ACT c3232f, ee13b5, fd26cf, d7ba54, ac7dc6 | video | alternatives for a video without sound |
| `video-description-track`, `video-audio-description`, `video-media-alternative`, `video-transcript`, `video-strict-alternative`, `video-visual-alternative` | ACT f196ce, 1ea59c, ab4d13, 1a02b0, 1ec09b, c5a4ea | video | alternatives for the visuals |
| `audio-alternative` | ACT e7aa44 | audio | a transcript or media alternative |

## Differences from the ACT rules

- ACT decides some cases by what the media contains (a silent video is inapplicable; a video with audio and no captions fails). Markup cannot
  say, so those cases are needs-review. The ACT test cases of these rules are counted as "judgement" in the test harness: a
  passed or failed case may come out as needs-review, never as the opposite verdict.
- A video with no captions `track` is not failed: captions may be burned into the picture (ACT f51b46 Passed Example 1). This is where ACT
  and axe agree, since axe also only asks.
- Native `controls` pass the autoplay control questions (ACT 4c31df Passed Examples 1 and 2). A page-level custom control cannot be recognised and is left to review.
- The deprecated ACT rules f196ce and ac7dc6 are implemented because their test cases are still published.
